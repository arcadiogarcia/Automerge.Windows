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

    [Fact]
    public void SyncState_SaveLoad_RoundTrips()
    {
        using var ss1 = new SyncState();
        var bytes = ss1.Save();
        using var ss2 = SyncState.Load(bytes);
        Assert.NotNull(ss2);
    }
}
