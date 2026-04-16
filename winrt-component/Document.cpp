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

// ─── New APIs ────────────────────────────────────────────────────────────────

winrt::Windows::Storage::Streams::IBuffer Document::GetAllChanges() {
    try { return bytes_to_ibuffer(doc_.get_all_changes()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

winrt::Windows::Storage::Streams::IBuffer Document::GetLastLocalChange() {
    try { return bytes_to_ibuffer(doc_.get_last_local_change()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

winrt::Windows::Storage::Streams::IBuffer Document::GetMissingDeps(
    winrt::Windows::Storage::Streams::IBuffer const& heads)
{
    try {
        auto h = ibuffer_to_bytes(heads);
        return bytes_to_ibuffer(doc_.get_missing_deps(h));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::EmptyChange(hstring const& message, int64_t timestamp) {
    try { doc_.empty_change(hstring_to_string(message), timestamp); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

winrt::Windows::Storage::Streams::IBuffer Document::SaveAfter(
    winrt::Windows::Storage::Streams::IBuffer const& heads)
{
    try {
        auto h = ibuffer_to_bytes(heads);
        return bytes_to_ibuffer(doc_.save_after(h));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::LoadIncremental(winrt::Windows::Storage::Streams::IBuffer const& data) {
    try { doc_.load_incremental(ibuffer_to_bytes(data)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

winrt::Automerge::Windows::Document Document::ForkAt(
    winrt::Windows::Storage::Streams::IBuffer const& heads)
{
    try {
        auto h = ibuffer_to_bytes(heads);
        auto forked = doc_.fork_at(h);
        auto impl = winrt::make<implementation::Document>();
        winrt::get_self<implementation::Document>(impl)->native_doc() = std::move(forked);
        return impl;
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::ObjectType(hstring const& objId) {
    try { return string_to_hstring(doc_.object_type(hstring_to_string(objId))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::Diff(winrt::Windows::Storage::Streams::IBuffer const& beforeHeads,
                       winrt::Windows::Storage::Streams::IBuffer const& afterHeads)
{
    try {
        auto b = ibuffer_to_bytes(beforeHeads);
        auto a = ibuffer_to_bytes(afterHeads);
        return string_to_hstring(doc_.diff(b, a));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::UpdateText(hstring const& textObjId, hstring const& newText) {
    try { doc_.update_text(hstring_to_string(textObjId), hstring_to_string(newText)); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::Mark(hstring const& textObjId, int32_t start, int32_t end,
                    hstring const& name, hstring const& valueJson, uint8_t expand)
{
    try {
        doc_.mark(hstring_to_string(textObjId), static_cast<size_t>(start),
                  static_cast<size_t>(end), hstring_to_string(name),
                  hstring_to_string(valueJson), expand);
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

void Document::Unmark(hstring const& textObjId, hstring const& name,
                      int32_t start, int32_t end, uint8_t expand)
{
    try {
        doc_.unmark(hstring_to_string(textObjId), hstring_to_string(name),
                    static_cast<size_t>(start), static_cast<size_t>(end), expand);
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetMarks(hstring const& textObjId) {
    try { return string_to_hstring(doc_.marks(hstring_to_string(textObjId))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetMarksAt(hstring const& textObjId,
                             winrt::Windows::Storage::Streams::IBuffer const& heads)
{
    try {
        auto h = ibuffer_to_bytes(heads);
        return string_to_hstring(doc_.marks_at(hstring_to_string(textObjId), h));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetCursor(hstring const& textObjId, int32_t position) {
    try {
        return string_to_hstring(
            doc_.get_cursor(hstring_to_string(textObjId), static_cast<size_t>(position)));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

int32_t Document::GetCursorPosition(hstring const& textObjId, hstring const& cursor) {
    try {
        return static_cast<int32_t>(
            doc_.get_cursor_position(hstring_to_string(textObjId), hstring_to_string(cursor)));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetSpans(hstring const& textObjId) {
    try { return string_to_hstring(doc_.spans(hstring_to_string(textObjId))); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::GetStats() {
    try { return string_to_hstring(doc_.stats()); }
    catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::MapRange(hstring const& objId, hstring const& start, hstring const& end) {
    try {
        return string_to_hstring(
            doc_.map_range(hstring_to_string(objId), hstring_to_string(start),
                           hstring_to_string(end)));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

hstring Document::ListRange(hstring const& objId, int32_t start, int32_t end) {
    try {
        return string_to_hstring(
            doc_.list_range(hstring_to_string(objId),
                            static_cast<size_t>(start), static_cast<size_t>(end)));
    } catch (const std::exception& ex) { throw_winrt_error(ex); }
}

} // namespace winrt::Automerge::Windows::implementation

