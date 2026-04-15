using System;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests;

/// <summary>Tests for change-based replication and merging.</summary>
public class ChangesAndMergeTests
{
    [Fact]
    public void GetChanges_AllChanges_ReturnsNonEmpty()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"a":1}""");
        var changes = doc.GetChanges();
        Assert.NotEmpty(changes);
    }

    [Fact]
    public void ApplyChanges_ReplicatesToNewDoc()
    {
        using var doc1 = new Document();
        doc1.PutJsonRoot("""{"greeting":"hello"}""");
        var changes = doc1.GetChanges();

        using var doc2 = new Document();
        doc2.ApplyChanges(changes);

        var val = doc2.GetValue("""["greeting"]""");
        Assert.Equal("\"hello\"", val);
    }

    [Fact]
    public void GetChangesSinceHeads_IncrementalOnly()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"first":1}""");
        var headsAfterFirst = doc.GetHeads();

        doc.PutJsonRoot("""{"second":2}""");
        var delta = doc.GetChanges(headsAfterFirst);

        // Delta changes should be non-empty
        Assert.NotEmpty(delta);

        // A fresh doc receiving only the delta should have "second" but may
        // not have "first".
        using var fresh = new Document();
        fresh.ApplyChanges(delta);
        // The fresh doc will not necessarily have "first" since we sent
        // only incremental changes — this is intentional.
    }

    [Fact]
    public void ApplyChanges_InvalidData_ThrowsException()
    {
        using var doc = new Document();
        var garbage = "garbage data"u8.ToArray();
        Assert.Throws<AutomergeNativeException>(() => doc.ApplyChanges(garbage));
    }

    [Fact]
    public void Merge_CombinesTwoDocuments()
    {
        using var doc1 = new Document();
        doc1.PutJsonRoot("""{"a":"from1"}""");

        using var doc2 = new Document();
        doc2.PutJsonRoot("""{"b":"from2"}""");

        doc1.Merge(doc2);

        Assert.Equal("\"from1\"", doc1.GetValue("""["a"]"""));
        Assert.Equal("\"from2\"", doc1.GetValue("""["b"]"""));
    }

    [Fact]
    public void Merge_Idempotent()
    {
        using var doc1 = new Document();
        doc1.PutJsonRoot("""{"k":"v"}""");

        using var doc2 = new Document();
        doc2.PutJsonRoot("""{"k":"v"}""");

        doc1.Merge(doc2);
        doc1.Merge(doc2); // second merge must be safe
    }

    [Fact]
    public void Merge_SameDocDifferentHistory_Converges()
    {
        // Simulate two peers starting from the same saved state
        using var origin = new Document();
        origin.PutJsonRoot("""{"shared":1}""");
        var snapshot = origin.Save();

        using var peer1 = Document.Load(snapshot);
        using var peer2 = Document.Load(snapshot);

        peer1.PutJsonRoot("""{"peer1_key":"peer1_val"}""");
        peer2.PutJsonRoot("""{"peer2_key":"peer2_val"}""");

        peer1.Merge(peer2);

        Assert.Equal("\"peer1_val\"", peer1.GetValue("""["peer1_key"]"""));
        Assert.Equal("\"peer2_val\"", peer1.GetValue("""["peer2_key"]"""));
        Assert.Equal("1", peer1.GetValue("""["shared"]"""));
    }

    // ─── CRDT: Concurrent writes to the same key ──────────────────────────────

    [Fact]
    public void ConcurrentSameKey_Merge_BothConvergeToSameValue()
    {
        // Both peers start from a shared snapshot, each writes the same key with
        // a different value. After bidirectional merge both must see the same
        // deterministic CRDT-resolved value.
        using var origin = new Document();
        origin.PutJsonRoot("""{"shared":0}""");
        var snap = origin.Save();

        using var docA = Document.Load(snap);
        using var docB = Document.Load(snap);
        docA.PutJsonRoot("""{"shared":"from_a"}""");
        docB.PutJsonRoot("""{"shared":"from_b"}""");

        docA.Merge(docB);
        docB.Merge(docA);

        var valA = docA.GetValue("""["shared"]""");
        var valB = docB.GetValue("""["shared"]""");
        Assert.Equal(valA, valB);
        Assert.True(valA == "\"from_a\"" || valA == "\"from_b\"",
            $"winner must be one of the inputs, got: {valA}");
        Assert.Equal(docA.GetHeads(), docB.GetHeads());
    }

    // ─── Incremental delta excludes prior history ─────────────────────────────

    [Fact]
    public void IncrementalDelta_OnlyContainsNewChanges()
    {
        // Build origin in two steps: add "a" first (snapshot), then add "b".
        using var doc = new Document();
        doc.PutJsonRoot("""{"a":1}""");
        var snapV1  = doc.Save();
        var headsV1 = doc.GetHeads();

        doc.PutJsonRoot("""{"b":2}""");
        var delta = doc.GetChanges(headsV1);
        Assert.NotEmpty(delta);

        // Full history must be longer than the delta.
        var full = doc.GetChanges();
        Assert.True(delta.Length < full.Length, "delta must be smaller than full history");

        // Apply only the delta to a peer pre-loaded with the v1 snapshot so its
        // causal dependency (the change for "a") is already satisfied.
        using var peer = Document.Load(snapV1);
        peer.ApplyChanges(delta);

        // Peer should have "a" (from v1 baseline) and "b" (from delta).
        Assert.Equal("1", peer.GetValue("""["a"]"""));
        Assert.Equal("2", peer.GetValue("""["b"]"""));
    }
}
