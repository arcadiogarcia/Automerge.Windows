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
