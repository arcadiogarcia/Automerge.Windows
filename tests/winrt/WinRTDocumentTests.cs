// WinRTDocumentTests.cs — Tests for the WinRT Document projection.
//
// Path format for GetValue: JSON array syntax, e.g. ["key"] or [] for root.
// This matches exactly how the underlying C++ wrapper and Rust core work.

using System;
using System.Text.Json;
using Automerge.Windows;
using Windows.Storage.Streams;
using Xunit;

namespace AutomergeWinRTTests;

public class WinRTDocumentTests
{
    // ─── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDocument_DoesNotThrow()
    {
        var doc = new Document();
        Assert.NotNull(doc);
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public void Save_ReturnsNonEmptyBuffer()
    {
        var doc = new Document();
        var buf = doc.Save();
        Assert.True(buf.Length > 0);
    }

    [Fact]
    public void SaveLoad_RoundtripPreservesContent()
    {
        var doc1 = new Document();
        doc1.PutJsonRoot("""{"x":42}""");
        var buf = doc1.Save();

        var doc2 = Document.Load(buf);
        var val = doc2.GetValue("""["x"]""");
        Assert.Equal("42", val);
    }

    // ─── Heads ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetHeads_ReturnsBuffer()
    {
        var doc = new Document();
        var heads = doc.GetHeads();
        Assert.NotNull(heads);
    }

    // ─── Changes ──────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyChanges_GetChanges_RoundTrip()
    {
        var doc1 = new Document();
        doc1.PutJsonRoot("""{"val":1}""");

        // Get all changes (since genesis = empty heads)
        var emptyHeads = MakeEmptyBuffer();
        var changes = doc1.GetChanges(emptyHeads);

        var doc2 = new Document();
        doc2.ApplyChanges(changes);
        Assert.Equal("1", doc2.GetValue("""["val"]"""));
    }

    // ─── Merge ────────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_ContentFromOtherIsVisible()
    {
        // doc1 and doc2 start as empty then diverge
        var sharedBytes = new Document().Save();
        var doc1 = Document.Load(sharedBytes);
        var doc2 = Document.Load(sharedBytes);

        // Only doc2 writes; merge into doc1
        doc2.PutJsonRoot("""{"merged":true}""");
        doc1.Merge(doc2);

        // doc1 should now have content from doc2
        var val = doc1.GetValue("""["merged"]""");
        Assert.Equal("true", val);
    }

    // ─── Read / Write ─────────────────────────────────────────────────────────

    [Fact]
    public void PutJsonRoot_GetValue_String()
    {
        var doc = new Document();
        doc.PutJsonRoot("""{"name":"hello"}""");
        Assert.Equal("\"hello\"", doc.GetValue("""["name"]"""));
    }

    [Fact]
    public void PutJsonRoot_GetValue_Number()
    {
        var doc = new Document();
        doc.PutJsonRoot("""{"count":7}""");
        Assert.Equal("7", doc.GetValue("""["count"]"""));
    }

    [Fact]
    public void GetValue_Root_ReturnsJson()
    {
        var doc = new Document();
        doc.PutJsonRoot("""{"a":1}""");
        var root = doc.GetValue("[]");
        // Root serialisation should contain our key
        Assert.Contains("\"a\"", root);
    }

    // ─── CRDT: Concurrent writes ──────────────────────────────────────────────

    [Fact]
    public void ConcurrentWrites_SameKey_ConvergesAfterMerge()
    {
        var origin = new Document();
        origin.PutJsonRoot("""{"shared":0}""");
        var snap = origin.Save();

        var docA = Document.Load(snap);
        var docB = Document.Load(snap);
        docA.PutJsonRoot("""{"shared":"from_a"}""");
        docB.PutJsonRoot("""{"shared":"from_b"}""");

        docA.Merge(docB);
        docB.Merge(docA);

        var valA = docA.GetValue("""["shared"]""");
        var valB = docB.GetValue("""["shared"]""");
        Assert.Equal(valA, valB);
        Assert.True(valA == "\"from_a\"" || valA == "\"from_b\"",
            $"winner must be one of the inputs, got: {valA}");
    }

    // ─── API boundary ─────────────────────────────────────────────────────────

    [Fact]
    public void PutJsonRoot_NestedObject_Throws()
    {
        var doc = new Document();
        // Nested objects are not supported; WinRT surfaces the error as a COMException.
        Assert.ThrowsAny<Exception>(() =>
            doc.PutJsonRoot("""{"nested":{"inner":1}}"""));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static IBuffer MakeEmptyBuffer()
    {
        using var writer = new DataWriter();
        writer.WriteBytes(Array.Empty<byte>());
        return writer.DetachBuffer();
    }
}

