using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Automerge.Windows
{
    /// <summary>
    /// C# convenience wrapper around the native Automerge document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Object IDs: The document root is represented as <c>""</c> (or pass
    /// <see langword="null"/>).  Nested objects created by
    /// <see cref="PutObject"/> / <see cref="InsertObject"/> return an opaque
    /// string of the form <c>"counter@hexactor"</c> that you pass back as
    /// <c>objId</c> for further operations.
    /// </para>
    /// <para>
    /// Scalar JSON: Methods that take <c>scalarJson</c> expect a JSON-encoded
    /// scalar: <c>"\"hello\""</c>, <c>"42"</c>, <c>"true"</c>, <c>"null"</c>.
    /// </para>
    /// </remarks>
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

        /// <summary>
        /// Save only the changes that have not yet been saved (incremental).
        /// Much faster than <see cref="Save"/> when most of the document is unchanged.
        /// </summary>
        public byte[] SaveIncremental()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes(
                (ref IntPtr ptr, ref nuint len) =>
                    NativeMethods.AMsave_incremental(_handle, ref ptr, ref len));
        }

        // ─── Fork ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fork (clone) the document, producing an independent copy with the
        /// same state and history.
        /// </summary>
        public Document Fork()
        {
            ThrowIfDisposed();
            IntPtr newHandle = IntPtr.Zero;
            NativeMethods.CheckResult(NativeMethods.AMfork(_handle, ref newHandle));
            return new Document(newHandle);
        }

        // ─── Heads ────────────────────────────────────────────────────────────

        /// <summary>Get the current heads as packed 32-byte SHA-256 hashes.</summary>
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
        /// Pass an empty/default span to get all changes.
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

        /// <summary>Apply a binary changes buffer (from <see cref="GetChanges"/>).</summary>
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

        // ─── Actor ────────────────────────────────────────────────────────────

        /// <summary>Get the actor ID for this document as raw bytes.</summary>
        public byte[] GetActorId()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes(
                (ref IntPtr ptr, ref nuint len) =>
                    NativeMethods.AMget_actor(_handle, ref ptr, ref len));
        }

        /// <summary>Set the actor ID for this document.</summary>
        public void SetActorId(ReadOnlySpan<byte> actorId)
        {
            ThrowIfDisposed();
            unsafe
            {
                fixed (byte* p = actorId)
                {
                    NativeMethods.CheckResult(
                        NativeMethods.AMset_actor(_handle, p, (nuint)actorId.Length));
                }
            }
        }

        // ─── Read — legacy ────────────────────────────────────────────────────

        /// <summary>
        /// Read a value by JSON-array path (serialises entire document first).
        /// For individual field access prefer <see cref="Get"/>.
        /// </summary>
        public string GetValue(string pathJson = "[]")
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            int rc = NativeMethods.AMget_value(_handle, pathJson, ref ptr, ref len);
            NativeMethods.CheckResult(rc);
            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, (int)len);
            NativeMethods.AMfree_bytes(ptr, len + 1);
            return Encoding.UTF8.GetString(bytes);
        }

        // ─── Read — fine-grained ──────────────────────────────────────────────

        /// <summary>
        /// Get a single value from a map object by key.
        /// Returns JSON: a scalar, or <c>{"_obj_id":"…","_obj_type":"map|list|text"}</c>
        /// for nested objects.
        /// </summary>
        /// <param name="objId">Object ID; <c>null</c> or <c>""</c> for root.</param>
        public string Get(string? objId, string key)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMget(_handle, objId.AsNullableC(), key, ref ptr, ref len));
        }

        /// <summary>Get a single value from a list/text object by index.</summary>
        public string GetIdx(string objId, int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMget_idx(_handle, objId.AsNullableC(), (nuint)index, ref ptr, ref len));
        }

        /// <summary>
        /// Get the keys of a map object as a JSON array.
        /// Example: <c>["title","author","rating"]</c>
        /// </summary>
        /// <param name="objId"><c>null</c> or <c>""</c> for root.</param>
        public string GetKeysJson(string? objId = null)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMkeys(_handle, objId.AsNullableC(), ref ptr, ref len));
        }

        /// <summary>Get the keys of a map object as a string array.</summary>
        public string[] GetKeys(string? objId = null)
        {
            var json = GetKeysJson(objId);
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }

        /// <summary>Get the number of elements/keys in an object.</summary>
        public int GetLength(string? objId = null)
        {
            ThrowIfDisposed();
            nuint n = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMlength(_handle, objId.AsNullableC(), ref n));
            return (int)n;
        }

        /// <summary>Get the text content of a text CRDT object.</summary>
        public string GetText(string objId)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMget_text(_handle, objId, ref ptr, ref len));
        }

        /// <summary>
        /// Get all concurrent values for a key (for conflict resolution).
        /// Returns a JSON array.
        /// </summary>
        public string GetAllJson(string? objId, string key)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMget_all(_handle, objId.AsNullableC(), key, ref ptr, ref len));
        }

        // ─── Write — legacy ───────────────────────────────────────────────────

        /// <summary>
        /// Set scalar key-value pairs at the document root from a JSON object string.
        /// </summary>
        public void PutJsonRoot(string jsonObj)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(NativeMethods.AMput_json_root(_handle, jsonObj));
        }

        // ─── Write — fine-grained ─────────────────────────────────────────────

        /// <summary>
        /// Set a scalar value in a map object by key.
        /// </summary>
        /// <param name="objId"><c>null</c> or <c>""</c> for root.</param>
        /// <param name="key">Map key.</param>
        /// <param name="scalarJson">JSON scalar, e.g. <c>"\"hello\""</c> or <c>"42"</c>.</param>
        public void Put(string? objId, string key, string scalarJson)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMput(_handle, objId.AsNullableC(), key, scalarJson));
        }

        /// <summary>Set a scalar value in a list object at an index.</summary>
        public void PutIdx(string objId, int index, string scalarJson)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMput_idx(_handle, objId.AsNullableC(), (nuint)index, scalarJson));
        }

        /// <summary>
        /// Create a nested map, list, or text object at a map key.
        /// </summary>
        /// <param name="objId">Parent object ID (null/"" for root).</param>
        /// <param name="key">Map key for the new object.</param>
        /// <param name="objType"><c>"map"</c>, <c>"list"</c>, or <c>"text"</c>.</param>
        /// <returns>The new object's ID to use for further operations.</returns>
        public string PutObject(string? objId, string key, string objType)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMput_object(_handle, objId.AsNullableC(), key, objType,
                                           ref ptr, ref len));
            return ReadCString(ptr, len);
        }

        /// <summary>Delete a key from a map object.</summary>
        /// <param name="objId">Object ID (null/"" for root).</param>
        public void Delete(string? objId, string key)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMdelete(_handle, objId.AsNullableC(), key));
        }

        // ─── List operations ──────────────────────────────────────────────────

        /// <summary>
        /// Insert a scalar value into a list at <paramref name="index"/>.
        /// Use <see cref="int.MaxValue"/> to append.
        /// </summary>
        public void Insert(string listObjId, int index, string scalarJson)
        {
            ThrowIfDisposed();
            nuint idx = (index < 0 || index == int.MaxValue) ? nuint.MaxValue : (nuint)index;
            NativeMethods.CheckResult(
                NativeMethods.AMinsert(_handle, listObjId, idx, scalarJson));
        }

        /// <summary>
        /// Insert a nested object into a list at <paramref name="index"/>.
        /// Use <see cref="int.MaxValue"/> to append.
        /// </summary>
        public string InsertObject(string listObjId, int index, string objType)
        {
            ThrowIfDisposed();
            nuint idx = (index < 0 || index == int.MaxValue) ? nuint.MaxValue : (nuint)index;
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMinsert_object(_handle, listObjId, idx, objType,
                                              ref ptr, ref len));
            return ReadCString(ptr, len);
        }

        /// <summary>Delete the element at <paramref name="index"/> from a list.</summary>
        public void DeleteAt(string listObjId, int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMdelete_at(_handle, listObjId, (nuint)index));
        }

        // ─── Counter ──────────────────────────────────────────────────────────

        /// <summary>Create a counter scalar at a map key with an initial value.</summary>
        public void PutCounter(string? objId, string key, long initialValue = 0)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMput_counter(_handle, objId.AsNullableC(), key, initialValue));
        }

        /// <summary>Increment (or decrement) a counter at a map key by <paramref name="delta"/>.</summary>
        public void Increment(string? objId, string key, long delta)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMincrement(_handle, objId.AsNullableC(), key, delta));
        }

        // ─── Text CRDT ────────────────────────────────────────────────────────

        /// <summary>
        /// Insert/delete characters in a text CRDT object.
        /// </summary>
        /// <param name="textObjId">Text object ID returned by <see cref="PutObject"/> with "text".</param>
        /// <param name="start">Character index to begin editing.</param>
        /// <param name="deleteCount">Number of characters to delete (0 = insert only).</param>
        /// <param name="text">UTF-16 text to insert (null/"" = delete only).</param>
        public void SpliceText(string textObjId, int start, int deleteCount, string? text = null)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMsplice_text(_handle, textObjId, (nuint)start, (nint)deleteCount, text));
        }

        // ─── Commit ───────────────────────────────────────────────────────────

        /// <summary>
        /// Commit pending changes with an optional message and/or timestamp.
        /// Normally you do not need to call this — each mutation auto-commits.
        /// Use this when you want a labelled change after a series of mutations.
        /// </summary>
        /// <param name="message">Optional commit message.</param>
        /// <param name="timestamp">Unix epoch seconds (0 = omit).</param>
        public void Commit(string? message = null, long timestamp = 0)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(NativeMethods.AMcommit(_handle, message, timestamp));
        }

        // ─── Diff / patches ───────────────────────────────────────────────────

        /// <summary>
        /// Get a JSON array of patches describing all changes since the last
        /// call to this method.  Useful for driving UI update notifications.
        /// </summary>
        public string DiffIncremental()
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMdiff_incremental(_handle, ref ptr, ref len));
        }

        // ─── New APIs: closing gap with JS @automerge/automerge ───────────────

        /// <summary>Get ALL changes as concatenated bytes (JS getAllChanges).</summary>
        public byte[] GetAllChanges()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMget_all_changes(_handle, ref ptr, ref len));
        }

        /// <summary>Get the binary representation of the last locally-made change.</summary>
        public byte[] GetLastLocalChange()
        {
            ThrowIfDisposed();
            return NativeMethods.InvokeWithBytes((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMget_last_local_change(_handle, ref ptr, ref len));
        }

        /// <summary>Get missing dependency hashes needed to reach <paramref name="heads"/>.</summary>
        public unsafe byte[] GetMissingDeps(ReadOnlySpan<byte> heads)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMget_missing_deps(_handle, h, (nuint)heads.Length, ref ptr, ref len));
            }
            return NativeMethods.ReadAndFree(ptr, len);
        }

        /// <summary>Create an empty change (no operations).</summary>
        public void EmptyChange(string? message = null, long timestamp = 0)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMempty_change(_handle, message, timestamp));
        }

        /// <summary>Save only changes since <paramref name="heads"/> (JS saveSince).</summary>
        public unsafe byte[] SaveAfter(ReadOnlySpan<byte> heads)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMsave_after(_handle, h, (nuint)heads.Length, ref ptr, ref len));
            }
            return NativeMethods.ReadAndFree(ptr, len);
        }

        /// <summary>Load incremental changes into this document (JS loadIncremental).</summary>
        public unsafe void LoadIncremental(ReadOnlySpan<byte> data)
        {
            ThrowIfDisposed();
            fixed (byte* d = data)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMload_incremental(_handle, d, (nuint)data.Length));
            }
        }

        /// <summary>Fork at specific heads (snapshot at a point in history).</summary>
        public unsafe Document ForkAt(ReadOnlySpan<byte> heads)
        {
            ThrowIfDisposed();
            IntPtr outDoc = IntPtr.Zero;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMfork_at(_handle, h, (nuint)heads.Length, ref outDoc));
            }
            return new Document(outDoc);
        }

        /// <summary>Get the object type ("map", "list", "text").</summary>
        public string ObjectType(string? objId = null)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMobject_type(_handle, objId.AsNullableC(), ref ptr, ref len));
        }

        /// <summary>Diff between two sets of heads. Returns JSON patch array.</summary>
        public unsafe string Diff(ReadOnlySpan<byte> beforeHeads, ReadOnlySpan<byte> afterHeads)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            fixed (byte* b = beforeHeads)
            fixed (byte* a = afterHeads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMdiff(_handle,
                        b, (nuint)beforeHeads.Length,
                        a, (nuint)afterHeads.Length,
                        ref ptr, ref len));
            }
            if (ptr == IntPtr.Zero) return "[]";
            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, (int)len);
            NativeMethods.AMfree_bytes(ptr, len + 1);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>Update a text object by diffing old vs new text (JS updateText).</summary>
        public void UpdateText(string objId, string newText)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMupdate_text(_handle, objId, newText));
        }

        /// <summary>Add a rich text mark. expand: 0=none, 1=before, 2=after, 3=both.</summary>
        public void Mark(string objId, int start, int end,
                         string name, string valueJson, byte expand = 3)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMmark(_handle, objId,
                    (nuint)start, (nuint)end, name, valueJson, expand));
        }

        /// <summary>Remove a mark. expand: 0=none, 1=before, 2=after, 3=both.</summary>
        public void Unmark(string objId, string name, int start, int end, byte expand = 3)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMunmark(_handle, objId, name,
                    (nuint)start, (nuint)end, expand));
        }

        /// <summary>Get all marks on a text object. Returns JSON array.</summary>
        public string GetMarks(string objId)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMmarks(_handle, objId, ref ptr, ref len));
        }

        /// <summary>Get marks at specific heads. Returns JSON array.</summary>
        public unsafe string GetMarksAt(string objId, ReadOnlySpan<byte> heads)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMmarks_at(_handle, objId, h, (nuint)heads.Length,
                        ref ptr, ref len));
            }
            if (ptr == IntPtr.Zero) return "[]";
            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, (int)len);
            NativeMethods.AMfree_bytes(ptr, len + 1);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>Get a cursor for a position in a text object.</summary>
        public unsafe string GetCursor(string objId, int position,
                                       ReadOnlySpan<byte> heads = default)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMget_cursor(_handle, objId, (nuint)position,
                        h, (nuint)heads.Length, ref ptr, ref len));
            }
            return ReadCString(ptr, len);
        }

        /// <summary>Resolve a cursor to a position.</summary>
        public unsafe int GetCursorPosition(string objId, string cursor,
                                            ReadOnlySpan<byte> heads = default)
        {
            ThrowIfDisposed();
            nuint pos = 0;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMget_cursor_position(_handle, objId, cursor,
                        h, (nuint)heads.Length, ref pos));
            }
            return (int)pos;
        }

        /// <summary>Get rich text spans. Returns JSON array.</summary>
        public string GetSpans(string objId)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMspans(_handle, objId, ref ptr, ref len));
        }

        /// <summary>Get document statistics as JSON.</summary>
        public string GetStats()
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMstats(_handle, ref ptr, ref len));
        }

        /// <summary>Get map entries in a key range. null = unbounded.</summary>
        public string MapRange(string? objId = null, string? start = null, string? end = null)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMmap_range(_handle, objId.AsNullableC(),
                    start, end, ref ptr, ref len));
        }

        /// <summary>Get list entries in an index range.</summary>
        public string ListRange(string objId, int start, int end)
        {
            ThrowIfDisposed();
            return ReadJson((ref IntPtr ptr, ref nuint len) =>
                NativeMethods.AMlist_range(_handle, objId,
                    (nuint)start, (nuint)end, ref ptr, ref len));
        }

        // ─── Block marker APIs ────────────────────────────────────────────────

        /// <summary>Insert a block marker at index in a text object. Returns the block's object ID.</summary>
        public string SplitBlock(string textObjId, int index)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMsplit_block(_handle, textObjId, (nuint)index, ref ptr, ref len));
            return ReadCString(ptr, len);
        }

        /// <summary>Remove the block marker at index from a text object.</summary>
        public void JoinBlock(string textObjId, int index)
        {
            ThrowIfDisposed();
            NativeMethods.CheckResult(
                NativeMethods.AMjoin_block(_handle, textObjId, (nuint)index));
        }

        /// <summary>Replace the block marker at index. Returns the new block's object ID.</summary>
        public string ReplaceBlock(string textObjId, int index)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            NativeMethods.CheckResult(
                NativeMethods.AMreplace_block(_handle, textObjId, (nuint)index, ref ptr, ref len));
            return ReadCString(ptr, len);
        }

        // ─── Additional gap-closing APIs ─────────────────────────────────────

        /// <summary>Look up a specific change by its 32-byte hash.</summary>
        public unsafe byte[] GetChangeByHash(ReadOnlySpan<byte> hash)
        {
            ThrowIfDisposed();
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            fixed (byte* h = hash)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMget_change_by_hash(_handle, h, (nuint)hash.Length, ref ptr, ref len));
            }
            return NativeMethods.ReadAndFree(ptr, len);
        }

        /// <summary>Check whether the document contains all the given heads.</summary>
        public unsafe bool HasHeads(ReadOnlySpan<byte> heads)
        {
            ThrowIfDisposed();
            int result = 0;
            fixed (byte* h = heads)
            {
                NativeMethods.CheckResult(
                    NativeMethods.AMhas_heads(_handle, h, (nuint)heads.Length, ref result));
            }
            return result != 0;
        }

        // ─── Internal ─────────────────────────────────────────────────────────

        internal IntPtr Handle => _handle;

        internal void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(_disposed, this);

        // Read NUL-terminated JSON from (ptr, len).  Frees with len+1.
        private static string ReadJson(NativeMethods.BytesDelegate fn)
        {
            IntPtr ptr = IntPtr.Zero;
            nuint len = 0;
            NativeMethods.CheckResult(fn(ref ptr, ref len));
            if (ptr == IntPtr.Zero) return "null";
            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, (int)len);
            NativeMethods.AMfree_bytes(ptr, len + 1);
            return Encoding.UTF8.GetString(bytes);
        }

        // Read a NUL-terminated C string (object ID). Frees with len+1.
        private static string ReadCString(IntPtr ptr, nuint len)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, (int)len);
            NativeMethods.AMfree_bytes(ptr, len + 1);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>Extension helpers for calling the native API.</summary>
    internal static class StringExtensions
    {
        /// <summary>Return null for null/empty strings (maps to C NULL pointer).</summary>
        internal static string? AsNullableC(this string? s) =>
            string.IsNullOrEmpty(s) ? null : s;
    }
}
