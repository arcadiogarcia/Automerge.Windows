// RepoSyncSession.h — WinRT RepoSyncSession runtime class header.
#pragma once

#include "pch.h"
#include "Cbor.hpp"
#include "Helpers.hpp"
#include "Document.h"
#include "SyncState.h"
#include "RepoSyncSession.g.h"

namespace winrt::Automerge::Windows::implementation {

struct RepoSyncSession : RepoSyncSessionT<RepoSyncSession>
{
    // ─── Constructors ────────────────────────────────────────────────────────

    RepoSyncSession(hstring const& serverUrl, hstring const& documentId);
    RepoSyncSession(hstring const& serverUrl, hstring const& documentId,
                    hstring const& peerId);

    // ─── Public API ──────────────────────────────────────────────────────────

    /// Continuous sync loop — runs until the IAsyncAction is cancelled.
    winrt::Windows::Foundation::IAsyncAction RunAsync(
        winrt::Automerge::Windows::Document const& doc,
        winrt::Automerge::Windows::SyncState const& syncState);

    /// One-shot sync — connects, syncs to 2-second convergence, then closes.
    static winrt::Windows::Foundation::IAsyncAction SyncOnceAsync(
        hstring const& serverUrl,
        hstring const& documentId,
        winrt::Automerge::Windows::Document const& doc,
        winrt::Automerge::Windows::SyncState const& syncState);

    /// Gracefully close the WebSocket connection.
    winrt::Windows::Foundation::IAsyncAction CloseAsync();

    // ─── Event ───────────────────────────────────────────────────────────

    /// Raised each time RunAsync/SyncOnceAsync applies an incoming remote change.
    winrt::event_token RemoteChange(
        winrt::Windows::Foundation::TypedEventHandler<
            winrt::Automerge::Windows::RepoSyncSession,
            winrt::Windows::Foundation::IInspectable> const& handler);
    void RemoteChange(winrt::event_token const& token) noexcept;

private:
    // ─── State ───────────────────────────────────────────────────────────────

    std::string server_url_;
    std::string doc_id_;
    std::string peer_id_;
    std::string remote_peer_id_;

    winrt::Windows::Networking::Sockets::MessageWebSocket ws_{ nullptr };
    winrt::event_token msg_token_{};
    winrt::event_token closed_token_{};
    bool ws_closed_{ false };

    // Receive queue (protected by recv_mutex_).
    // recv_event_ is auto-reset; signalled whenever a frame is enqueued or
    // ws_closed_ is set.
    std::mutex recv_mutex_;
    std::deque<std::vector<uint8_t>> recv_queue_;
    winrt::handle recv_event_{
        ::CreateEvent(nullptr, /*manual*/ FALSE, /*initial*/ FALSE, nullptr)
    };

    // ─── Internal helpers ────────────────────────────────────────────────────

    winrt::Windows::Foundation::IAsyncAction ConnectAndHandshakeAsync();

    winrt::Windows::Foundation::IAsyncAction PushAsync(
        implementation::Document&   doc_impl,
        implementation::SyncState&  sync_impl);

    void EnqueueFrame(std::vector<uint8_t> frame);

    // Event source for RemoteChange.
    winrt::event<winrt::Windows::Foundation::TypedEventHandler<
        winrt::Automerge::Windows::RepoSyncSession,
        winrt::Windows::Foundation::IInspectable>> remote_change_event_;
};

} // namespace winrt::Automerge::Windows::implementation

namespace winrt::Automerge::Windows::factory_implementation {

struct RepoSyncSession :
    RepoSyncSessionT<RepoSyncSession, implementation::RepoSyncSession> {};

} // namespace winrt::Automerge::Windows::factory_implementation
