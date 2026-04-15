#include "../include/automerge/Document.hpp"

#include <cassert>
#include <utility>

namespace automerge {

// ─── Helper: wrap C-malloc buffer into a vector ───────────────────────────────

static std::vector<uint8_t> take_cbuf(uint8_t* ptr, size_t len) {
    // Copy the library-owned buffer into a vector, then free it.
    std::vector<uint8_t> result;
    if (ptr && len > 0) {
        result.assign(ptr, ptr + len);
        AMfree_bytes(ptr, len);
    } else if (ptr) {
        // len == 0 but ptr is non-null (e.g. NUL-terminated JSON)
        // nothing to copy
        AMfree_bytes(ptr, 0);
    }
    return result;
}

// ─── check ────────────────────────────────────────────────────────────────────

void Document::check(int rc) {
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
}

// ─── Lifecycle ────────────────────────────────────────────────────────────────

Document::Document() : handle_(AMcreate_doc()) {
    if (!handle_) {
        throw AutomergeError("AMcreate_doc returned null");
    }
}

Document::Document(AMdoc* handle) noexcept : handle_(handle) {}

Document::~Document() {
    if (handle_) {
        AMdestroy_doc(handle_);
        handle_ = nullptr;
    }
}

Document::Document(Document&& other) noexcept : handle_(other.handle_) {
    other.handle_ = nullptr;
}

Document& Document::operator=(Document&& other) noexcept {
    if (this != &other) {
        if (handle_) AMdestroy_doc(handle_);
        handle_ = other.handle_;
        other.handle_ = nullptr;
    }
    return *this;
}

// ─── Persistence ─────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::save() const {
    uint8_t* ptr{};
    size_t   len{};
    check(AMsave(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

Document Document::load(std::span<const uint8_t> data) {
    AMdoc* out{};
    int rc = AMload(data.data(), data.size(), &out);
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
    return Document(out);
}

// ─── Heads ───────────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::get_heads() const {
    uint8_t* ptr{};
    size_t   len{};
    check(AMget_heads(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

// ─── Changes ─────────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::get_changes(std::span<const uint8_t> heads) const {
    uint8_t* ptr{};
    size_t   len{};
    check(AMget_changes(handle_,
                        heads.data(), heads.size(),
                        &ptr, &len));
    return take_cbuf(ptr, len);
}

void Document::apply_changes(std::span<const uint8_t> changes) {
    check(AMapply_changes(handle_, changes.data(), changes.size()));
}

// ─── Merge ───────────────────────────────────────────────────────────────────

void Document::merge(Document& other) {
    check(AMmerge(handle_, other.handle_));
}

// ─── Read ────────────────────────────────────────────────────────────────────

std::string Document::get_value(const std::string& path_json) const {
    uint8_t* ptr{};
    size_t   len{};
    const char* path = path_json.empty() ? nullptr : path_json.c_str();
    check(AMget_value(handle_, path, &ptr, &len));
    // ptr is NUL-terminated; len excludes NUL
    std::string result(reinterpret_cast<const char*>(ptr), len);
    AMfree_bytes(ptr, len + 1);
    return result;
}

// ─── Write ───────────────────────────────────────────────────────────────────

void Document::put_json_root(const std::string& json_obj) {
    check(AMput_json_root(handle_, json_obj.c_str()));
}

} // namespace automerge
