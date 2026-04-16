using System;
using System.Linq;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests
{
    /// <summary>
    /// Tests for the new Document APIs added to close the gap with JS @automerge/automerge.
    /// </summary>
    public class NewApiTests
    {
        // ─── getAllChanges ─────────────────────────────────────────────────────

        [Fact]
        public void GetAllChanges_ReturnsBytes()
        {
            using var doc = new Document();
            doc.Put(null, "key", "\"val\"");
            var changes = doc.GetAllChanges();
            Assert.True(changes.Length > 0);
        }

        [Fact]
        public void GetAllChanges_EmptyDoc_ReturnsEmpty()
        {
            using var doc = new Document();
            var changes = doc.GetAllChanges();
            Assert.Empty(changes);
        }

        // ─── getLastLocalChange ───────────────────────────────────────────────

        [Fact]
        public void GetLastLocalChange_AfterPut_ReturnsBytes()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            doc.Commit();
            var change = doc.GetLastLocalChange();
            Assert.True(change.Length > 0);
        }

        [Fact]
        public void GetLastLocalChange_EmptyDoc_ReturnsEmpty()
        {
            using var doc = new Document();
            var change = doc.GetLastLocalChange();
            Assert.Empty(change);
        }

        // ─── getMissingDeps ───────────────────────────────────────────────────

        [Fact]
        public void GetMissingDeps_CurrentHeads_ReturnsEmpty()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var heads = doc.GetHeads();
            var deps = doc.GetMissingDeps(heads);
            Assert.Empty(deps);
        }

        // ─── emptyChange ──────────────────────────────────────────────────────

        [Fact]
        public void EmptyChange_CreatesChange()
        {
            using var doc = new Document();
            var headsBefore = doc.GetHeads();
            doc.EmptyChange("test empty");
            var headsAfter = doc.GetHeads();
            Assert.NotEqual(headsBefore, headsAfter);
        }

        // ─── saveAfter ────────────────────────────────────────────────────────

        [Fact]
        public void SaveAfter_ReturnsIncrementalBytes()
        {
            using var doc = new Document();
            doc.Put(null, "k1", "1");
            var heads1 = doc.GetHeads();
            doc.Put(null, "k2", "2");
            var incremental = doc.SaveAfter(heads1);
            Assert.True(incremental.Length > 0);
        }

        // ─── loadIncremental ──────────────────────────────────────────────────

        [Fact]
        public void LoadIncremental_AppliesChanges()
        {
            using var doc1 = new Document();
            doc1.Put(null, "lk", "\"lv\"");
            doc1.Put(null, "lk2", "\"lv2\"");
            var fullSave = doc1.Save();

            using var doc2 = new Document();
            doc2.LoadIncremental(fullSave);
            Assert.Equal("\"lv\"", doc2.Get(null, "lk"));
            Assert.Equal("\"lv2\"", doc2.Get(null, "lk2"));
        }

        // ─── forkAt ──────────────────────────────────────────────────────────

        [Fact]
        public void ForkAt_ProducesHistoricalSnapshot()
        {
            using var doc = new Document();
            doc.Put(null, "v", "1");
            var heads1 = doc.GetHeads();
            doc.Put(null, "v", "2");

            using var snapshot = doc.ForkAt(heads1);
            Assert.Equal("1", snapshot.Get(null, "v"));
            Assert.Equal("2", doc.Get(null, "v"));
        }

        // ─── objectType ──────────────────────────────────────────────────────

        [Fact]
        public void ObjectType_Root_ReturnsMap()
        {
            using var doc = new Document();
            var ot = doc.ObjectType(null);
            Assert.Equal("\"map\"", ot);
        }

        [Fact]
        public void ObjectType_Text_ReturnsText()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            var ot = doc.ObjectType(textId);
            Assert.Equal("\"text\"", ot);
        }

        [Fact]
        public void ObjectType_List_ReturnsList()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            var ot = doc.ObjectType(listId);
            Assert.Equal("\"list\"", ot);
        }

        // ─── diff ─────────────────────────────────────────────────────────────

        [Fact]
        public void Diff_DetectsChanges()
        {
            using var doc = new Document();
            doc.Put(null, "a", "1");
            var heads1 = doc.GetHeads();
            doc.Put(null, "b", "2");
            var heads2 = doc.GetHeads();
            var patches = doc.Diff(heads1, heads2);
            Assert.Contains("put_map", patches);
            Assert.Contains("\"b\"", patches);
        }

        // ─── updateText ──────────────────────────────────────────────────────

        [Fact]
        public void UpdateText_ReplacesContent()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello");
            doc.UpdateText(textId, "world");
            Assert.Equal("world", doc.GetText(textId));
        }

        // ─── mark / unmark / getMarks ─────────────────────────────────────────

        [Fact]
        public void Mark_And_GetMarks_RoundTrips()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello world");
            doc.Mark(textId, 0, 5, "bold", "true", 3);
            var marks = doc.GetMarks(textId);
            Assert.Contains("bold", marks);
            Assert.Contains("true", marks);
        }

        [Fact]
        public void Unmark_RemovesMark()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello");
            doc.Mark(textId, 0, 5, "bold", "true", 3);
            doc.Unmark(textId, "bold", 0, 5, 3);
            var marks = doc.GetMarks(textId);
            Assert.Equal("[]", marks);
        }

        // ─── getMarksAt ──────────────────────────────────────────────────────

        [Fact]
        public void GetMarksAt_ReturnsMarksAtHeads()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello");
            doc.Mark(textId, 0, 5, "italic", "true", 3);
            var heads = doc.GetHeads();
            var marks = doc.GetMarksAt(textId, heads);
            Assert.Contains("italic", marks);
        }

        // ─── getCursor / getCursorPosition ────────────────────────────────────

        [Fact]
        public void Cursor_RoundTrips()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello world");
            var cursor = doc.GetCursor(textId, 5);
            Assert.False(string.IsNullOrEmpty(cursor));
            var pos = doc.GetCursorPosition(textId, cursor);
            Assert.Equal(5, pos);
        }

        // ─── spans ────────────────────────────────────────────────────────────

        [Fact]
        public void GetSpans_ReturnsTextSpans()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello");
            var spans = doc.GetSpans(textId);
            Assert.Contains("text", spans);
            Assert.Contains("hello", spans);
        }

        // ─── stats ───────────────────────────────────────────────────────────

        [Fact]
        public void GetStats_ReturnsNonEmptyJson()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var stats = doc.GetStats();
            Assert.False(string.IsNullOrEmpty(stats));
        }

        // ─── mapRange ─────────────────────────────────────────────────────────

        [Fact]
        public void MapRange_FullRange_ReturnsAllEntries()
        {
            using var doc = new Document();
            doc.Put(null, "a", "1");
            doc.Put(null, "b", "2");
            doc.Put(null, "c", "3");
            var result = doc.MapRange(null);
            Assert.Contains("\"a\"", result);
            Assert.Contains("\"b\"", result);
            Assert.Contains("\"c\"", result);
        }

        [Fact]
        public void MapRange_WithBounds_FiltersEntries()
        {
            using var doc = new Document();
            doc.Put(null, "a", "1");
            doc.Put(null, "b", "2");
            doc.Put(null, "c", "3");
            var result = doc.MapRange(null, "b", "c");
            Assert.Contains("\"b\"", result);
            Assert.DoesNotContain("[\"a\"", result);
        }

        // ─── listRange ────────────────────────────────────────────────────────

        [Fact]
        public void ListRange_ReturnsSubset()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            doc.Insert(listId, 0, "\"a\"");
            doc.Insert(listId, 1, "\"b\"");
            doc.Insert(listId, 2, "\"c\"");
            var result = doc.ListRange(listId, 0, 2);
            Assert.Contains("\"a\"", result);
            Assert.Contains("\"b\"", result);
        }

        // ─── PREVIOUSLY UNTESTED: SaveIncremental ─────────────────────────────

        [Fact]
        public void SaveIncremental_ReturnsBytes()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var inc = doc.SaveIncremental();
            Assert.True(inc.Length > 0);
        }

        [Fact]
        public void SaveIncremental_SecondCall_ReturnsEmpty()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            _ = doc.SaveIncremental();
            var inc2 = doc.SaveIncremental();
            Assert.Empty(inc2);
        }

        // ─── PREVIOUSLY UNTESTED: Fork ────────────────────────────────────────

        [Fact]
        public void Fork_ProducesIndependentCopy()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            using var forked = doc.Fork();
            forked.Put(null, "k", "2");
            Assert.Equal("1", doc.Get(null, "k"));
            Assert.Equal("2", forked.Get(null, "k"));
        }

        // ─── PREVIOUSLY UNTESTED: Actor ID ────────────────────────────────────

        [Fact]
        public void GetActorId_ReturnsNonEmpty()
        {
            using var doc = new Document();
            var actor = doc.GetActorId();
            Assert.True(actor.Length > 0);
        }

        [Fact]
        public void SetActorId_ChangesActor()
        {
            using var doc = new Document();
            var newActor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            doc.SetActorId(newActor);
            var got = doc.GetActorId();
            Assert.Equal(newActor, got);
        }

        // ─── PREVIOUSLY UNTESTED: GetIdx ──────────────────────────────────────

        [Fact]
        public void GetIdx_ReturnsListElement()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            doc.Insert(listId, 0, "\"hello\"");
            doc.Insert(listId, 1, "42");
            Assert.Equal("\"hello\"", doc.GetIdx(listId, 0));
            Assert.Equal("42", doc.GetIdx(listId, 1));
        }

        // ─── PREVIOUSLY UNTESTED: GetKeysJson / GetKeys ──────────────────────

        [Fact]
        public void GetKeysJson_RootKeys()
        {
            using var doc = new Document();
            doc.Put(null, "alpha", "1");
            doc.Put(null, "beta", "2");
            var keys = doc.GetKeysJson(null);
            Assert.Contains("alpha", keys);
            Assert.Contains("beta", keys);
        }

        [Fact]
        public void GetKeys_ReturnsArray()
        {
            using var doc = new Document();
            doc.Put(null, "x", "1");
            doc.Put(null, "y", "2");
            var keys = doc.GetKeys(null);
            Assert.Contains("x", keys);
            Assert.Contains("y", keys);
        }

        // ─── PREVIOUSLY UNTESTED: GetLength ───────────────────────────────────

        [Fact]
        public void GetLength_Root_ReturnsKeyCount()
        {
            using var doc = new Document();
            doc.Put(null, "a", "1");
            doc.Put(null, "b", "2");
            Assert.Equal(2, doc.GetLength(null));
        }

        [Fact]
        public void GetLength_List_ReturnsItemCount()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            doc.Insert(listId, 0, "1");
            doc.Insert(listId, 1, "2");
            doc.Insert(listId, 2, "3");
            Assert.Equal(3, doc.GetLength(listId));
        }

        // ─── PREVIOUSLY UNTESTED: GetAllJson (conflicts) ─────────────────────

        [Fact]
        public void GetAllJson_SingleValue_ReturnsArray()
        {
            using var doc = new Document();
            doc.Put(null, "k", "\"v\"");
            var all = doc.GetAllJson(null, "k");
            Assert.Contains("\"v\"", all);
            Assert.StartsWith("[", all);
        }

        // ─── PREVIOUSLY UNTESTED: PutIdx ──────────────────────────────────────

        [Fact]
        public void PutIdx_OverwritesListElement()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            doc.Insert(listId, 0, "\"old\"");
            doc.PutIdx(listId, 0, "\"new\"");
            Assert.Equal("\"new\"", doc.GetIdx(listId, 0));
        }

        // ─── PREVIOUSLY UNTESTED: Delete ──────────────────────────────────────

        [Fact]
        public void Delete_RemovesKey()
        {
            using var doc = new Document();
            doc.Put(null, "gone", "\"bye\"");
            Assert.Equal("\"bye\"", doc.Get(null, "gone"));
            doc.Delete(null, "gone");
            Assert.Throws<AutomergeNativeException>(() => doc.Get(null, "gone"));
        }

        // ─── PREVIOUSLY UNTESTED: InsertObject ────────────────────────────────

        [Fact]
        public void InsertObject_CreatesNestedObject()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            var nestedId = doc.InsertObject(listId, 0, "map");
            Assert.False(string.IsNullOrEmpty(nestedId));
            doc.Put(nestedId, "inner", "\"value\"");
            Assert.Equal("\"value\"", doc.Get(nestedId, "inner"));
        }

        // ─── PREVIOUSLY UNTESTED: DeleteAt ────────────────────────────────────

        [Fact]
        public void DeleteAt_RemovesListElement()
        {
            using var doc = new Document();
            var listId = doc.PutObject(null, "l", "list");
            doc.Insert(listId, 0, "\"a\"");
            doc.Insert(listId, 1, "\"b\"");
            doc.Insert(listId, 2, "\"c\"");
            doc.DeleteAt(listId, 1); // remove "b"
            Assert.Equal(2, doc.GetLength(listId));
            Assert.Equal("\"a\"", doc.GetIdx(listId, 0));
            Assert.Equal("\"c\"", doc.GetIdx(listId, 1));
        }

        // ─── PREVIOUSLY UNTESTED: PutCounter / Increment ──────────────────────

        [Fact]
        public void PutCounter_And_Increment()
        {
            using var doc = new Document();
            doc.PutCounter(null, "count", 0);
            doc.Increment(null, "count", 5);
            doc.Increment(null, "count", 3);
            var val = doc.Get(null, "count");
            Assert.Equal("8", val);
        }

        [Fact]
        public void Counter_MergeAddsValues()
        {
            using var doc1 = new Document();
            doc1.PutCounter(null, "c", 0);

            using var doc2 = doc1.Fork();
            doc1.Increment(null, "c", 3);
            doc2.Increment(null, "c", 7);

            doc1.Merge(doc2);
            Assert.Equal("10", doc1.Get(null, "c"));
        }

        // ─── PREVIOUSLY UNTESTED: DiffIncremental ─────────────────────────────

        [Fact]
        public void DiffIncremental_ReturnsPatches()
        {
            using var doc = new Document();
            _ = doc.DiffIncremental(); // consume initial state
            doc.Put(null, "x", "1");
            var patches = doc.DiffIncremental();
            Assert.Contains("put_map", patches);
            Assert.Contains("\"x\"", patches);
        }

        // ─── PREVIOUSLY UNTESTED: SplitBlock / JoinBlock / ReplaceBlock ───────

        [Fact]
        public void SplitBlock_InsertsBlockMarker()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello world");
            var blockId = doc.SplitBlock(textId, 5);
            Assert.False(string.IsNullOrEmpty(blockId));
            // After split, we can set block properties
            doc.Put(blockId, "type", "\"paragraph\"");
            Assert.Equal("\"paragraph\"", doc.Get(blockId, "type"));
        }

        [Fact]
        public void JoinBlock_RemovesBlockMarker()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello world");
            doc.SplitBlock(textId, 5);
            // Text now has a block marker; length should be 12 (11 chars + 1 block)
            var lenBefore = doc.GetLength(textId);
            doc.JoinBlock(textId, 5);
            var lenAfter = doc.GetLength(textId);
            Assert.True(lenAfter < lenBefore);
        }

        [Fact]
        public void ReplaceBlock_ReturnsNewBlockId()
        {
            using var doc = new Document();
            var textId = doc.PutObject(null, "t", "text");
            doc.SpliceText(textId, 0, 0, "hello world");
            doc.SplitBlock(textId, 5);
            var newBlockId = doc.ReplaceBlock(textId, 5);
            Assert.False(string.IsNullOrEmpty(newBlockId));
            doc.Put(newBlockId, "type", "\"heading\"");
            Assert.Equal("\"heading\"", doc.Get(newBlockId, "type"));
        }

        // ─── PREVIOUSLY UNTESTED: GetChangeByHash ─────────────────────────────

        [Fact]
        public void GetChangeByHash_FindsExistingChange()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var heads = doc.GetHeads();
            Assert.True(heads.Length >= 32);
            // Extract first 32-byte hash
            var hash = heads.AsSpan(0, 32);
            var change = doc.GetChangeByHash(hash);
            Assert.True(change.Length > 0);
        }

        [Fact]
        public void GetChangeByHash_UnknownHash_ReturnsEmpty()
        {
            using var doc = new Document();
            var fakeHash = new byte[32];
            var change = doc.GetChangeByHash(fakeHash);
            Assert.Empty(change);
        }

        // ─── PREVIOUSLY UNTESTED: HasHeads ────────────────────────────────────

        [Fact]
        public void HasHeads_CurrentHeads_ReturnsTrue()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var heads = doc.GetHeads();
            Assert.True(doc.HasHeads(heads));
        }

        [Fact]
        public void HasHeads_UnknownHeads_ReturnsFalse()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var fakeHeads = new byte[32]; // all zeros — not a valid head
            Assert.False(doc.HasHeads(fakeHeads));
        }

        [Fact]
        public void HasHeads_EmptyHeads_ReturnsTrue()
        {
            using var doc = new Document();
            Assert.True(doc.HasHeads(ReadOnlySpan<byte>.Empty));
        }

        // ─── GetChangesMeta ──────────────────────────────────────────────────

        [Fact]
        public void GetChangesMeta_ReturnsJsonArray()
        {
            using var doc = new Document();
            doc.Put(null, "k", "\"v\"");
            var meta = doc.GetChangesMeta();
            Assert.StartsWith("[", meta);
            Assert.Contains("actor", meta);
            Assert.Contains("hash", meta);
            Assert.Contains("seq", meta);
        }

        [Fact]
        public void GetChangesMeta_EmptyDoc_ReturnsEmptyArray()
        {
            using var doc = new Document();
            var meta = doc.GetChangesMeta();
            Assert.Equal("[]", meta);
        }

        [Fact]
        public void GetChangesMeta_SinceHeads_ReturnsOnlyNew()
        {
            using var doc = new Document();
            doc.Put(null, "a", "1");
            var heads = doc.GetHeads();
            doc.Put(null, "b", "2");
            var allMeta = doc.GetChangesMeta();
            var sinceMeta = doc.GetChangesMeta(heads);
            // Since should return fewer (or same) changes than all
            Assert.True(sinceMeta.Length <= allMeta.Length);
            Assert.StartsWith("[", sinceMeta);
        }

        // ─── InspectChange ───────────────────────────────────────────────────

        [Fact]
        public void InspectChange_ExistingHash_ReturnsMetadata()
        {
            using var doc = new Document();
            doc.Put(null, "k", "\"v\"");
            var heads = doc.GetHeads();
            Assert.True(heads.Length >= 32);
            var hash = heads.AsSpan(0, 32);
            var meta = doc.InspectChange(hash);
            Assert.Contains("actor", meta);
            Assert.Contains("hash", meta);
        }

        [Fact]
        public void InspectChange_UnknownHash_ReturnsNull()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var fakeHash = new byte[32];
            var meta = doc.InspectChange(fakeHash);
            Assert.Equal("null", meta);
        }

        // ─── HasOurChanges (on SyncState) ────────────────────────────────────

        [Fact]
        public void HasOurChanges_AfterFullSync_ReturnsTrue()
        {
            using var doc1 = new Document();
            doc1.Put(null, "k", "\"v\"");

            using var doc2 = new Document();

            // Sync doc1 → doc2
            using var sync1 = new SyncState();
            using var sync2 = new SyncState();

            for (int i = 0; i < 10; i++)
            {
                var msg1 = sync1.GenerateSyncMessage(doc1);
                if (msg1.Length > 0) sync2.ReceiveSyncMessage(doc2, msg1);

                var msg2 = sync2.GenerateSyncMessage(doc2);
                if (msg2.Length > 0) sync1.ReceiveSyncMessage(doc1, msg2);

                if (msg1.Length == 0 && msg2.Length == 0) break;
            }

            // After full sync, the remote should have our changes
            Assert.True(sync1.HasOurChanges(doc1));
        }

        [Fact]
        public void HasOurChanges_BeforeSync_ReturnsFalse()
        {
            using var doc = new Document();
            doc.Put(null, "k", "1");

            using var sync = new SyncState();
            // Haven't synced yet — remote doesn't have our changes
            Assert.False(sync.HasOurChanges(doc));
        }
    }
}
