using System;
using System.Runtime.InteropServices;

namespace Automerge.Windows
{
    /// <summary>P/Invoke declarations for automerge_core.dll.</summary>
    internal static unsafe partial class NativeMethods
    {
        private const string Lib = "automerge_core";

        // ─── Document lifecycle ──────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial IntPtr AMcreate_doc();
        [LibraryImport(Lib)] internal static partial void AMdestroy_doc(IntPtr doc);

        // ─── Persistence ────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMsave(
            IntPtr doc, ref IntPtr out_bytes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMload(
            byte* data, nuint len, ref IntPtr out_doc);

        // ─── Heads ──────────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMget_heads(
            IntPtr doc, ref IntPtr out_heads, ref nuint out_len);

        // ─── Changes ────────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMget_changes(
            IntPtr doc, byte* heads, nuint heads_len,
            ref IntPtr out_changes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMapply_changes(
            IntPtr doc, byte* changes, nuint len);

        // ─── Merge ──────────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMmerge(
            IntPtr dest, IntPtr src);

        // ─── Read ───────────────────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget_value(
            IntPtr doc, string? pathJson,
            ref IntPtr out_json, ref nuint out_len);

        // ─── Write ──────────────────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMput_json_root(
            IntPtr doc, string jsonObj);

        // ─── Sync ───────────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial IntPtr AMcreate_sync_state();
        [LibraryImport(Lib)] internal static partial void AMfree_sync_state(IntPtr state);

        [LibraryImport(Lib)] internal static partial int AMgenerate_sync_message(
            IntPtr doc, IntPtr state,
            ref IntPtr out_msg, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMreceive_sync_message(
            IntPtr doc, IntPtr state, byte* msg, nuint len);

        [LibraryImport(Lib)] internal static partial int AMsave_sync_state(
            IntPtr state, ref IntPtr out_bytes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMload_sync_state(
            byte* data, nuint len, ref IntPtr out_state);

        // ─── Error handling ─────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial nuint AMget_last_error(
            byte* buf, nuint buf_len);

        // ─── Memory management ──────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial void AMfree_bytes(
            IntPtr data, nuint len);

        // ─── Helpers ────────────────────────────────────────────────────────

        internal delegate int BytesDelegate(
            ref IntPtr ptr, ref nuint len);

        /// <summary>
        /// Invoke a C API function that writes (ptr, len), copy to byte[],
        /// free the native buffer, and return the array.
        /// </summary>
        internal static byte[] InvokeWithBytes(BytesDelegate fn)
        {
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            CheckResult(fn(ref ptr, ref len));
            return ReadAndFree(ptr, len);
        }

        /// <summary>Copy (ptr, len) to byte[] and free the native buffer.</summary>
        internal static byte[] ReadAndFree(IntPtr ptr, nuint len)
        {
            if (ptr == IntPtr.Zero || len == 0)
            {
                if (ptr != IntPtr.Zero) AMfree_bytes(ptr, 0);
                return Array.Empty<byte>();
            }
            var result = new byte[(int)len];
            Marshal.Copy(ptr, result, 0, (int)len);
            AMfree_bytes(ptr, len);
            return result;
        }

        /// <summary>
        /// Throw <see cref="AutomergeNativeException"/> if rc != AM_OK.
        /// </summary>
        internal static void CheckResult(int rc)
        {
            if (rc == 0) return;

            // Read the last error into a stack buffer
            Span<byte> buf = stackalloc byte[512];
            nuint written;
            fixed (byte* p = buf)
            {
                written = AMget_last_error(p, (nuint)buf.Length);
            }
            var msg = System.Text.Encoding.UTF8.GetString(
                buf[..(int)Math.Max(0, (int)written - 1)]);
            throw new AutomergeNativeException(msg);
        }
    }
}
