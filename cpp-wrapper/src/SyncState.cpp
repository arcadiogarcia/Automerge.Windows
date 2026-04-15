#include "../include/automerge/SyncState.hpp"

#include <utility>

namespace automerge {

// ─── Helper ───────────────────────────────────────────────────────────────────

static std::vector<uint8_t> take_cbuf_ss(uint8_t* ptr, size_t len) {
    std::vector<uint8_t> result;
    if (ptr && len > 0) {
        result.assign(ptr, ptr + len);
        AMfree_bytes(ptr, len);
    }
    return result;
}

void SyncState::check(int rc) {
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
}

// ─── Lifecycle ────────────────────────────────────────────────────────────────

SyncState::SyncState() : handle_(AMcreate_sync_state()) {
    if (!handle_) {
        throw AutomergeError("AMcreate_sync_state returned null");
    }
}

SyncState::SyncState(AMsync_state* handle) noexcept : handle_(handle) {}

SyncState::~SyncState() {
    if (handle_) {
        AMfree_sync_state(handle_);
        handle_ = nullptr;
    }
}

SyncState::SyncState(SyncState&& other) noexcept : handle_(other.handle_) {
    other.handle_ = nullptr;
}

SyncState& SyncState::operator=(SyncState&& other) noexcept {
    if (this != &other) {
        if (handle_) AMfree_sync_state(handle_);
        handle_ = other.handle_;
        other.handle_ = nullptr;
    }
    return *this;
}

// ─── Protocol ────────────────────────────────────────────────────────────────

std::vector<uint8_t> SyncState::generate_sync_message(Document& doc) {
    uint8_t* ptr{};
    size_t   len{};
    check(AMgenerate_sync_message(doc.native_handle(), handle_, &ptr, &len));
    return take_cbuf_ss(ptr, len);
}

void SyncState::receive_sync_message(Document& doc,
                                     std::span<const uint8_t> message) {
    check(AMreceive_sync_message(doc.native_handle(), handle_,
                                 message.data(), message.size()));
}

// ─── Persistence ─────────────────────────────────────────────────────────────

std::vector<uint8_t> SyncState::save() const {
    uint8_t* ptr{};
    size_t   len{};
    check(AMsave_sync_state(handle_, &ptr, &len));
    return take_cbuf_ss(ptr, len);
}

SyncState SyncState::load(std::span<const uint8_t> data) {
    AMsync_state* out{};
    int rc = AMload_sync_state(data.data(), data.size(), &out);
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
    return SyncState(out);
}

} // namespace automerge
