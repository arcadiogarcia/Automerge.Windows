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
    }
}
