// Document.h — WinRT runtime class implementation header.
#pragma once

#include "pch.h"
#include "Helpers.hpp"
#include "Document.g.h"

namespace winrt::Automerge::Windows::implementation {

struct Document : DocumentT<Document>
{
    Document();

    // Persistence
    winrt::Windows::Storage::Streams::IBuffer Save();
    static winrt::Automerge::Windows::Document Load(
        winrt::Windows::Storage::Streams::IBuffer const& data);
    winrt::Windows::Storage::Streams::IBuffer SaveIncremental();

    // Fork
    winrt::Automerge::Windows::Document Fork();

    // Heads
    winrt::Windows::Storage::Streams::IBuffer GetHeads();

    // Changes
    winrt::Windows::Storage::Streams::IBuffer GetChanges(
        winrt::Windows::Storage::Streams::IBuffer const& heads);
    void ApplyChanges(winrt::Windows::Storage::Streams::IBuffer const& changes);

    // Merge
    void Merge(winrt::Automerge::Windows::Document const& other);

    // Actor
    winrt::Windows::Storage::Streams::IBuffer GetActorId();
    void SetActorId(winrt::Windows::Storage::Streams::IBuffer const& actorId);

    // Read
    hstring GetValue(hstring const& pathJson);
    hstring Get(hstring const& objId, hstring const& key);
    hstring GetIdx(hstring const& objId, int32_t index);
    hstring GetKeysJson(hstring const& objId);
    int32_t GetLength(hstring const& objId);
    hstring GetText(hstring const& objId);
    hstring GetAllJson(hstring const& objId, hstring const& key);

    // Write
    void PutJsonRoot(hstring const& jsonObj);
    void Put(hstring const& objId, hstring const& key, hstring const& scalarJson);
    void PutIdx(hstring const& objId, int32_t index, hstring const& scalarJson);
    hstring PutObject(hstring const& objId, hstring const& key, hstring const& objType);
    void Delete(hstring const& objId, hstring const& key);

    // List operations
    void Insert(hstring const& listObjId, int32_t index, hstring const& scalarJson);
    hstring InsertObject(hstring const& listObjId, int32_t index, hstring const& objType);
    void DeleteAt(hstring const& listObjId, int32_t index);

    // Counter
    void PutCounter(hstring const& objId, hstring const& key, int64_t initialValue);
    void Increment(hstring const& objId, hstring const& key, int64_t delta);

    // Text CRDT
    void SpliceText(hstring const& textObjId, int32_t start, int32_t deleteCount,
                    hstring const& text);

    // Commit
    void Commit(hstring const& message, int64_t timestamp);

    // Diff
    hstring DiffIncremental();

    // ─── New APIs ────────────────────────────────────────────────────────

    winrt::Windows::Storage::Streams::IBuffer GetAllChanges();
    winrt::Windows::Storage::Streams::IBuffer GetLastLocalChange();
    winrt::Windows::Storage::Streams::IBuffer GetMissingDeps(
        winrt::Windows::Storage::Streams::IBuffer const& heads);
    void EmptyChange(hstring const& message, int64_t timestamp);
    winrt::Windows::Storage::Streams::IBuffer SaveAfter(
        winrt::Windows::Storage::Streams::IBuffer const& heads);
    void LoadIncremental(winrt::Windows::Storage::Streams::IBuffer const& data);
    winrt::Automerge::Windows::Document ForkAt(
        winrt::Windows::Storage::Streams::IBuffer const& heads);
    hstring ObjectType(hstring const& objId);
    hstring Diff(winrt::Windows::Storage::Streams::IBuffer const& beforeHeads,
                 winrt::Windows::Storage::Streams::IBuffer const& afterHeads);
    void UpdateText(hstring const& textObjId, hstring const& newText);
    void Mark(hstring const& textObjId, int32_t start, int32_t end,
              hstring const& name, hstring const& valueJson, uint8_t expand);
    void Unmark(hstring const& textObjId, hstring const& name,
                int32_t start, int32_t end, uint8_t expand);
    hstring GetMarks(hstring const& textObjId);
    hstring GetMarksAt(hstring const& textObjId,
                       winrt::Windows::Storage::Streams::IBuffer const& heads);
    hstring GetCursor(hstring const& textObjId, int32_t position);
    int32_t GetCursorPosition(hstring const& textObjId, hstring const& cursor);
    hstring GetSpans(hstring const& textObjId);
    hstring GetStats();
    hstring MapRange(hstring const& objId, hstring const& start, hstring const& end);
    hstring ListRange(hstring const& objId, int32_t start, int32_t end);

    // Block marker APIs
    hstring SplitBlock(hstring const& textObjId, int32_t index);
    void JoinBlock(hstring const& textObjId, int32_t index);
    hstring ReplaceBlock(hstring const& textObjId, int32_t index);

    // Additional gap-closing APIs
    winrt::Windows::Storage::Streams::IBuffer GetChangeByHash(
        winrt::Windows::Storage::Streams::IBuffer const& hash);
    bool HasHeads(winrt::Windows::Storage::Streams::IBuffer const& heads);

    // Change metadata APIs
    hstring GetChangesMeta(winrt::Windows::Storage::Streams::IBuffer const& heads);
    hstring InspectChange(winrt::Windows::Storage::Streams::IBuffer const& hash);

    // Internal
    ::automerge::Document& native_doc() noexcept { return doc_; }

private:
    ::automerge::Document doc_;
};

} // namespace winrt::Automerge::Windows::implementation

namespace winrt::Automerge::Windows::factory_implementation {
struct Document : DocumentT<Document, implementation::Document> {};
} // namespace winrt::Automerge::Windows::factory_implementation
