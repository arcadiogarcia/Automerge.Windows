// Document.cpp — WinRT Document runtime class implementation.
#include "pch.h"
#include "Document.h"
#include "SyncState.h"

namespace winrt::Automerge::Windows::implementation {

using namespace AutomergeWinRT;

// Helper: convert hstring to std::string; empty hstring → empty string (= ROOT).
static std::string hs(hstring const& h) {
    return hstring_to_string(h);
}

Document::Document() : doc_() {}

// ─── Persistence ─────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::Save() {
    try { return bytes_to_ibuffer(doc_.save()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
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
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

winrt::Windows::Storage::Streams::IBuffer Document::SaveIncremental() {
    try { return bytes_to_ibuffer(doc_.save_incremental()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Fork ─────────────────────────────────────────────────────────────────────

winrt::Automerge::Windows::Document Document::Fork() {
    try {
        auto impl = winrt::make<implementation::Document>();
        winrt::get_self<implementation::Document>(impl)->doc_ = doc_.fork();
        return impl;
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Heads ───────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::GetHeads() {
    try { return bytes_to_ibuffer(doc_.get_heads()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Changes ─────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::GetChanges(
    winrt::Windows::Storage::Streams::IBuffer const& heads)
{
    try {
        auto h = ibuffer_to_bytes(heads);
        return bytes_to_ibuffer(doc_.get_changes(h));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::ApplyChanges(winrt::Windows::Storage::Streams::IBuffer const& changes) {
    try {
        auto ch = ibuffer_to_bytes(changes);
        doc_.apply_changes(ch);
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Merge ───────────────────────────────────────────────────────────────────

void Document::Merge(winrt::Automerge::Windows::Document const& other) {
    try {
        auto other_impl = winrt::get_self<implementation::Document>(other);
        doc_.merge(other_impl->native_doc());
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Actor ───────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::GetActorId() {
    try { return bytes_to_ibuffer(doc_.get_actor()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::SetActorId(winrt::Windows::Storage::Streams::IBuffer const& actorId) {
    try {
        auto bytes = ibuffer_to_bytes(actorId);
        doc_.set_actor(bytes);
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Read ────────────────────────────────────────────────────────────────────

hstring Document::GetValue(hstring const& pathJson) {
    try { return string_to_hstring(doc_.get_value(hs(pathJson))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::Get(hstring const& objId, hstring const& key) {
    try { return string_to_hstring(doc_.get(hs(objId), hs(key))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetIdx(hstring const& objId, int32_t index) {
    try { return string_to_hstring(doc_.get_idx(hs(objId), static_cast<size_t>(index))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetKeysJson(hstring const& objId) {
    try { return string_to_hstring(doc_.keys(hs(objId))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

int32_t Document::GetLength(hstring const& objId) {
    try { return static_cast<int32_t>(doc_.length(hs(objId))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetText(hstring const& objId) {
    try { return string_to_hstring(doc_.get_text(hs(objId))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetAllJson(hstring const& objId, hstring const& key) {
    try { return string_to_hstring(doc_.get_all(hs(objId), hs(key))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Write ───────────────────────────────────────────────────────────────────

void Document::PutJsonRoot(hstring const& jsonObj) {
    try { doc_.put_json_root(hs(jsonObj)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::Put(hstring const& objId, hstring const& key, hstring const& scalarJson) {
    try { doc_.put(hs(objId), hs(key), hs(scalarJson)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::PutIdx(hstring const& objId, int32_t index, hstring const& scalarJson) {
    try { doc_.put_idx(hs(objId), static_cast<size_t>(index), hs(scalarJson)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::PutObject(hstring const& objId, hstring const& key,
                             hstring const& objType)
{
    try { return string_to_hstring(doc_.put_object(hs(objId), hs(key), hs(objType))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::Delete(hstring const& objId, hstring const& key) {
    try { doc_.del(hs(objId), hs(key)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── List operations ─────────────────────────────────────────────────────────

void Document::Insert(hstring const& listObjId, int32_t index, hstring const& scalarJson) {
    try {
        size_t idx = (index < 0) ? SIZE_MAX : static_cast<size_t>(index);
        doc_.insert(hs(listObjId), idx, hs(scalarJson));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::InsertObject(hstring const& listObjId, int32_t index,
                                hstring const& objType)
{
    try {
        size_t idx = (index < 0) ? SIZE_MAX : static_cast<size_t>(index);
        return string_to_hstring(doc_.insert_object(hs(listObjId), idx, hs(objType)));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::DeleteAt(hstring const& listObjId, int32_t index) {
    try { doc_.delete_at(hs(listObjId), static_cast<size_t>(index)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Counter ─────────────────────────────────────────────────────────────────

void Document::PutCounter(hstring const& objId, hstring const& key, int64_t initialValue) {
    try { doc_.put_counter(hs(objId), hs(key), initialValue); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::Increment(hstring const& objId, hstring const& key, int64_t delta) {
    try { doc_.increment(hs(objId), hs(key), delta); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Text CRDT ───────────────────────────────────────────────────────────────

void Document::SpliceText(hstring const& textObjId, int32_t start, int32_t deleteCount,
                           hstring const& text)
{
    try {
        doc_.splice_text(hs(textObjId), static_cast<size_t>(start),
                         static_cast<std::ptrdiff_t>(deleteCount), hs(text));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Commit ──────────────────────────────────────────────────────────────────

void Document::Commit(hstring const& message, int64_t timestamp) {
    try { doc_.commit(hs(message), timestamp); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

// ─── Diff ────────────────────────────────────────────────────────────────────

hstring Document::DiffIncremental() {
    try { return string_to_hstring(doc_.diff_incremental()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

} // namespace winrt::Automerge::Windows::implementation

