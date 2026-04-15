#pragma once
#ifndef AUTOMERGE_DOCUMENT_HPP
#define AUTOMERGE_DOCUMENT_HPP

#include <cstdint>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

#include "Error.hpp"
#include "../../rust-core/include/automerge_core.h"

namespace automerge {

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

    // Non-copyable (semantics are unclear and expensive).
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

    // ─── Heads ─────────────────────────────────────────────────────────────

    /// Get the current heads as packed 32-byte SHA-256 hashes.
    [[nodiscard]] std::vector<uint8_t> get_heads() const;

    // ─── Changes ───────────────────────────────────────────────────────────

    /// Get all changes produced since the given heads.
    ///
    /// @param heads  Packed 32-byte head hashes.  Empty → all changes.
    [[nodiscard]] std::vector<uint8_t>
    get_changes(std::span<const uint8_t> heads = {}) const;

    /// Apply a binary changes buffer (produced by get_changes) to this document.
    void apply_changes(std::span<const uint8_t> changes);

    // ─── Merge ─────────────────────────────────────────────────────────────

    /// Merge all changes from other into this document.
    void merge(Document& other);

    // ─── Read ──────────────────────────────────────────────────────────────

    /// Read a value by JSON-array path.
    ///
    /// @param path_json  JSON array string, e.g. "[\"contacts\",0,\"name\"]".
    ///                   Pass "" or "[]" to serialise the entire root object.
    /// @return           The value as a JSON string.
    [[nodiscard]] std::string get_value(const std::string& path_json = "[]") const;

    // ─── Write ─────────────────────────────────────────────────────────────

    /// Set scalar key-value pairs in the document root from a JSON object.
    ///
    /// @param json_obj  JSON object string, e.g. "{\"name\":\"Alice\",\"age\":30}".
    void put_json_root(const std::string& json_obj);

    // ─── Internal access ───────────────────────────────────────────────────

    /// Return the underlying raw handle for use with the C API.
    [[nodiscard]] AMdoc* native_handle() const noexcept { return handle_; }

private:
    /// Private constructor used by load().
    explicit Document(AMdoc* handle) noexcept;

    /// Check a C-API return code and throw AutomergeError on failure.
    static void check(int rc);

    AMdoc* handle_{nullptr};
};

} // namespace automerge

#endif // AUTOMERGE_DOCUMENT_HPP
