// Document.cpp — WinRT Document runtime class implementation.
#include "pch.h"
#include "Document.h"
#include "SyncState.h"

namespace winrt::Automerge::Windows::implementation {

using namespace AutomergeWinRT;

Document::Document() : doc_() {}

// ─── Persistence ─────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::Save() {
    try {
        return bytes_to_ibuffer(doc_.save());
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

winrt::Automerge::Windows::Document Document::Load(
    winrt::Windows::Storage::Streams::IBuffer const& data)
{
    try {
        auto bytes = ibuffer_to_bytes(data);
        auto impl = winrt::make<implementation::Document>();
        winrt::get_self<implementation::Document>(impl)->doc_ =
            ::automerge::Document::load(bytes);
        return impl;
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

// ─── Heads ───────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::GetHeads() {
    try {
        return bytes_to_ibuffer(doc_.get_heads());
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

// ─── Changes ─────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::GetChanges(
    winrt::Windows::Storage::Streams::IBuffer const& heads)
{
    try {
        auto h = ibuffer_to_bytes(heads);
        return bytes_to_ibuffer(doc_.get_changes(h));
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

void Document::ApplyChanges(winrt::Windows::Storage::Streams::IBuffer const& changes) {
    try {
        auto ch = ibuffer_to_bytes(changes);
        doc_.apply_changes(ch);
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

// ─── Merge ───────────────────────────────────────────────────────────────────

void Document::Merge(winrt::Automerge::Windows::Document const& other) {
    try {
        // other is the WinRT projected type; extract the implementation.
        auto other_impl = winrt::get_self<implementation::Document>(other);
        doc_.merge(other_impl->native_doc());
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

// ─── Read ────────────────────────────────────────────────────────────────────

hstring Document::GetValue(hstring const& pathJson) {
    try {
        auto result = doc_.get_value(hstring_to_string(pathJson));
        return string_to_hstring(result);
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

// ─── Write ───────────────────────────────────────────────────────────────────

void Document::PutJsonRoot(hstring const& jsonObj) {
    try {
        doc_.put_json_root(hstring_to_string(jsonObj));
    } catch (const std::exception& ex) {
        throw_winrt_error(ex);
    }
}

} // namespace winrt::Automerge::Windows::implementation
