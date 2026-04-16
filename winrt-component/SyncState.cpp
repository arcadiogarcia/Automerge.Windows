// SyncState.cpp — WinRT SyncState runtime class implementation.
#include "pch.h"
#include "SyncState.h"
#include "Document.h"

namespace winrt::Automerge::Windows::implementation {

using namespace AutomergeWinRT;

SyncState::SyncState() : state_() {}

// ─── Protocol ────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer SyncState::GenerateSyncMessage(
    winrt::Automerge::Windows::Document const& doc)
{
    try {
        auto& doc_impl = *winrt::get_self<implementation::Document>(doc);
        auto msg = state_.generate_sync_message(doc_impl.native_doc());
        return bytes_to_ibuffer(msg);
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

void SyncState::ReceiveSyncMessage(
    winrt::Automerge::Windows::Document const& doc,
    winrt::Windows::Storage::Streams::IBuffer const& message)
{
    try {
        auto& doc_impl = *winrt::get_self<implementation::Document>(doc);
        auto msg = ibuffer_to_bytes(message);
        state_.receive_sync_message(doc_impl.native_doc(), msg);
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

// ─── Persistence ─────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer SyncState::Save() {
    try {
        return bytes_to_ibuffer(state_.save());
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

winrt::Automerge::Windows::SyncState SyncState::Load(
    winrt::Windows::Storage::Streams::IBuffer const& data)
{
    try {
        auto bytes = ibuffer_to_bytes(data);
        auto impl = winrt::make<implementation::SyncState>();
        winrt::get_self<implementation::SyncState>(impl)->state_ =
            ::automerge::SyncState::load(bytes);
        return impl;
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

bool SyncState::HasOurChanges(winrt::Automerge::Windows::Document const& doc) {
    try {
        auto& doc_impl = *winrt::get_self<implementation::Document>(doc);
        return state_.has_our_changes(doc_impl.native_doc());
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

} // namespace winrt::Automerge::Windows::implementation
