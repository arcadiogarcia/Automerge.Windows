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

        // ─── Actor ──────────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMget_actor(
            IntPtr doc, ref IntPtr out_bytes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMset_actor(
            IntPtr doc, byte* bytes, nuint len);

        // ─── Fine-grained read ──────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget(
            IntPtr doc, string? objId, string key,
            ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget_idx(
            IntPtr doc, string? objId, nuint index,
            ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMkeys(
            IntPtr doc, string? objId,
            ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMlength(
            IntPtr doc, string? objId, ref nuint out_n);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget_text(
            IntPtr doc, string objId,
            ref IntPtr out_text, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget_all(
            IntPtr doc, string? objId, string key,
            ref IntPtr out_json, ref nuint out_len);

        // ─── Fine-grained write ─────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMput(
            IntPtr doc, string? objId, string key, string scalarJson);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMput_idx(
            IntPtr doc, string? objId, nuint index, string scalarJson);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMput_object(
            IntPtr doc, string? objId, string key, string objType,
            ref IntPtr out_new_obj_id, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMdelete(
            IntPtr doc, string? objId, string key);

        // ─── List operations ────────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMinsert(
            IntPtr doc, string objId, nuint index, string scalarJson);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMinsert_object(
            IntPtr doc, string objId, nuint index, string objType,
            ref IntPtr out_new_obj_id, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMdelete_at(
            IntPtr doc, string objId, nuint index);

        // ─── Counter ────────────────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMput_counter(
            IntPtr doc, string? objId, string key, long initial);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMincrement(
            IntPtr doc, string? objId, string key, long delta);

        // ─── Text ───────────────────────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMsplice_text(
            IntPtr doc, string objId, nuint start, nint deleteCount, string? text);

        // ─── Fork and incremental save ──────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMfork(
            IntPtr doc, ref IntPtr out_doc);

        [LibraryImport(Lib)] internal static partial int AMsave_incremental(
            IntPtr doc, ref IntPtr out_bytes, ref nuint out_len);

        // ─── Commit with metadata ───────────────────────────────────────────
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMcommit(
            IntPtr doc, string? message, long timestamp);

        // ─── Diff ───────────────────────────────────────────────────────────
        [LibraryImport(Lib)] internal static partial int AMdiff_incremental(
            IntPtr doc, ref IntPtr out_json, ref nuint out_len);

        // ─── New APIs ───────────────────────────────────────────────────────

        [LibraryImport(Lib)] internal static partial int AMget_all_changes(
            IntPtr doc, ref IntPtr out_changes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMget_last_local_change(
            IntPtr doc, ref IntPtr out_bytes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMget_missing_deps(
            IntPtr doc, byte* heads, nuint heads_len,
            ref IntPtr out_heads, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMempty_change(
            IntPtr doc, string? message, long timestamp);

        [LibraryImport(Lib)] internal static partial int AMsave_after(
            IntPtr doc, byte* heads, nuint heads_len,
            ref IntPtr out_bytes, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMload_incremental(
            IntPtr doc, byte* data, nuint len);

        [LibraryImport(Lib)] internal static partial int AMfork_at(
            IntPtr doc, byte* heads, nuint heads_len, ref IntPtr out_doc);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMobject_type(
            IntPtr doc, string? obj_id, ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMdiff(
            IntPtr doc,
            byte* before_heads, nuint before_heads_len,
            byte* after_heads, nuint after_heads_len,
            ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMupdate_text(
            IntPtr doc, string obj_id, string new_text);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMmark(
            IntPtr doc, string obj_id,
            nuint start, nuint end,
            string name, string value_json, byte expand);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMunmark(
            IntPtr doc, string obj_id,
            string name, nuint start, nuint end, byte expand);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMmarks(
            IntPtr doc, string obj_id, ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMmarks_at(
            IntPtr doc, string obj_id,
            byte* heads, nuint heads_len,
            ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget_cursor(
            IntPtr doc, string obj_id, nuint position,
            byte* heads, nuint heads_len,
            ref IntPtr out_cursor, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMget_cursor_position(
            IntPtr doc, string obj_id, string cursor_str,
            byte* heads, nuint heads_len,
            ref nuint out_position);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMspans(
            IntPtr doc, string obj_id, ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib)] internal static partial int AMstats(
            IntPtr doc, ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMmap_range(
            IntPtr doc, string? obj_id, string? start, string? end,
            ref IntPtr out_json, ref nuint out_len);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int AMlist_range(
            IntPtr doc, string obj_id, nuint start, nuint end,
            ref IntPtr out_json, ref nuint out_len);

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
