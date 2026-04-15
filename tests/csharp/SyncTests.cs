using System;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests;

/// <summary>Tests for the sync protocol convergence behavior.</summary>
public class SyncTests
{
    // ─── Sync state lifecycle ─────────────────────────────────────────────────

    [Fact]
    public void CreateAndDispose_DoesNotThrow()
    {
        using var ss = new SyncState();
        Assert.NotNull(ss);
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var ss = new SyncState();
        ss.Dispose();
        ss.Dispose();
    }

    // ─── Basic sync ───────────────────────────────────────────────────────────

    [Fact]
    public void SyncOneWay_DataReachesEmptyPeer()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"source":"alice"}""");

        using var docB = new Document();

        // Run sync
        AutomergeExtensions.SyncInMemory(docA, docB);

        var val = docB.GetValue("""["source"]""");
        Assert.Equal("\"alice\"", val);
    }

    [Fact]
    public void SyncBidirectional_BothSidesSeeAllData()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"by_a":"data_a"}""");

        using var docB = new Document();
        docB.PutJsonRoot("""{"by_b":"data_b"}""");

        AutomergeExtensions.SyncInMemory(docA, docB);

        Assert.Equal("\"data_a\"", docA.GetValue("""["by_a"]"""));
        Assert.Equal("\"data_b\"", docA.GetValue("""["by_b"]"""));
        Assert.Equal("\"data_a\"", docB.GetValue("""["by_a"]"""));
        Assert.Equal("\"data_b\"", docB.GetValue("""["by_b"]"""));
    }

    [Fact]
    public void SyncConvergesToSameHeads()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"a":1}""");

        using var docB = new Document();
        docB.PutJsonRoot("""{"b":2}""");

        AutomergeExtensions.SyncInMemory(docA, docB);

        var headsA = docA.GetHeads();
        var headsB = docB.GetHeads();
        Assert.Equal(headsA, headsB);
    }

    [Fact]
    public void SyncAlreadySynced_NoMessages()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"x":1}""");
        using var docB = new Document();

        // First sync — sends data
        AutomergeExtensions.SyncInMemory(docA, docB);

        // Second sync on a fresh pair of SyncStates — should find no new data
        using var ssA2 = new SyncState();
        using var ssB2 = new SyncState();

        // After full round, no more messages needed
        using var ssA3 = new SyncState();
        using var ssB3 = new SyncState();

        // Generate a message — there will be one "have" ping, then nothing
        var msg1 = ssA3.GenerateSyncMessage(docA);
        ssB3.ReceiveSyncMessage(docB, msg1);  // B sees it has everything
        var msg2 = ssB3.GenerateSyncMessage(docB);
        if (msg2.Length > 0)
            ssA3.ReceiveSyncMessage(docA, msg2);
        var msg3 = ssA3.GenerateSyncMessage(docA);
        Assert.Equal(0, msg3.Length); // A is done
    }

    // ─── Multiple rounds ──────────────────────────────────────────────────────

    [Fact]
    public void SyncManualRounds_ConvergesWithin10Rounds()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"written_by":"alice"}""");
        using var docB = new Document();
        docB.PutJsonRoot("""{"written_by_b":"bob"}""");

        using var ssA = new SyncState();
        using var ssB = new SyncState();

        bool converged = false;
        for (int i = 0; i < 10; i++)
        {
            var msgAB = ssA.GenerateSyncMessage(docA);
            if (msgAB.Length > 0) ssB.ReceiveSyncMessage(docB, msgAB);

            var msgBA = ssB.GenerateSyncMessage(docB);
            if (msgBA.Length > 0) ssA.ReceiveSyncMessage(docA, msgBA);

            var ha = docA.GetHeads();
            var hb = docB.GetHeads();
            if (ha.Length > 0 && ((ReadOnlySpan<byte>)ha).SequenceEqual(hb))
            {
                converged = true;
                break;
            }
        }

        Assert.True(converged, "Sync did not converge within 10 rounds");
    }

    // ─── Sync state persistence ───────────────────────────────────────────────

    // ─── Sync state persistence ───────────────────────────────────────────────

    [Fact]
    public void SyncState_SaveLoad_RoundTrips()
    {
        using var ss1 = new SyncState();
        var bytes = ss1.Save();
        using var ss2 = SyncState.Load(bytes);
        Assert.NotNull(ss2);
    }

    // ─── CRDT: Concurrent writes ──────────────────────────────────────────────

    [Fact]
    public void Sync_ConcurrentSameKey_ConvergesToSameValue()
    {
        using var origin = new Document();
        origin.PutJsonRoot("""{"counter":0}""");
        var snap = origin.Save();

        using var docA = Document.Load(snap);
        using var docB = Document.Load(snap);
        docA.PutJsonRoot("""{"counter":100}""");
        docB.PutJsonRoot("""{"counter":200}""");

        AutomergeExtensions.SyncInMemory(docA, docB);

        var valA = docA.GetValue("""["counter"]""");
        var valB = docB.GetValue("""["counter"]""");
        Assert.Equal(valA, valB);
        Assert.True(valA == "100" || valA == "200",
            $"winner must be one of the inputs, got: {valA}");
        Assert.Equal(docA.GetHeads(), docB.GetHeads());
    }

    // ─── Sync: shared history then diverge ────────────────────────────────────

    [Fact]
    public void Sync_SharedHistoryThenDiverge_Converges()
    {
        using var origin = new Document();
        origin.PutJsonRoot("""{"shared":1}""");
        var snap = origin.Save();

        using var docA = Document.Load(snap);
        using var docB = Document.Load(snap);
        docA.PutJsonRoot("""{"from_a":"only_in_a"}""");
        docB.PutJsonRoot("""{"from_b":"only_in_b"}""");

        AutomergeExtensions.SyncInMemory(docA, docB);

        Assert.Equal("\"only_in_b\"", docA.GetValue("""["from_b"]"""));
        Assert.Equal("\"only_in_a\"", docB.GetValue("""["from_a"]"""));
        Assert.Equal("1", docA.GetValue("""["shared"]"""));
        Assert.Equal("1", docB.GetValue("""["shared"]"""));
        Assert.Equal(docA.GetHeads(), docB.GetHeads());
    }

    // ─── SyncState resume ─────────────────────────────────────────────────────

    [Fact]
    public void Sync_ResumeAfterSyncStateSaveLoad_Converges()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"first":"change"}""");
        using var docB = new Document();

        using var ssA = new SyncState();
        using var ssB = new SyncState();

        // One message A\u2192B then persist both states.
        var msg1 = ssA.GenerateSyncMessage(docA);
        Assert.True(msg1.Length > 0, "first message must be non-empty");
        ssB.ReceiveSyncMessage(docB, msg1);

        using var ssA2 = SyncState.Load(ssA.Save());
        using var ssB2 = SyncState.Load(ssB.Save());

        bool converged = false;
        for (int i = 0; i < 10; i++)
        {
            var mAB = ssA2.GenerateSyncMessage(docA);
            if (mAB.Length > 0) ssB2.ReceiveSyncMessage(docB, mAB);

            var mBA = ssB2.GenerateSyncMessage(docB);
            if (mBA.Length > 0) ssA2.ReceiveSyncMessage(docA, mBA);

            var ha = docA.GetHeads();
            var hb = docB.GetHeads();
            if (ha.Length > 0 && ((ReadOnlySpan<byte>)ha).SequenceEqual(hb))
            {
                converged = true;
                break;
            }
        }

        Assert.True(converged, "resumed sync must converge");
        Assert.Equal("\"change\"", docB.GetValue("""["first"]"""));
        Assert.Equal(docA.GetHeads(), docB.GetHeads());
    }

    // ─── Three peers ──────────────────────────────────────────────────────────

    [Fact]
    public void ThreePeers_AllHaveIdenticalHeads()
    {
        using var docA = new Document();
        docA.PutJsonRoot("""{"peer":"a"}""");
        using var docB = new Document();
        docB.PutJsonRoot("""{"peer_b":"b"}""");
        using var docC = new Document();
        docC.PutJsonRoot("""{"peer_c":"c"}""");

        // A\u2194B, A\u2194C, B\u2194C, then final A\u2194B so B picks up C\u2019s data via A.
        AutomergeExtensions.SyncInMemory(docA, docB);
        AutomergeExtensions.SyncInMemory(docA, docC);
        AutomergeExtensions.SyncInMemory(docB, docC);
        AutomergeExtensions.SyncInMemory(docA, docB);

        Assert.Equal(docA.GetHeads(), docB.GetHeads());
        Assert.Equal(docB.GetHeads(), docC.GetHeads());
        Assert.Equal("\"a\"", docC.GetValue("""["peer"]"""));
        Assert.Equal("\"b\"", docA.GetValue("""["peer_b"]"""));
        Assert.Equal("\"c\"", docB.GetValue("""["peer_c"]"""));
    }

    // ─── Stress ───────────────────────────────────────────────────────────────

    [Fact]
    public void Stress_FiftyChanges_SyncConverges()
    {
        using var docA = new Document();
        for (int i = 0; i < 50; i++)
            docA.PutJsonRoot($"{{\"key_{i}\": {i}}}");

        using var docB = new Document();
        AutomergeExtensions.SyncInMemory(docA, docB, maxRounds: 50);

        Assert.Equal("0",  docB.GetValue("""["key_0"]"""));
        Assert.Equal("49", docB.GetValue("""["key_49"]"""));
        Assert.Equal(docA.GetHeads(), docB.GetHeads());
    }
}
