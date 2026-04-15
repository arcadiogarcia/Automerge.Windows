// SyncState.h — WinRT SyncState runtime class header.
#pragma once

#include "pch.h"
#include "Helpers.hpp"
#include "winrt/Automerge.Windows.h"
#include "Automerge.Windows.g.h"

namespace winrt::Automerge::Windows::implementation {

struct SyncState : SyncStateT<SyncState>
{
    SyncState();

    // Protocol
    Windows::Storage::Streams::IBuffer GenerateSyncMessage(
        Automerge::Windows::Document const& doc);

    void ReceiveSyncMessage(
        Automerge::Windows::Document const& doc,
        Windows::Storage::Streams::IBuffer const& message);

    // Persistence
    Windows::Storage::Streams::IBuffer Save();
    static Automerge::Windows::SyncState Load(
        Windows::Storage::Streams::IBuffer const& data);

private:
    ::automerge::SyncState state_;
};

} // namespace winrt::Automerge::Windows::implementation

namespace winrt::Automerge::Windows::factory_implementation {
struct SyncState : SyncStateT<SyncState, implementation::SyncState> {};
} // namespace winrt::Automerge::Windows::factory_implementation
