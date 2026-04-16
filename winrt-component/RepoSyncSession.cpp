// RepoSyncSession.cpp — WinRT RepoSyncSession runtime class implementation.
// Implements the automerge-repo v1 WebSocket sync protocol over
// Windows::Networking::Sockets::MessageWebSocket.
#include "pch.h"
#include "RepoSyncSession.h"
#include <combaseapi.h>   // CoCreateGuid
#include <cstdio>         // snprintf

namespace winrt::Automerge::Windows::implementation {

using namespace winrt::Windows::Networking::Sockets;
using namespace winrt::Windows::Storage::Streams;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::System::Threading;
using namespace AutomergeWinRT;

// 100-nanosecond tick constants for Windows::Foundation::TimeSpan
static constexpr TimeSpan k1s{  10'000'000LL };   // 1 second
static constexpr TimeSpan k2s{  20'000'000LL };   // 2 seconds
static constexpr TimeSpan k10s{ 100'000'000LL };  // 10 seconds (connect timeout)

// ─── Helpers ─────────────────────────────────────────────────────────────────

static std::string new_guid_string()
{
    GUID g{};
    ::CoCreateGuid(&g);
    char buf[37]{};
    snprintf(buf, sizeof(buf),
        "%08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
        static_cast<unsigned long>(g.Data1),
        static_cast<unsigned>(g.Data2),
        static_cast<unsigned>(g.Data3),
        g.Data4[0], g.Data4[1], g.Data4[2], g.Data4[3],
        g.Data4[4], g.Data4[5], g.Data4[6], g.Data4[7]);
    return buf;
}

// ─── Constructors ─────────────────────────────────────────────────────────────

RepoSyncSession::RepoSyncSession(hstring const& serverUrl, hstring const& documentId)
    : RepoSyncSession(serverUrl, documentId, hstring{})
{}

RepoSyncSession::RepoSyncSession(
    hstring const& serverUrl,
    hstring const& documentId,
    hstring const& peerId)
    : server_url_(hstring_to_string(serverUrl))
    , doc_id_(hstring_to_string(documentId))
    , peer_id_(peerId.empty() ? new_guid_string() : hstring_to_string(peerId))
{}

// ─── EnqueueFrame (called from MessageReceived callback) ─────────────────────

void RepoSyncSession::EnqueueFrame(std::vector<uint8_t> frame)
{
    {
        std::lock_guard lock(recv_mutex_);
        recv_queue_.push_back(std::move(frame));
    }
    ::SetEvent(recv_event_.get());
}

// ─── ConnectAndHandshakeAsync ─────────────────────────────────────────────────

IAsyncAction RepoSyncSession::ConnectAndHandshakeAsync()
{
    auto strong_this = get_strong();
    co_await resume_background();

    ws_ = MessageWebSocket{};
    ws_.Control().MessageType(SocketMessageType::Binary);
    ws_closed_ = false;
    {
        std::lock_guard lock(recv_mutex_);
        recv_queue_.clear();
    }

    // Register event handlers (weak ref to avoid cycles).
    msg_token_ = ws_.MessageReceived(
        [weak = get_weak()](auto&&, MessageWebSocketMessageReceivedEventArgs const& args) {
            auto self = weak.get();
            if (!self) return;
            auto reader = args.GetDataReader();
            uint32_t len = reader.UnconsumedBufferLength();
            std::vector<uint8_t> frame(len);
            reader.ReadBytes({ frame.data(), static_cast<uint32_t>(frame.size()) });
            self->EnqueueFrame(std::move(frame));
        });

    closed_token_ = ws_.Closed(
        [weak = get_weak()](auto&&, auto&&) {
            auto self = weak.get();
            if (!self) return;
            self->ws_closed_ = true;
            ::SetEvent(self->recv_event_.get());
        });

    // Connect
    auto uri = Uri{ to_hstring(server_url_) };
    co_await ws_.ConnectAsync(uri);

    // ── Send join message ────────────────────────────────────────────────────
    {
        auto join_bytes = Cbor::encode_join(peer_id_);
        auto writer = DataWriter{ ws_.OutputStream() };
        writer.WriteBytes({ join_bytes.data(), static_cast<uint32_t>(join_bytes.size()) });
        co_await writer.StoreAsync();
        writer.DetachStream();
    }

    // ── Wait for peer message ────────────────────────────────────────────────
    uint64_t deadline = ::GetTickCount64() + 10000; // 10-second timeout
    while (true) {
        co_await resume_on_signal(recv_event_.get());

        if (ws_closed_) {
            throw hresult_error(E_FAIL, L"Server closed connection before sending 'peer' message.");
        }
        if (::GetTickCount64() >= deadline) {
            throw hresult_error(E_FAIL, L"Timeout waiting for 'peer' message from server.");
        }

        std::vector<uint8_t> frame;
        {
            std::lock_guard lock(recv_mutex_);
            if (recv_queue_.empty()) continue;
            frame = std::move(recv_queue_.front());
            recv_queue_.pop_front();
        }

        auto msg  = Cbor::parse(frame);
        auto type = Cbor::get_string(msg, "type");

        if (type == "peer") {
            remote_peer_id_ = Cbor::get_string(msg, "senderId");
            break;
        }
        if (type == "error") {
            auto err = Cbor::get_string(msg, "message");
            throw hresult_error(E_FAIL, to_hstring("Sync server error during handshake: " + err));
        }
        // ignore other messages
    }
}

// ─── PushAsync ────────────────────────────────────────────────────────────────

IAsyncAction RepoSyncSession::PushAsync(
    implementation::Document&  doc_impl,
    implementation::SyncState& sync_impl)
{
    auto strong_this = get_strong();
    if (ws_ == nullptr || ws_closed_) co_return;

    std::vector<uint8_t> msg_bytes;
    try {
        msg_bytes = sync_impl.native_state().generate_sync_message(doc_impl.native_doc());
    } catch (...) { co_return; }

    if (msg_bytes.empty()) co_return;

    auto frame = Cbor::encode_sync(peer_id_, remote_peer_id_, doc_id_, msg_bytes);
    auto writer = DataWriter{ ws_.OutputStream() };
    writer.WriteBytes({ frame.data(), static_cast<uint32_t>(frame.size()) });
    co_await writer.StoreAsync();
    writer.DetachStream();
}

// ─── CloseAsync ───────────────────────────────────────────────────────────────

IAsyncAction RepoSyncSession::CloseAsync()
{
    auto strong_this = get_strong();
    co_await resume_background();
    if (ws_ == nullptr) co_return;
    try {
        ws_.MessageReceived(std::exchange(msg_token_, {}));
        ws_.Closed(std::exchange(closed_token_, {}));
        ws_.Close(1000, L"");
    } catch (...) {}
    ws_closed_ = true;
    ws_ = nullptr;
    ::SetEvent(recv_event_.get()); // unblock any waiting coroutine
}

// ─── RunAsync ─────────────────────────────────────────────────────────────────

IAsyncAction RepoSyncSession::RunAsync(
    winrt::Automerge::Windows::Document const& doc,
    winrt::Automerge::Windows::SyncState       const& syncState)
{
    auto strong_this = get_strong();
    auto cancel = co_await get_cancellation_token();
    cancel.enable_propagation(false); // check cancel() manually each tick

    auto& doc_impl  = *get_self<implementation::Document>(doc);
    auto& sync_impl = *get_self<implementation::SyncState>(syncState);

    co_await ConnectAndHandshakeAsync();
    co_await PushAsync(doc_impl, sync_impl);

    // A 1-second periodic timer wakes the loop to push local changes even when
    // the server sends nothing.
    auto timer = ThreadPoolTimer::CreatePeriodicTimer(
        [weak = get_weak()](ThreadPoolTimer const&) {
            if (auto self = weak.get())
                ::SetEvent(self->recv_event_.get());
        }, k1s);

    while (!cancel() && !ws_closed_)
    {
        co_await resume_on_signal(recv_event_.get());
        if (cancel()) break;

        // Drain all queued frames
        std::vector<std::vector<uint8_t>> frames;
        {
            std::lock_guard lock(recv_mutex_);
            while (!recv_queue_.empty()) {
                frames.push_back(std::move(recv_queue_.front()));
                recv_queue_.pop_front();
            }
        }

        bool got_sync = false;
        for (auto& frame : frames) {
            auto m    = Cbor::parse(frame);
            auto type = Cbor::get_string(m, "type");

            if (type == "sync") {
                auto data = Cbor::get_bytes(m, "data");
                if (!data.empty()) {
                    try {
                        sync_impl.native_state().receive_sync_message(
                            doc_impl.native_doc(), data);
                    } catch (...) {}
                    got_sync = true;
                }
            } else if (type == "error") {
                timer.Cancel();
                auto err = Cbor::get_string(m, "message");
                co_await CloseAsync();
                throw hresult_error(E_FAIL, to_hstring("Sync server error: " + err));
            }
        }

        // Push back any local changes (or the initial outgoing sync messages).
        co_await PushAsync(doc_impl, sync_impl);
        (void)got_sync;
    }

    timer.Cancel();
    co_await CloseAsync();
}

// ─── SyncOnceAsync ────────────────────────────────────────────────────────────

/*static*/
IAsyncAction RepoSyncSession::SyncOnceAsync(
    hstring const& serverUrl,
    hstring const& documentId,
    winrt::Automerge::Windows::Document    const& doc,
    winrt::Automerge::Windows::SyncState   const& syncState)
{
    auto impl = make_self<implementation::RepoSyncSession>(serverUrl, documentId);

    auto& doc_impl  = *get_self<implementation::Document>(doc);
    auto& sync_impl = *get_self<implementation::SyncState>(syncState);

    co_await impl->ConnectAndHandshakeAsync();
    co_await impl->PushAsync(doc_impl, sync_impl);

    // A one-shot 2-second idle timer detects convergence.  When it fires it
    // sets idle_done and signals recv_event_ so the loop below wakes up.
    auto idle_done = std::make_shared<std::atomic<bool>>(false);

    auto make_idle_timer = [&]() {
        auto flag     = idle_done;
        auto raw_ev   = impl->recv_event_.get();
        auto impl_ref = impl; // keep impl alive inside the lambda
        return ThreadPoolTimer::CreateTimer(
            [impl_ref, flag, raw_ev](ThreadPoolTimer const&) {
                *flag = true;
                ::SetEvent(raw_ev);
            }, k2s);
    };

    auto idle_timer = make_idle_timer();

    while (!impl->ws_closed_) {
        co_await resume_on_signal(impl->recv_event_.get());

        if (*idle_done) break; // 2-second idle — converged

        // Drain all queued frames
        std::vector<std::vector<uint8_t>> frames;
        {
            std::lock_guard lock(impl->recv_mutex_);
            while (!impl->recv_queue_.empty()) {
                frames.push_back(std::move(impl->recv_queue_.front()));
                impl->recv_queue_.pop_front();
            }
        }

        bool got_sync = false;
        for (auto& frame : frames) {
            auto m    = Cbor::parse(frame);
            auto type = Cbor::get_string(m, "type");

            if (type == "sync") {
                auto data = Cbor::get_bytes(m, "data");
                if (!data.empty()) {
                    try {
                        sync_impl.native_state().receive_sync_message(
                            doc_impl.native_doc(), data);
                    } catch (...) {}
                    got_sync = true;
                }
            } else if (type == "error") {
                idle_timer.Cancel();
                auto err = Cbor::get_string(m, "message");
                co_await impl->CloseAsync();
                throw hresult_error(E_FAIL, to_hstring("Sync server error: " + err));
            }
        }

        if (got_sync) {
            // Activity detected — push and restart the idle timer.
            co_await impl->PushAsync(doc_impl, sync_impl);
            idle_timer.Cancel();
            idle_done = std::make_shared<std::atomic<bool>>(false);
            idle_timer = make_idle_timer();
        }
    }

    idle_timer.Cancel();
    co_await impl->CloseAsync();
}

} // namespace winrt::Automerge::Windows::implementation
