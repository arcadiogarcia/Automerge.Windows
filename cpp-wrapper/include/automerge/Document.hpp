#pragma once
#ifndef AUTOMERGE_DOCUMENT_HPP
#define AUTOMERGE_DOCUMENT_HPP

#include <cstdint>
#include <optional>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

#include "Error.hpp"
#include "../../rust-core/include/automerge_core.h"

namespace automerge {

/// String handle for an object ID ("_root" for the document root, or
/// "counter@hexactor" for nested objects returned by put_object / insert_object).
using ObjId = std::string;

/// Sentinel value for the document root object.
inline const ObjId ROOT_OBJ{};

/// Sentinel index meaning "append to end" for insert / insert_object.
constexpr size_t APPEND = SIZE_MAX;

/// RAII wrapper around an Automerge document (AMdoc*).
///
/// Provides a modern C++20 interface with span<> and vector<> instead of raw
/// pointer/length pairs.  All errors are thrown as AutomergeError exceptions.
///
/// Thread-safety: The Document itself is NOT thread-safe.  Callers must
/// ensure mutual exclusion if a Document is shared across threads.
class Document {
public:
    // ─── Lifecycle ─────────────────────────────────────────────────────────

    /// Create a new empty document.
    Document();

    /// Destructor — frees the underlying native handle.
    ~Document();

    // Non-copyable (use fork() for explicit copies).
    Document(const Document&) = delete;
    Document& operator=(const Document&) = delete;

    /// Move constructor.
    Document(Document&& other) noexcept;

    /// Move assignment.
    Document& operator=(Document&& other) noexcept;

    // ─── Persistence ───────────────────────────────────────────────────────

    /// Serialize the document to bytes.
    [[nodiscard]] std::vector<uint8_t> save() const;

    /// Load a document from bytes produced by save().
    [[nodiscard]] static Document load(std::span<const uint8_t> data);

    /// Save only the changes since the last save() / save_incremental() call.
    std::vector<uint8_t> save_incremental();

    // ─── Fork ──────────────────────────────────────────────────────────────

    /// Fork (clone) the document, producing an independent copy.
    [[nodiscard]] Document fork();

    // ─── Heads ─────────────────────────────────────────────────────────────

    /// Get the current heads as packed 32-byte SHA-256 hashes.
    [[nodiscard]] std::vector<uint8_t> get_heads() const;

    // ─── Changes ───────────────────────────────────────────────────────────

    /// Get all changes produced since the given heads.
    [[nodiscard]] std::vector<uint8_t>
    get_changes(std::span<const uint8_t> heads = {}) const;

    /// Apply a binary changes buffer (produced by get_changes) to this document.
    void apply_changes(std::span<const uint8_t> changes);

    // ─── Merge ─────────────────────────────────────────────────────────────

    /// Merge all changes from other into this document.
    void merge(Document& other);

    // ─── Actor ─────────────────────────────────────────────────────────────

    /// Get the actor ID for this document as raw bytes.
    [[nodiscard]] std::vector<uint8_t> get_actor() const;

    /// Set the actor ID for this document.
    void set_actor(std::span<const uint8_t> actor_bytes);

    // ─── Read ──────────────────────────────────────────────────────────────

    /// Read a value by JSON-array path (legacy; serialises entire doc first).
    [[nodiscard]] std::string get_value(const std::string& path_json = "[]") const;

    /// Get a single value from a map object by key.
    /// Returns JSON: a scalar or {"_obj_id":"...","_obj_type":"map|list|text"}.
    [[nodiscard]] std::string get(const ObjId& obj_id, const std::string& key) const;

    /// Get a single value from a list/text object by index.
    [[nodiscard]] std::string get_idx(const ObjId& obj_id, size_t index) const;

    /// Get all map keys of an object as a JSON array string.
    [[nodiscard]] std::string keys(const ObjId& obj_id = ROOT_OBJ) const;

    /// Get the number of elements/keys in an object.
    [[nodiscard]] size_t length(const ObjId& obj_id = ROOT_OBJ) const;

    /// Get the text content of a text object.
    [[nodiscard]] std::string get_text(const ObjId& obj_id) const;

    /// Get all concurrent values for a key (conflict detection), as a JSON array.
    [[nodiscard]] std::string get_all(const ObjId& obj_id, const std::string& key) const;

    // ─── Write ─────────────────────────────────────────────────────────────

    /// Set scalar key-value pairs in the document root from a JSON object (legacy).
    void put_json_root(const std::string& json_obj);

    /// Set a scalar value in a map object by key.
    /// scalar_json: "\"hello\"", "42", "true", "null", etc.
    void put(const ObjId& obj_id, const std::string& key, const std::string& scalar_json);

    /// Set a scalar value in a list object at an index (overwrites existing).
    void put_idx(const ObjId& obj_id, size_t index, const std::string& scalar_json);

    /// Create a nested map/list/text object at a map key.
    /// @param obj_type "map", "list", or "text".
    /// @return The new object's ID for use in subsequent get/put calls.
    [[nodiscard]] ObjId put_object(const ObjId& obj_id, const std::string& key,
                                   const std::string& obj_type);

    /// Delete a key from a map object.
    void del(const ObjId& obj_id, const std::string& key);

    // ─── List operations ───────────────────────────────────────────────────

    /// Insert a scalar into a list at index. Use APPEND to add to the end.
    void insert(const ObjId& obj_id, size_t index, const std::string& scalar_json);

    /// Insert a nested object into a list at index. Use APPEND to add to the end.
    [[nodiscard]] ObjId insert_object(const ObjId& obj_id, size_t index,
                                      const std::string& obj_type);

    /// Delete the element at an index from a list.
    void delete_at(const ObjId& obj_id, size_t index);

    // ─── Counter ───────────────────────────────────────────────────────────

    /// Create a counter scalar at a map key with an initial value.
    void put_counter(const ObjId& obj_id, const std::string& key, int64_t initial = 0);

    /// Increment (or decrement) a counter at a map key.
    void increment(const ObjId& obj_id, const std::string& key, int64_t delta);

    // ─── Text ──────────────────────────────────────────────────────────────

    /// Insert/delete characters in a text object.
    /// Use delete_count = 0 for insert-only. Use text = "" for delete-only.
    void splice_text(const ObjId& obj_id, size_t start, std::ptrdiff_t delete_count,
                     const std::string& text = {});

    // ─── Commit ────────────────────────────────────────────────────────────

    /// Commit pending changes with an optional message/timestamp.
    /// message = "" omits the message. timestamp = 0 omits the timestamp.
    void commit(const std::string& message = {}, int64_t timestamp = 0);

    // ─── Diff / patches ────────────────────────────────────────────────────

    /// Get patches for all changes since the last call to this function.
    /// Returns a JSON array string.
    std::string diff_incremental();

    // ─── New APIs: closing gap with JS @automerge/automerge ────────────────

    /// Get ALL changes (equivalent to JS getAllChanges).
    [[nodiscard]] std::vector<uint8_t> get_all_changes() const;

    /// Get the binary representation of the last locally-made change.
    [[nodiscard]] std::vector<uint8_t> get_last_local_change() const;

    /// Get missing dependency hashes needed to reach `heads`.
    [[nodiscard]] std::vector<uint8_t>
    get_missing_deps(std::span<const uint8_t> heads) const;

    /// Create an empty change with optional message/timestamp.
    void empty_change(const std::string& message = {}, int64_t timestamp = 0);

    /// Save only changes since `heads`.
    [[nodiscard]] std::vector<uint8_t>
    save_after(std::span<const uint8_t> heads) const;

    /// Load incremental changes into this document.
    void load_incremental(std::span<const uint8_t> data);

    /// Fork at specific heads (snapshot at a point in history).
    [[nodiscard]] Document fork_at(std::span<const uint8_t> heads);

    /// Get the object type ("map", "list", "text") as a JSON string.
    [[nodiscard]] std::string object_type(const ObjId& obj_id) const;

    /// Diff between two sets of heads. Returns JSON patch array.
    [[nodiscard]] std::string diff(std::span<const uint8_t> before_heads,
                                   std::span<const uint8_t> after_heads) const;

    /// Update a text object by diffing old vs new text (equivalent to JS updateText).
    void update_text(const ObjId& obj_id, const std::string& new_text);

    /// Add a rich text mark. expand: 0=none, 1=before, 2=after, 3=both.
    void mark(const ObjId& obj_id, size_t start, size_t end,
              const std::string& name, const std::string& value_json, uint8_t expand = 3);

    /// Remove a mark. expand: 0=none, 1=before, 2=after, 3=both.
    void unmark(const ObjId& obj_id, const std::string& name,
                size_t start, size_t end, uint8_t expand = 3);

    /// Get all marks on a text object. Returns JSON array.
    [[nodiscard]] std::string marks(const ObjId& obj_id) const;

    /// Get marks at specific heads. Returns JSON array.
    [[nodiscard]] std::string marks_at(const ObjId& obj_id,
                                       std::span<const uint8_t> heads) const;

    /// Get a cursor for a position in a text object.
    /// heads may be empty for current version.
    [[nodiscard]] std::string get_cursor(const ObjId& obj_id, size_t position,
                                         std::span<const uint8_t> heads = {}) const;

    /// Resolve a cursor to a position.
    [[nodiscard]] size_t get_cursor_position(const ObjId& obj_id,
                                             const std::string& cursor,
                                             std::span<const uint8_t> heads = {}) const;

    /// Get rich text spans. Returns JSON array.
    [[nodiscard]] std::string spans(const ObjId& obj_id) const;

    /// Get document statistics. Returns JSON string.
    [[nodiscard]] std::string stats() const;

    /// Get map entries in a key range. empty string = unbounded.
    [[nodiscard]] std::string map_range(const ObjId& obj_id,
                                        const std::string& start = {},
                                        const std::string& end = {}) const;

    /// Get list entries in an index range.
    [[nodiscard]] std::string list_range(const ObjId& obj_id,
                                         size_t start, size_t end) const;

    // ─── Block marker APIs ─────────────────────────────────────────────────

    /// Insert a block marker at index in a text object. Returns block object ID.
    [[nodiscard]] ObjId split_block(const ObjId& obj_id, size_t index);

    /// Remove the block marker at index.
    void join_block(const ObjId& obj_id, size_t index);

    /// Replace the block marker at index. Returns new block object ID.
    [[nodiscard]] ObjId replace_block(const ObjId& obj_id, size_t index);

    // ─── Additional gap-closing APIs ───────────────────────────────────────

    /// Look up a specific change by its 32-byte hash.
    [[nodiscard]] std::vector<uint8_t>
    get_change_by_hash(std::span<const uint8_t> hash) const;

    /// Check whether the document contains all the given heads.
    [[nodiscard]] bool has_heads(std::span<const uint8_t> heads) const;

    // ─── Internal access ───────────────────────────────────────────────────

    /// Return the underlying raw handle.
    [[nodiscard]] AMdoc* native_handle() const noexcept { return handle_; }

private:
    explicit Document(AMdoc* handle) noexcept;
    static void check(int rc);

    // Helper: read NUL-terminated JSON from a C API call.
    static std::string take_json(uint8_t* ptr, size_t len);

    // Helper: read a heap-allocated NUL-terminated ID string.
    static std::string take_cstring(uint8_t* ptr, size_t len);

    AMdoc* handle_{nullptr};
};

} // namespace automerge

#endif // AUTOMERGE_DOCUMENT_HPP
