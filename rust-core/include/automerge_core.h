#pragma once
#ifndef AUTOMERGE_CORE_H
#define AUTOMERGE_CORE_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ─── Opaque types ──────────────────────────────────────────────────────────────

/// Opaque handle for an Automerge document.
typedef struct AMdoc AMdoc;

/// Opaque handle for an Automerge sync state (one instance per peer).
typedef struct AMsync_state AMsync_state;

// ─── Return codes ─────────────────────────────────────────────────────────────
#define AM_OK  0
#define AM_ERR 1

// ─── Document lifecycle ───────────────────────────────────────────────────────

/// Create a new empty Automerge document.
/// Ownership is transferred to the caller; free with AMdestroy_doc.
AMdoc* AMcreate_doc(void);

/// Destroy a document and free its memory.
void AMdestroy_doc(AMdoc* doc);

// ─── Persistence ─────────────────────────────────────────────────────────────

/// Serialize the document to bytes.
/// @param doc     The document to save.
/// @param out_bytes  Receives heap-allocated byte buffer (free with AMfree_bytes).
/// @param out_len    Receives byte count.
/// @return AM_OK on success, AM_ERR on failure.
int AMsave(AMdoc* doc, uint8_t** out_bytes, size_t* out_len);

/// Load a document from bytes produced by AMsave.
/// @param data     Pointer to byte buffer.
/// @param len      Byte count.
/// @param out_doc  Receives heap-allocated document handle (free with AMdestroy_doc).
/// @return AM_OK on success, AM_ERR on failure.
int AMload(const uint8_t* data, size_t len, AMdoc** out_doc);

// ─── Heads ────────────────────────────────────────────────────────────────────

/// Get the current heads (frontier) as packed 32-byte SHA-256 hashes.
/// @param doc       The document.
/// @param out_heads Receives packed hash bytes (n_heads * 32); free with AMfree_bytes.
/// @param out_len   Receives total byte count.
/// @return AM_OK on success.
int AMget_heads(AMdoc* doc, uint8_t** out_heads, size_t* out_len);

// ─── Changes ─────────────────────────────────────────────────────────────────

/// Get all changes produced since the given heads.
/// @param doc         The document.
/// @param heads       Packed 32-byte hashes of known heads (may be NULL / 0 for all).
/// @param heads_len   Total byte count of heads (multiple of 32).
/// @param out_changes Receives concatenated change bytes; free with AMfree_bytes.
/// @param out_len     Receives byte count.
/// @return AM_OK on success.
int AMget_changes(AMdoc* doc,
                  const uint8_t* heads, size_t heads_len,
                  uint8_t** out_changes, size_t* out_len);

/// Apply a binary changes buffer to the document.
/// @param doc     The document.
/// @param changes Concatenated raw change bytes (from AMget_changes).
/// @param len     Byte count.
/// @return AM_OK on success.
int AMapply_changes(AMdoc* doc, const uint8_t* changes, size_t len);

// ─── Merge ───────────────────────────────────────────────────────────────────

/// Merge all changes from src into dest.  Both must be valid.
int AMmerge(AMdoc* dest, AMdoc* src);

// ─── Read ─────────────────────────────────────────────────────────────────────

/// Read a document value by JSON path.
/// @param doc       The document (const).
/// @param path_json UTF-8 JSON array string, e.g. "[\"name\"]".
///                  NULL or "[]" → serialise entire root.
/// @param out_json  Receives NUL-terminated JSON bytes; free with AMfree_bytes(ptr, len+1).
/// @param out_len   Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMget_value(const AMdoc* doc,
                const char* path_json,
                uint8_t** out_json, size_t* out_len);

// ─── Write ────────────────────────────────────────────────────────────────────

/// Set scalar key-value pairs in the document root.
/// @param doc      The document.
/// @param json_obj UTF-8 JSON object, e.g. "{\"name\":\"Alice\"}".
///                 Only scalar values are accepted.
/// @return AM_OK on success.
int AMput_json_root(AMdoc* doc, const char* json_obj);

// ─── Actor ────────────────────────────────────────────────────────────────────

/// Get the actor ID for this document as raw bytes.
/// @param doc       The document.
/// @param out_bytes Receives heap-allocated actor bytes; free with AMfree_bytes.
/// @param out_len   Receives byte count.
/// @return AM_OK on success.
int AMget_actor(const AMdoc* doc, uint8_t** out_bytes, size_t* out_len);

/// Set the actor ID for this document.
/// @param doc   The document.
/// @param bytes Actor ID bytes.
/// @param len   Byte count.
/// @return AM_OK on success.
int AMset_actor(AMdoc* doc, const uint8_t* bytes, size_t len);

// ─── Fine-grained read ────────────────────────────────────────────────────────

/// Read a single value from a map object by key.
/// @param doc       The document.
/// @param obj_id    Object ID string (NULL or "" for root, "counter@hex" otherwise).
/// @param key       Map key (UTF-8).
/// @param out_json  Receives NUL-terminated JSON; free with AMfree_bytes(ptr, len+1).
/// @param out_len   Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMget(const AMdoc* doc, const char* obj_id, const char* key,
          uint8_t** out_json, size_t* out_len);

/// Read a single value from a list/text object by index.
/// @param doc      The document.
/// @param obj_id   Object ID string.
/// @param index    Zero-based index.
/// @param out_json Receives NUL-terminated JSON; free with AMfree_bytes(ptr, len+1).
/// @param out_len  Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMget_idx(const AMdoc* doc, const char* obj_id, size_t index,
              uint8_t** out_json, size_t* out_len);

/// Get the keys of a map object as a JSON array of strings.
/// @param doc      The document.
/// @param obj_id   Object ID (NULL/"" for root).
/// @param out_json Receives NUL-terminated JSON array; free with AMfree_bytes(ptr, len+1).
/// @param out_len  Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMkeys(const AMdoc* doc, const char* obj_id,
           uint8_t** out_json, size_t* out_len);

/// Get the number of elements/keys in an object.
/// @param doc    The document.
/// @param obj_id Object ID.
/// @param out_n  Receives the count.
/// @return AM_OK on success.
int AMlength(const AMdoc* doc, const char* obj_id, size_t* out_n);

/// Get the text content of a text object.
/// @param doc      The document.
/// @param obj_id   Object ID of the text object.
/// @param out_text Receives NUL-terminated UTF-8; free with AMfree_bytes(ptr, len+1).
/// @param out_len  Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMget_text(const AMdoc* doc, const char* obj_id,
               uint8_t** out_text, size_t* out_len);

// ─── Fine-grained write ──────────────────────────────────────────────────────

/// Set a scalar value in a map object by key.
/// @param doc         The document.
/// @param obj_id      Object ID (NULL/"" for root).
/// @param key         Map key (UTF-8).
/// @param scalar_json JSON scalar string, e.g. "\"hello\"", "42", "true", "null".
/// @return AM_OK on success.
int AMput(AMdoc* doc, const char* obj_id, const char* key, const char* scalar_json);

/// Set a scalar value in a list object at an index (overwrites existing item).
/// @param doc         The document.
/// @param obj_id      List object ID.
/// @param index       Zero-based index.
/// @param scalar_json JSON scalar string.
/// @return AM_OK on success.
int AMput_idx(AMdoc* doc, const char* obj_id, size_t index, const char* scalar_json);

/// Create a nested object (map, list, or text) at a map key.
/// @param doc               The document.
/// @param obj_id            Parent object ID.
/// @param key               Map key.
/// @param obj_type          "map", "list", or "text".
/// @param out_new_obj_id    Receives NUL-terminated new object ID; free with AMfree_bytes(ptr, len+1).
/// @param out_new_obj_id_len Receives byte count of the ID excluding NUL.
/// @return AM_OK on success.
int AMput_object(AMdoc* doc, const char* obj_id, const char* key, const char* obj_type,
                 uint8_t** out_new_obj_id, size_t* out_new_obj_id_len);

/// Delete a key from a map object.
/// @param doc    The document.
/// @param obj_id Map object ID.
/// @param key    Key to delete.
/// @return AM_OK on success.
int AMdelete(AMdoc* doc, const char* obj_id, const char* key);

// ─── List operations ─────────────────────────────────────────────────────────

/// Insert a scalar value into a list at an index.
/// Pass SIZE_MAX as index to append.
/// @param doc         The document.
/// @param obj_id      List object ID.
/// @param index       Insertion position (or SIZE_MAX to append).
/// @param scalar_json JSON scalar.
/// @return AM_OK on success.
int AMinsert(AMdoc* doc, const char* obj_id, size_t index, const char* scalar_json);

/// Insert a nested object into a list at an index.
/// @param doc               The document.
/// @param obj_id            List object ID.
/// @param index             Insertion position (or SIZE_MAX to append).
/// @param obj_type          "map", "list", or "text".
/// @param out_new_obj_id    Receives new object ID; free with AMfree_bytes(ptr, len+1).
/// @param out_new_obj_id_len Receives string length excluding NUL.
/// @return AM_OK on success.
int AMinsert_object(AMdoc* doc, const char* obj_id, size_t index, const char* obj_type,
                    uint8_t** out_new_obj_id, size_t* out_new_obj_id_len);

/// Delete the element at an index from a list object.
/// @param doc    The document.
/// @param obj_id List object ID.
/// @param index  Zero-based index to delete.
/// @return AM_OK on success.
int AMdelete_at(AMdoc* doc, const char* obj_id, size_t index);

// ─── Counter ─────────────────────────────────────────────────────────────────

/// Create a counter scalar at a map key with an initial value.
/// @param doc     The document.
/// @param obj_id  Map object ID.
/// @param key     Map key.
/// @param initial Initial counter value.
/// @return AM_OK on success.
int AMput_counter(AMdoc* doc, const char* obj_id, const char* key, int64_t initial);

/// Increment (or decrement) a counter at a map key by delta.
/// @param doc    The document.
/// @param obj_id Map object ID.
/// @param key    Map key pointing to a counter.
/// @param delta  Amount to add (negative to decrement).
/// @return AM_OK on success.
int AMincrement(AMdoc* doc, const char* obj_id, const char* key, int64_t delta);

// ─── Text ────────────────────────────────────────────────────────────────────

/// Insert/delete characters in a text object.
/// @param doc          The document.
/// @param obj_id       Text object ID.
/// @param start        Character index to start editing.
/// @param delete_count Number of characters to delete (0 for insert-only).
/// @param text         UTF-8 text to insert (may be NULL or "" for delete-only).
/// @return AM_OK on success.
int AMsplice_text(AMdoc* doc, const char* obj_id,
                  size_t start, ptrdiff_t delete_count, const char* text);

// ─── Fork and incremental save ───────────────────────────────────────────────

/// Fork (clone) the document, producing an independent copy with the same state.
/// Caller frees with AMdestroy_doc.
/// @param doc     The source document.
/// @param out_doc Receives a newly allocated document handle.
/// @return AM_OK on success.
int AMfork(AMdoc* doc, AMdoc** out_doc);

/// Save only the changes that have not yet been saved.
/// @param doc       The document.
/// @param out_bytes Receives incremental bytes; free with AMfree_bytes.
/// @param out_len   Receives byte count.
/// @return AM_OK on success.
int AMsave_incremental(AMdoc* doc, uint8_t** out_bytes, size_t* out_len);

// ─── Commit with metadata ─────────────────────────────────────────────────────

/// Commit pending changes with an optional message and/or timestamp.
/// @param doc       The document.
/// @param message   Optional commit message (may be NULL).
/// @param timestamp Unix seconds timestamp (0 = omit).
/// @return AM_OK on success.
int AMcommit(AMdoc* doc, const char* message, int64_t timestamp);

// ─── Conflict detection ──────────────────────────────────────────────────────

/// Get all concurrent values for a key (conflict detection).
/// Returns a JSON array; each element has the same format as AMget.
/// @param doc      The document.
/// @param obj_id   Map object ID.
/// @param key      Map key.
/// @param out_json Receives NUL-terminated JSON array; free with AMfree_bytes(ptr, len+1).
/// @param out_len  Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMget_all(const AMdoc* doc, const char* obj_id, const char* key,
              uint8_t** out_json, size_t* out_len);

// ─── Diff / patches ──────────────────────────────────────────────────────────

/// Get patches for all changes since the last call to this function.
/// Returns a JSON array of patch objects.
/// @param doc      The document.
/// @param out_json Receives NUL-terminated JSON array; free with AMfree_bytes(ptr, len+1).
/// @param out_len  Receives byte count excluding NUL.
/// @return AM_OK on success.
int AMdiff_incremental(AMdoc* doc, uint8_t** out_json, size_t* out_len);

// ─── New APIs: closing gap with JS @automerge/automerge ──────────────────────

/// Get ALL changes in the document. Equivalent to JS getAllChanges().
int AMget_all_changes(AMdoc* doc, uint8_t** out_changes, size_t* out_len);

/// Get the binary representation of the last locally-made change.
/// Returns 0-length if no local change exists.
int AMget_last_local_change(AMdoc* doc, uint8_t** out_bytes, size_t* out_len);

/// Get change hashes needed to reach `heads` that are missing from the document.
int AMget_missing_deps(AMdoc* doc, const uint8_t* heads, size_t heads_len,
                       uint8_t** out_heads, size_t* out_len);

/// Create an empty change (no operations).
int AMempty_change(AMdoc* doc, const char* message, int64_t timestamp);

/// Save only changes since `heads`. Equivalent to JS saveSince(heads).
int AMsave_after(AMdoc* doc, const uint8_t* heads, size_t heads_len,
                 uint8_t** out_bytes, size_t* out_len);

/// Load incremental changes into an existing document.
int AMload_incremental(AMdoc* doc, const uint8_t* data, size_t len);

/// Fork at specific heads (immutable snapshot). Equivalent to JS view(heads)/forkAt.
int AMfork_at(AMdoc* doc, const uint8_t* heads, size_t heads_len, AMdoc** out_doc);

/// Get the object type ("map", "list", "text") as JSON string.
int AMobject_type(AMdoc* doc, const char* obj_id, uint8_t** out_json, size_t* out_len);

/// Diff between two sets of heads. Returns JSON patch array.
int AMdiff(AMdoc* doc,
           const uint8_t* before_heads, size_t before_heads_len,
           const uint8_t* after_heads, size_t after_heads_len,
           uint8_t** out_json, size_t* out_len);

/// Update a text object by diffing old vs new text. Equivalent to JS updateText.
int AMupdate_text(AMdoc* doc, const char* obj_id, const char* new_text);

/// Add a rich text mark to a range.
/// expand: 0=none, 1=before, 2=after, 3=both.
int AMmark(AMdoc* doc, const char* obj_id,
           size_t start, size_t end,
           const char* name, const char* value_json, uint8_t expand);

/// Remove a mark from a range.
/// expand: 0=none, 1=before, 2=after, 3=both.
int AMunmark(AMdoc* doc, const char* obj_id,
             const char* name, size_t start, size_t end, uint8_t expand);

/// Get all marks on a text object. Returns JSON array.
int AMmarks(AMdoc* doc, const char* obj_id, uint8_t** out_json, size_t* out_len);

/// Get marks at specific heads. Returns JSON array.
int AMmarks_at(AMdoc* doc, const char* obj_id,
               const uint8_t* heads, size_t heads_len,
               uint8_t** out_json, size_t* out_len);

/// Get a cursor for a position in a text object.
/// heads may be NULL for current version.
int AMget_cursor(AMdoc* doc, const char* obj_id, size_t position,
                 const uint8_t* heads, size_t heads_len,
                 uint8_t** out_cursor, size_t* out_len);

/// Resolve a cursor to a position.
/// heads may be NULL for current version.
int AMget_cursor_position(AMdoc* doc, const char* obj_id, const char* cursor_str,
                          const uint8_t* heads, size_t heads_len,
                          size_t* out_position);

/// Get rich text spans. Returns JSON array.
int AMspans(AMdoc* doc, const char* obj_id, uint8_t** out_json, size_t* out_len);

/// Get document statistics. Returns JSON string.
int AMstats(AMdoc* doc, uint8_t** out_json, size_t* out_len);

/// Get map entries in a key range. start/end may be NULL for unbounded.
int AMmap_range(AMdoc* doc, const char* obj_id,
                const char* start, const char* end,
                uint8_t** out_json, size_t* out_len);

/// Get list entries in an index range.
int AMlist_range(AMdoc* doc, const char* obj_id,
                 size_t start, size_t end,
                 uint8_t** out_json, size_t* out_len);

// ─── Sync ─────────────────────────────────────────────────────────────────────

/// Create a new sync state.  Ownership is transferred to caller.
AMsync_state* AMcreate_sync_state(void);

/// Free a sync state.
void AMfree_sync_state(AMsync_state* state);

/// Generate the next sync message to send to a remote peer.
/// @param doc     The local document.
/// @param state   The per-peer sync state.
/// @param out_msg Receives heap-allocated message bytes, or NULL if nothing to send.
/// @param out_len Receives message byte count (0 if nothing to send).
/// @return AM_OK on success.
int AMgenerate_sync_message(AMdoc* doc, AMsync_state* state,
                            uint8_t** out_msg, size_t* out_len);

/// Process an incoming sync message from a remote peer.
/// @param doc   The local document.
/// @param state The per-peer sync state.
/// @param msg   Incoming message bytes.
/// @param len   Byte count.
/// @return AM_OK on success.
int AMreceive_sync_message(AMdoc* doc, AMsync_state* state,
                           const uint8_t* msg, size_t len);

/// Persist a sync state to bytes.
/// @param state     The sync state.
/// @param out_bytes Receives heap-allocated bytes; free with AMfree_bytes.
/// @param out_len   Receives byte count.
/// @return AM_OK on success.
int AMsave_sync_state(const AMsync_state* state,
                      uint8_t** out_bytes, size_t* out_len);

/// Restore a sync state from persisted bytes.
/// @param data      Byte buffer.
/// @param len       Byte count.
/// @param out_state Receives heap-allocated sync state; free with AMfree_sync_state.
/// @return AM_OK on success.
int AMload_sync_state(const uint8_t* data, size_t len,
                      AMsync_state** out_state);

// ─── Error handling ──────────────────────────────────────────────────────────

/// Copy the last error message into buf (NUL-terminated).
/// @param buf     Output buffer.  May be NULL to query required size.
/// @param buf_len Buffer capacity in bytes.
/// @return Number of bytes written including NUL, or required size if buf==NULL.
size_t AMget_last_error(char* buf, size_t buf_len);

// ─── Memory management ───────────────────────────────────────────────────────

/// Free a byte buffer returned by this library.
/// @param data Pointer originally written to an out_* parameter.
/// @param len  Byte count originally written to the matching out_len parameter.
void AMfree_bytes(uint8_t* data, size_t len);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // AUTOMERGE_CORE_H
