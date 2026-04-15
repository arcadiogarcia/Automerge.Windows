// WinRTSyncTests.cs — Tests for the WinRT SyncState projection.

using System;
using Automerge.Windows;
using Windows.Storage.Streams;
using Xunit;

namespace AutomergeWinRTTests;

public class WinRTSyncTests
{
    // ─── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateSyncState_DoesNotThrow()
    {
        var state = new SyncState();
        Assert.NotNull(state);
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public void SyncState_SaveLoad_Roundtrip()
    {
        var state = new SyncState();
        var data = state.Save();
        var state2 = SyncState.Load(data);
        Assert.NotNull(state2);
    }

    // ─── Sync protocol ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSyncMessage_ReturnsBuffer()
    {
        var doc = new Document();
        var state = new SyncState();
        var msg = state.GenerateSyncMessage(doc);
        Assert.NotNull(msg);
    }

    [Fact]
    public void SyncTwoDocuments_Converges()
    {
        // docA has content; docB is empty; sync A→B
        var docA = new Document();
        docA.PutJsonRoot("""{"synced":1}""");

        var docB = new Document();

        var stateA = new SyncState();
        var stateB = new SyncState();

        // Exchange messages until neither side has anything new to say
        for (int round = 0; round < 5; round++)
        {
            var msgA = stateA.GenerateSyncMessage(docA);
            if (msgA.Length > 0)
                stateB.ReceiveSyncMessage(docB, msgA);

            var msgB = stateB.GenerateSyncMessage(docB);
            if (msgB.Length > 0)
                stateA.ReceiveSyncMessage(docA, msgB);
        }

        // docB should now have the content from docA
        Assert.Equal("1", docB.GetValue("""["synced"]"""));
    }

    // ─── Sync: shared history then diverge ───────────────────────────────────

    [Fact]
    public void SyncSharedHistoryThenDiverge_Converges()
    {
        var origin = new Document();
        origin.PutJsonRoot("""{"shared":1}""");
        var snap = origin.Save();

        var docA = Document.Load(snap);
        var docB = Document.Load(snap);
        docA.PutJsonRoot("""{"from_a":"a_only"}""");
        docB.PutJsonRoot("""{"from_b":"b_only"}""");

        var stateA = new SyncState();
        var stateB = new SyncState();
        for (int i = 0; i < 10; i++)
        {
            var mAB = stateA.GenerateSyncMessage(docA);
            if (mAB.Length > 0) stateB.ReceiveSyncMessage(docB, mAB);
            var mBA = stateB.GenerateSyncMessage(docB);
            if (mBA.Length > 0) stateA.ReceiveSyncMessage(docA, mBA);
        }

        Assert.Equal("\"a_only\"", docB.GetValue("""["from_a"]"""));
        Assert.Equal("\"b_only\"", docA.GetValue("""["from_b"]"""));
        Assert.Equal("1", docA.GetValue("""["shared"]"""));
        Assert.Equal("1", docB.GetValue("""["shared"]"""));
    }

    // ─── SyncState resume ─────────────────────────────────────────────────────

    [Fact]
    public void SyncState_ResumeAfterSaveLoad_Converges()
    {
        var docA = new Document();
        docA.PutJsonRoot("""{"msg":"hello"}""");
        var docB = new Document();

        var stateA = new SyncState();
        var stateB = new SyncState();

        // One message A→B then save/reload both states.
        var msg1 = stateA.GenerateSyncMessage(docA);
        if (msg1.Length > 0)
            stateB.ReceiveSyncMessage(docB, msg1);

        var stateA2 = SyncState.Load(stateA.Save());
        var stateB2 = SyncState.Load(stateB.Save());

        for (int i = 0; i < 10; i++)
        {
            var mAB = stateA2.GenerateSyncMessage(docA);
            if (mAB.Length > 0) stateB2.ReceiveSyncMessage(docB, mAB);
            var mBA = stateB2.GenerateSyncMessage(docB);
            if (mBA.Length > 0) stateA2.ReceiveSyncMessage(docA, mBA);
        }

        Assert.Equal("\"hello\"", docB.GetValue("""["msg"]"""));
    }

    // ─── Three peers ──────────────────────────────────────────────────────────

    [Fact]
    public void ThreePeers_AllConvergeToSameData()
    {
        var docA = new Document();
        docA.PutJsonRoot("""{"peer":"a"}""");
        var docB = new Document();
        docB.PutJsonRoot("""{"peer_b":"b"}""");
        var docC = new Document();
        docC.PutJsonRoot("""{"peer_c":"c"}""");

        static void SyncPair(Document d1, Document d2)
        {
            var s1 = new SyncState();
            var s2 = new SyncState();
            for (int i = 0; i < 10; i++)
            {
                var m1 = s1.GenerateSyncMessage(d1);
                if (m1.Length > 0) s2.ReceiveSyncMessage(d2, m1);
                var m2 = s2.GenerateSyncMessage(d2);
                if (m2.Length > 0) s1.ReceiveSyncMessage(d1, m2);
            }
        }

        SyncPair(docA, docB);
        SyncPair(docA, docC);
        SyncPair(docB, docC);
        SyncPair(docA, docB); // Final pass: B picks up C's data via A.

        Assert.Equal("\"a\"", docC.GetValue("""["peer"]"""));
        Assert.Equal("\"b\"", docA.GetValue("""["peer_b"]"""));
        Assert.Equal("\"c\"", docB.GetValue("""["peer_c"]"""));
    }
}
