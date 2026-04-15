#pragma once
#ifndef AUTOMERGE_SYNCSTATE_HPP
#define AUTOMERGE_SYNCSTATE_HPP

#include <cstdint>
#include <span>
#include <vector>

#include "Document.hpp"
#include "Error.hpp"
#include "../../rust-core/include/automerge_core.h"

namespace automerge {

/// RAII wrapper around an Automerge sync state (AMsync_state*).
///
/// Every logical sync session with a remote peer needs its own SyncState.
/// Keep this alive for the duration of the peer relationship; it tracks what
/// the peer has already seen so only incremental differences are sent.
class SyncState {
public:
    // ─── Lifecycle ─────────────────────────────────────────────────────────

    /// Create a fresh sync state (beginning of a new peer relationship).
    SyncState();

    /// Destructor — frees the native handle.
    ~SyncState();

    SyncState(const SyncState&) = delete;
    SyncState& operator=(const SyncState&) = delete;

    SyncState(SyncState&& other) noexcept;
    SyncState& operator=(SyncState&& other) noexcept;

    // ─── Protocol ──────────────────────────────────────────────────────────

    /// Generate the next message to send to the remote peer.
    ///
    /// @param doc  The local document.
    /// @return     Encoded message bytes, or empty if the sync is complete.
    [[nodiscard]] std::vector<uint8_t>
    generate_sync_message(Document& doc);

    /// Process a message received from the remote peer.
    ///
    /// @param doc     The local document.
    /// @param message Encoded message bytes received from the peer.
    void receive_sync_message(Document& doc,
                              std::span<const uint8_t> message);

    // ─── Persistence ───────────────────────────────────────────────────────

    /// Serialize the sync state for optional persistence.
    [[nodiscard]] std::vector<uint8_t> save() const;

    /// Restore a sync state from persisted bytes.
    [[nodiscard]] static SyncState load(std::span<const uint8_t> data);

private:
    explicit SyncState(AMsync_state* handle) noexcept;
    static void check(int rc);

    AMsync_state* handle_{nullptr};
};

} // namespace automerge

#endif // AUTOMERGE_SYNCSTATE_HPP
