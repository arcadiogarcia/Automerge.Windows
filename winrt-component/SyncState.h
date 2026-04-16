// SyncState.h — WinRT SyncState runtime class header.
#pragma once

#include "pch.h"
#include "Helpers.hpp"
#include "SyncState.g.h"

namespace winrt::Automerge::Windows::implementation {

struct SyncState : SyncStateT<SyncState>
{
    SyncState();

    // Protocol
    winrt::Windows::Storage::Streams::IBuffer GenerateSyncMessage(
        winrt::Automerge::Windows::Document const& doc);

    void ReceiveSyncMessage(
        winrt::Automerge::Windows::Document const& doc,
        winrt::Windows::Storage::Streams::IBuffer const& message);

    // Persistence
    winrt::Windows::Storage::Streams::IBuffer Save();
    static winrt::Automerge::Windows::SyncState Load(
        winrt::Windows::Storage::Streams::IBuffer const& data);

    // Internal
    ::automerge::SyncState& native_state() noexcept { return state_; }

private:
    ::automerge::SyncState state_;
};

} // namespace winrt::Automerge::Windows::implementation

namespace winrt::Automerge::Windows::factory_implementation {
struct SyncState : SyncStateT<SyncState, implementation::SyncState> {};
} // namespace winrt::Automerge::Windows::factory_implementation
