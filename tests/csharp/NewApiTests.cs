using System;
using System.Linq;
using System.Text.Json;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests;

/// <summary>Tests for all new APIs: actor, fine-grained read/write, nested objects,
/// delete, list ops, counters, text, fork, incremental save, commit, diff, conflicts.</summary>
public class NewApiTests
{
    // ─── Actor ID ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetActorId_ReturnsNonEmptyBytes()
    {
        using var doc = new Document();
        var actor = doc.GetActorId();
        Assert.NotEmpty(actor);
    }

    [Fact]
    public void SetActorId_ThenGet_RoundTrips()
    {
        using var doc = new Document();
        var actor = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };
        doc.SetActorId(actor);
        var got = doc.GetActorId();
        Assert.Equal(actor, got);
    }

    [Fact]
    public void TwoDocs_HaveDifferentActorIds()
    {
        using var doc1 = new Document();
        using var doc2 = new Document();
        Assert.NotEqual(doc1.GetActorId(), doc2.GetActorId());
    }

    // ─── Fine-grained read ────────────────────────────────────────────────────

    [Fact]
    public void Get_ScalarAtRoot_ReturnsJsonScalar()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"title":"Dune","pages":412}""");

        Assert.Equal("\"Dune\"", doc.Get(null, "title"));
        Assert.Equal("412", doc.Get(null, "pages"));
    }

    [Fact]
    public void Get_MissingKey_Throws()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"x":1}""");
        Assert.Throws<AutomergeNativeException>(() => doc.Get(null, "missing"));
    }

    [Fact]
    public void GetKeys_ReturnsAllKeys()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"a":1,"b":2,"c":3}""");
        var keys = doc.GetKeys(null);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
        Assert.Equal(3, keys.Length);
    }

    [Fact]
    public void GetLength_Map_ReturnKeyCount()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"x":1,"y":2}""");
        Assert.Equal(2, doc.GetLength(null));
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesKey()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"keep":"yes","remove":"no"}""");
        doc.Delete(null, "remove");

        var keys = doc.GetKeys(null);
        Assert.Contains("keep", keys);
        Assert.DoesNotContain("remove", keys);
    }

    [Fact]
    public void Delete_MissingKey_DoesNotThrow()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"k":"v"}""");
        // Automerge delete on non-existent key is a no-op
        var ex = Record.Exception(() => doc.Delete(null, "nonexistent"));
        Assert.Null(ex);
    }

    // ─── Nested maps (put_object / put) ───────────────────────────────────────

    [Fact]
    public void PutObject_CreatesNestedMap()
    {
        using var doc = new Document();
        var bookId = doc.PutObject(null, "book:1", "map");
        Assert.NotEmpty(bookId);

        doc.Put(bookId, "title", "\"Dune\"");
        doc.Put(bookId, "rating", "5");

        Assert.Equal("\"Dune\"", doc.Get(bookId, "title"));
        Assert.Equal("5", doc.Get(bookId, "rating"));
    }

    [Fact]
    public void PutObject_NestedInNested_Works()
    {
        using var doc = new Document();
        var booksId = doc.PutObject(null, "books", "map");
        var book1Id = doc.PutObject(booksId, "1", "map");
        doc.Put(book1Id, "title", "\"Foundation\"");
        Assert.Equal("\"Foundation\"", doc.Get(book1Id, "title"));
    }

    [Fact]
    public void GetValue_NestedMapReturnsObjIdJson()
    {
        using var doc = new Document();
        var nId = doc.PutObject(null, "nested", "map");
        var raw = doc.Get(null, "nested");
        var obj = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.True(obj.TryGetProperty("_obj_id", out var idProp));
        Assert.Equal(nId, idProp.GetString());
        Assert.Equal("map", obj.GetProperty("_obj_type").GetString());
    }

    [Fact]
    public void NestedMap_PerFieldMerge_BothChangesPreserved()
    {
        // This is the key CRDT benefit: two users edit different fields of
        // the same nested map → both changes survive.
        using var origin = new Document();
        var bookId = origin.PutObject(null, "book", "map");
        origin.Put(bookId, "title", "\"Unknown\"");
        var snap = origin.Save();

        using var peerA = Document.Load(snap);
        using var peerB = Document.Load(snap);

        // PeerA adds bookId from snap; in the loaded doc we must look it up.
        var aBookId = GetNestedObjId(peerA, null, "book");
        var bBookId = GetNestedObjId(peerB, null, "book");

        peerA.Put(aBookId, "rating", "5");
        peerB.Put(bBookId, "notes", "\"great read\"");

        peerA.Merge(peerB);

        var mergedBookId = GetNestedObjId(peerA, null, "book");
        Assert.Equal("5", peerA.Get(mergedBookId, "rating"));
        Assert.Equal("\"great read\"", peerA.Get(mergedBookId, "notes"));
    }

    // ─── Fine-grained put — types ─────────────────────────────────────────────

    [Fact]
    public void Put_AllScalarTypes_Roundtrip()
    {
        using var doc = new Document();
        doc.Put(null, "str", "\"hello\"");
        doc.Put(null, "int", "-42");
        doc.Put(null, "float", "3.14");
        doc.Put(null, "bool", "true");
        doc.Put(null, "nil", "null");

        Assert.Equal("\"hello\"", doc.Get(null, "str"));
        Assert.Equal("-42", doc.Get(null, "int"));
        Assert.Equal("3.14", doc.Get(null, "float"));
        Assert.Equal("true", doc.Get(null, "bool"));
        Assert.Equal("null", doc.Get(null, "nil"));
    }

    // ─── List operations ──────────────────────────────────────────────────────

    [Fact]
    public void List_InsertAndGet()
    {
        using var doc = new Document();
        var listId = doc.PutObject(null, "items", "list");

        doc.Insert(listId, 0, "\"first\"");
        doc.Insert(listId, 1, "\"second\"");
        doc.Insert(listId, 2, "\"third\"");

        Assert.Equal(3, doc.GetLength(listId));
        Assert.Equal("\"first\"", doc.GetIdx(listId, 0));
        Assert.Equal("\"second\"", doc.GetIdx(listId, 1));
        Assert.Equal("\"third\"", doc.GetIdx(listId, 2));
    }

    [Fact]
    public void List_InsertAppend_AddsToEnd()
    {
        using var doc = new Document();
        var listId = doc.PutObject(null, "list", "list");
        doc.Insert(listId, int.MaxValue, "\"a\"");
        doc.Insert(listId, int.MaxValue, "\"b\"");
        Assert.Equal(2, doc.GetLength(listId));
        Assert.Equal("\"a\"", doc.GetIdx(listId, 0));
        Assert.Equal("\"b\"", doc.GetIdx(listId, 1));
    }

    [Fact]
    public void List_DeleteAt_RemovesElement()
    {
        using var doc = new Document();
        var listId = doc.PutObject(null, "list", "list");
        doc.Insert(listId, 0, "\"a\"");
        doc.Insert(listId, 1, "\"b\"");
        doc.Insert(listId, 2, "\"c\"");

        doc.DeleteAt(listId, 1); // remove "b"

        Assert.Equal(2, doc.GetLength(listId));
        Assert.Equal("\"a\"", doc.GetIdx(listId, 0));
        Assert.Equal("\"c\"", doc.GetIdx(listId, 1));
    }

    [Fact]
    public void List_PutIdx_OverwritesElement()
    {
        using var doc = new Document();
        var listId = doc.PutObject(null, "list", "list");
        doc.Insert(listId, 0, "\"old\"");
        doc.PutIdx(listId, 0, "\"new\"");
        Assert.Equal("\"new\"", doc.GetIdx(listId, 0));
    }

    [Fact]
    public void List_InsertObject_CreatesNestedMap()
    {
        using var doc = new Document();
        var listId = doc.PutObject(null, "people", "list");
        var personId = doc.InsertObject(listId, 0, "map");
        doc.Put(personId, "name", "\"Alice\"");
        Assert.Equal("\"Alice\"", doc.Get(personId, "name"));

        var raw = doc.GetIdx(listId, 0);
        var obj = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.True(obj.TryGetProperty("_obj_id", out _));
    }

    // ─── Counter ──────────────────────────────────────────────────────────────

    [Fact]
    public void Counter_PutAndIncrement()
    {
        using var doc = new Document();
        doc.PutCounter(null, "views", 0);
        var val = doc.Get(null, "views");
        Assert.Equal("0", val);

        doc.Increment(null, "views", 5);
        Assert.Equal("5", doc.Get(null, "views"));

        doc.Increment(null, "views", -2);
        Assert.Equal("3", doc.Get(null, "views"));
    }

    [Fact]
    public void Counter_ConcurrentIncrements_SumCorrectly()
    {
        using var origin = new Document();
        origin.PutCounter(null, "score", 0);
        var snap = origin.Save();

        using var peerA = Document.Load(snap);
        using var peerB = Document.Load(snap);

        peerA.Increment(null, "score", 10);
        peerB.Increment(null, "score", 5);

        peerA.Merge(peerB);

        // CRDT counters: concurrent increments are summed
        Assert.Equal("15", peerA.Get(null, "score"));
    }

    // ─── Text CRDT ────────────────────────────────────────────────────────────

    [Fact]
    public void Text_SpliceAndGet()
    {
        using var doc = new Document();
        var textId = doc.PutObject(null, "note", "text");

        doc.SpliceText(textId, 0, 0, "Hello");
        doc.SpliceText(textId, 5, 0, ", World");

        Assert.Equal("Hello, World", doc.GetText(textId));
    }

    [Fact]
    public void Text_Delete_RemovesChars()
    {
        using var doc = new Document();
        var textId = doc.PutObject(null, "note", "text");
        doc.SpliceText(textId, 0, 0, "Hello World");
        doc.SpliceText(textId, 5, 6, ""); // delete " World"
        Assert.Equal("Hello", doc.GetText(textId));
    }

    [Fact]
    public void Text_ConcurrentEdits_MergeWithoutConflict()
    {
        using var origin = new Document();
        var textId = origin.PutObject(null, "note", "text");
        origin.SpliceText(textId, 0, 0, "Hello");
        var snap = origin.Save();

        using var peerA = Document.Load(snap);
        using var peerB = Document.Load(snap);

        var aTextId = GetNestedObjId(peerA, null, "note");
        var bTextId = GetNestedObjId(peerB, null, "note");

        peerA.SpliceText(aTextId, 5, 0, " from A");
        peerB.SpliceText(bTextId, 5, 0, " from B");

        peerA.Merge(peerB);

        // Text merges character-by-character — result contains both insertions
        var merged = peerA.GetText(aTextId);
        Assert.Contains("Hello", merged);
        // Both additions should be present
        Assert.Contains("from A", merged);
        Assert.Contains("from B", merged);
    }

    // ─── Fork ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fork_ProducesIdenticalDocument()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"k":"v","n":42}""");
        using var fork = doc.Fork();

        Assert.Equal(doc.GetHeads(), fork.GetHeads());
        Assert.Equal("\"v\"", fork.Get(null, "k"));
    }

    [Fact]
    public void Fork_MutationsDoNotAffectOriginal()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"shared":"yes"}""");
        using var fork = doc.Fork();

        fork.Put(null, "forked_only", "\"unique\"");
        Assert.Throws<AutomergeNativeException>(() => doc.Get(null, "forked_only"));
    }

    [Fact]
    public void Fork_CanMergeBack()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"base":1}""");
        using var fork = doc.Fork();
        fork.Put(null, "extra", "\"2\"");
        doc.Merge(fork);
        Assert.Equal("\"2\"", doc.Get(null, "extra"));
    }

    // ─── Incremental save ─────────────────────────────────────────────────────

    [Fact]
    public void SaveIncremental_ReturnsNonEmptyAfterChange()
    {
        using var doc = new Document();
        var initial = doc.SaveIncremental(); // first call may be empty or not
        doc.Put(null, "x", "\"new\"");
        var delta = doc.SaveIncremental();
        Assert.NotEmpty(delta);
    }

    [Fact]
    public void SaveIncremental_LoadedViaApplyChanges_ReconstitutesDoc()
    {
        using var doc = new Document();
        doc.Put(null, "a", "\"1\"");
        var snap1 = doc.Save();
        doc.Put(null, "b", "\"2\"");
        var delta = doc.SaveIncremental();

        using var loaded = Document.Load(snap1);
        loaded.ApplyChanges(delta);
        Assert.Equal("\"1\"", loaded.Get(null, "a"));
        Assert.Equal("\"2\"", loaded.Get(null, "b"));
    }

    // ─── Commit with metadata ─────────────────────────────────────────────────

    [Fact]
    public void Commit_WithMessage_DoesNotThrow()
    {
        using var doc = new Document();
        doc.Put(null, "x", "1");
        var ex = Record.Exception(() => doc.Commit("initial commit"));
        Assert.Null(ex);
    }

    [Fact]
    public void Commit_WithTimestamp_DoesNotThrow()
    {
        using var doc = new Document();
        doc.Put(null, "x", "1");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ex = Record.Exception(() => doc.Commit("timestamped", ts));
        Assert.Null(ex);
    }

    // ─── Conflict detection ───────────────────────────────────────────────────

    [Fact]
    public void GetAllJson_NoConflict_ReturnsSingleEntry()
    {
        using var doc = new Document();
        doc.Put(null, "k", "\"v\"");
        var allJson = doc.GetAllJson(null, "k");
        var arr = JsonSerializer.Deserialize<JsonElement[]>(allJson);
        Assert.NotNull(arr);
        Assert.Single(arr);
    }

    [Fact]
    public void GetAllJson_ConcurrentConflict_ReturnsBothValues()
    {
        using var origin = new Document();
        origin.Put(null, "field", "\"base\"");
        var snap = origin.Save();

        using var peerA = Document.Load(snap);
        using var peerB = Document.Load(snap);

        peerA.Put(null, "field", "\"from_a\"");
        peerB.Put(null, "field", "\"from_b\"");
        peerA.Merge(peerB);

        var allJson = peerA.GetAllJson(null, "field");
        var arr = JsonSerializer.Deserialize<JsonElement[]>(allJson);
        Assert.NotNull(arr);
        // After concurrent writes there should be 2 conflicting values
        Assert.Equal(2, arr!.Length);
        var vals = arr.Select(e => e.GetString()).ToArray();
        Assert.Contains("from_a", vals);
        Assert.Contains("from_b", vals);
    }

    // ─── Diff / patches ───────────────────────────────────────────────────────

    [Fact]
    public void DiffIncremental_AfterPut_ReturnsPutMapPatch()
    {
        using var doc = new Document();
        _ = doc.DiffIncremental(); // baseline — consume any initial state
        doc.Put(null, "title", "\"Dune\"");
        var diff = doc.DiffIncremental();
        var patches = JsonSerializer.Deserialize<JsonElement[]>(diff);
        Assert.NotNull(patches);
        Assert.NotEmpty(patches!);
        var any = patches.Any(p =>
            p.TryGetProperty("action", out var a) &&
            a.TryGetProperty("op", out var op) &&
            op.GetString() == "put_map");
        Assert.True(any, $"Expected a 'put_map' patch, got: {diff}");
    }

    [Fact]
    public void DiffIncremental_SecondCall_ReturnsEmptyIfNoChanges()
    {
        using var doc = new Document();
        doc.Put(null, "x", "1");
        _ = doc.DiffIncremental(); // consume
        var diff2 = doc.DiffIncremental();
        var patches = JsonSerializer.Deserialize<JsonElement[]>(diff2);
        Assert.NotNull(patches);
        Assert.Empty(patches!);
    }

    [Fact]
    public void DiffIncremental_Delete_ReturnsDeleteMapPatch()
    {
        using var doc = new Document();
        doc.Put(null, "bye", "\"see ya\"");
        _ = doc.DiffIncremental(); // baseline
        doc.Delete(null, "bye");
        var diff = doc.DiffIncremental();
        var patches = JsonSerializer.Deserialize<JsonElement[]>(diff);
        var any = patches!.Any(p =>
            p.TryGetProperty("action", out var a) &&
            a.TryGetProperty("op", out var op) &&
            op.GetString() == "delete_map");
        Assert.True(any, $"Expected a 'delete_map' patch, got: {diff}");
    }

    // ─── Save/Load round-trip with all new constructs ─────────────────────────

    [Fact]
    public void SaveLoad_PreservesNestedObjects()
    {
        using var doc = new Document();
        var bookId = doc.PutObject(null, "book", "map");
        doc.Put(bookId, "title", "\"Foundation\"");
        doc.Put(bookId, "year", "1951");

        var bytes = doc.Save();
        using var loaded = Document.Load(bytes);

        var loadedBookId = GetNestedObjId(loaded, null, "book");
        Assert.Equal("\"Foundation\"", loaded.Get(loadedBookId, "title"));
        Assert.Equal("1951", loaded.Get(loadedBookId, "year"));
    }

    [Fact]
    public void SaveLoad_PreservesList()
    {
        using var doc = new Document();
        var listId = doc.PutObject(null, "tags", "list");
        doc.Insert(listId, 0, "\"fiction\"");
        doc.Insert(listId, 1, "\"scifi\"");

        var bytes = doc.Save();
        using var loaded = Document.Load(bytes);
        var loadedListId = GetNestedObjId(loaded, null, "tags");
        Assert.Equal(2, loaded.GetLength(loadedListId));
        Assert.Equal("\"fiction\"", loaded.GetIdx(loadedListId, 0));
    }

    [Fact]
    public void SaveLoad_PreservesText()
    {
        using var doc = new Document();
        var textId = doc.PutObject(null, "note", "text");
        doc.SpliceText(textId, 0, 0, "Hello, World");

        var bytes = doc.Save();
        using var loaded = Document.Load(bytes);
        var loadedTextId = GetNestedObjId(loaded, null, "note");
        Assert.Equal("Hello, World", loaded.GetText(loadedTextId));
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the "_obj_id" from a nested object reference returned by Get().
    /// </summary>
    private static string GetNestedObjId(Document doc, string? parentObjId, string key)
    {
        var raw = doc.Get(parentObjId, key);
        var obj = JsonSerializer.Deserialize<JsonElement>(raw);
        return obj.GetProperty("_obj_id").GetString()!;
    }
}
