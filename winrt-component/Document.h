// Document.h — WinRT runtime class implementation header.
#pragma once

#include "pch.h"
#include "Helpers.hpp"
#include "Document.g.h"

namespace winrt::Automerge::Windows::implementation {

/// WinRT implementation of the Document runtime class.
struct Document : DocumentT<Document>
{
    Document();

    // Persistence
    winrt::Windows::Storage::Streams::IBuffer Save();
    static winrt::Automerge::Windows::Document Load(
        winrt::Windows::Storage::Streams::IBuffer const& data);

    // Heads
    winrt::Windows::Storage::Streams::IBuffer GetHeads();

    // Changes
    winrt::Windows::Storage::Streams::IBuffer GetChanges(
        winrt::Windows::Storage::Streams::IBuffer const& heads);
    void ApplyChanges(winrt::Windows::Storage::Streams::IBuffer const& changes);

    // Merge
    void Merge(winrt::Automerge::Windows::Document const& other);

    // Read
    hstring GetValue(hstring const& pathJson);

    // Write
    void PutJsonRoot(hstring const& jsonObj);

    // Internal access for SyncState
    ::automerge::Document& native_doc() noexcept { return doc_; }

private:
    ::automerge::Document doc_;
};

} // namespace winrt::Automerge::Windows::implementation

namespace winrt::Automerge::Windows::factory_implementation {
struct Document : DocumentT<Document, implementation::Document> {};
} // namespace winrt::Automerge::Windows::factory_implementation
