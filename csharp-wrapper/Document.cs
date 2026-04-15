using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Automerge.Windows
{
    /// <summary>
    /// C# convenience wrapper around the native Automerge document.
    ///
    /// This class marshals calls across the P/Invoke boundary directly to the
    /// Rust C ABI (automerge_core.dll), converting between <c>byte[]</c> /
    /// <c>string</c> and the raw pointer/length pairs used by the C API.
    /// </summary>
    public sealed class Document : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        /// <summary>Create a new empty Automerge document.</summary>
        public Document()
        {
            _handle = NativeMethods.AMcreate_doc();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create Automerge document.");
        }

        private Document(IntPtr handle)
        {
            _handle = handle;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeMethods.AMdestroy_doc(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        // ─── Persistence ──────────────────────────────────────────────────────

        /// <summary>Serialize the document to bytes.</summary>
        public byte[] Save()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes(
                (ref IntPtr ptr, ref nuint len) =>
                    NativeMethods.AMsave(_handle, ref ptr, ref len));
        }

        /// <summary>Load a document from bytes produced by <see cref="Save"/>.</summary>
        public static Document Load(ReadOnlySpan<byte> data)
        {
            IntPtr doc = IntPtr.Zero;
            int rc;
            unsafe
            {
                fixed (byte* p = data)
                {
                    rc = NativeMethods.AMload(p, (nuint)data.Length, ref doc);
                }
            }
            NativeMethods.CheckResult(rc);
            return new Document(doc);
        }

        // ─── Heads ────────────────────────────────────────────────────────────

        /// <summary>
        /// Get the current heads as packed 32-byte SHA-256 hashes.
        /// </summary>
        public byte[] GetHeads()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes(
                (ref IntPtr ptr, ref nuint len) =>
                    NativeMethods.AMget_heads(_handle, ref ptr, ref len));
        }

        // ─── Changes ──────────────────────────────────────────────────────────

        /// <summary>
        /// Get all changes since <paramref name="heads"/>.
        /// Pass an empty array or <c>null</c> to get all changes from genesis.
        /// </summary>
        public byte[] GetChanges(ReadOnlySpan<byte> heads = default)
        {
            ThrowIfDisposed();
            unsafe
            {
                fixed (byte* h = heads)
                {
                    IntPtr ptr = IntPtr.Zero;
                    nuint len = 0;
                    int rc = NativeMethods.AMget_changes(
                        _handle, h, (nuint)heads.Length, ref ptr, ref len);
                    NativeMethods.CheckResult(rc);
                    return NativeMethods.ReadAndFree(ptr, len);
                }
            }
        }

        /// <summary>
        /// Apply a binary changes buffer (from <see cref="GetChanges"/>) to
        /// this document.
        /// </summary>
        public void ApplyChanges(ReadOnlySpan<byte> changes)
        {
            ThrowIfDisposed();
            unsafe
            {
                fixed (byte* c = changes)
                {
                    NativeMethods.CheckResult(
                        NativeMethods.AMapply_changes(_handle, c, (nuint)changes.Length));
                }
            }
        }

        // ─── Merge ────────────────────────────────────────────────────────────

        /// <summary>Merge all changes from <paramref name="other"/> into this document.</summary>
        public void Merge(Document other)
        {
            ArgumentNullException.ThrowIfNull(other);
            ThrowIfDisposed();
            other.ThrowIfDisposed();
            NativeMethods.CheckResult(NativeMethods.AMmerge(_handle, other._handle));
        }

        // ─── Read ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Read a value by JSON-array path.
        /// Pass <c>null</c> or <c>"[]"</c> to serialise the entire root object.
        /// </summary>
        public string GetValue(string pathJson = "[]")
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            int rc = NativeMethods.AMget_value(_handle, pathJson, ref ptr, ref len);
            NativeMethods.CheckResult(rc);
            // ptr is NUL-terminated; len excludes NUL
            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, (int)len);
            NativeMethods.AMfree_bytes(ptr, len + 1);
            return Encoding.UTF8.GetString(bytes);
        }

        // ─── Write ────────────────────────────────────────────────────────────

        /// <summary>
        /// Set scalar key-value pairs from a JSON object string.
        /// Example: <c>PutJsonRoot("{\"name\":\"Alice\",\"age\":30}")</c>
        /// </summary>
        public void PutJsonRoot(string jsonObj)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(NativeMethods.AMput_json_root(_handle, jsonObj));
        }

        // ─── Internal ─────────────────────────────────────────────────────────

        internal IntPtr Handle => _handle;

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
