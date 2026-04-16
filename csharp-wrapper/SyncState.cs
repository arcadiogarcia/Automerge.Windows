using System;

namespace Automerge.Windows
{
    /// <summary>
    /// Maintains the state of a sync session with one remote peer.
    ///
    /// Each unique peer requires its own <see cref="SyncState"/> instance.
    /// Keep it alive for the duration of the relationship so that only
    /// incremental changes are transmitted.
    /// </summary>
    public sealed class SyncState : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        /// <summary>Create a fresh sync state.</summary>
        public SyncState()
        {
            _handle = NativeMethods.AMcreate_sync_state();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create SyncState.");
        }

        private SyncState(IntPtr handle)
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
                    NativeMethods.AMfree_sync_state(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        // ─── Protocol ─────────────────────────────────────────────────────────

        /// <summary>
        /// Generate the next message to send to the remote peer.
        /// Returns an empty array if sync is complete.
        /// </summary>
        public byte[] GenerateSyncMessage(Document doc)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ThrowIfDisposed();
            doc.ThrowIfDisposedInternal();

            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMgenerate_sync_message(
                    doc.Handle, _handle, ref ptr, ref len));
            return NativeMethods.ReadAndFree(ptr, len);
        }

        /// <summary>
        /// Process a message received from the remote peer.
        /// </summary>
        public void ReceiveSyncMessage(Document doc, ReadOnlySpan<byte> message)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ThrowIfDisposed();
            doc.ThrowIfDisposedInternal();

            unsafe
            {
                fixed (byte* m = message)
                {
                    NativeMethods.CheckResult(
                        NativeMethods.AMreceive_sync_message(
                            doc.Handle, _handle, m, (nuint)message.Length));
                }
            }
        }

        // ─── Persistence ──────────────────────────────────────────────────────

        /// <summary>Serialize the sync state for optional persistence.</summary>
        public byte[] Save()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes(
                (ref IntPtr ptr, ref nuint len) =>
                    NativeMethods.AMsave_sync_state(_handle, ref ptr, ref len));
        }

        /// <summary>Load a sync state from persisted bytes.</summary>
        public static SyncState Load(ReadOnlySpan<byte> data)
        {
            IntPtr state = IntPtr.Zero;
            int rc;
            unsafe
            {
                fixed (byte* p = data)
                {
                    rc = NativeMethods.AMload_sync_state(p, (nuint)data.Length, ref state);
                }
            }
            NativeMethods.CheckResult(rc);
            return new SyncState(state);
        }

        /// <summary>
        /// Check if the remote peer (represented by this sync state) has all of our local changes.
        /// </summary>
        public bool HasOurChanges(Document doc)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ThrowIfDisposed();
            doc.ThrowIfDisposedInternal();

            int result = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMhas_our_changes(doc.Handle, _handle, ref result));
            return result != 0;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
