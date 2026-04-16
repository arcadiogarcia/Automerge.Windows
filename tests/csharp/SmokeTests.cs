// SmokeTests.cs — Live integration tests against wss://sync.automerge.org.
//
// These tests require a real network connection and hit the public Automerge
// community sync server.  They are skipped unless the environment variable
// AUTOMERGE_SMOKE_TESTS=1 is set, so they never block the CI suite.
//
// Run manually with:
//   $env:AUTOMERGE_SMOKE_TESTS=1
//   dotnet test --filter "Category=Smoke" tests\csharp\AutomergeTests.csproj
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Automerge.Windows.Tests
{
    /// <summary>
    /// Guard attribute: skips the test unless AUTOMERGE_SMOKE_TESTS=1.
    /// </summary>
    public sealed class SmokeFactAttribute : FactAttribute
    {
        public SmokeFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("AUTOMERGE_SMOKE_TESTS") != "1")
                Skip = "Set AUTOMERGE_SMOKE_TESTS=1 to run live network tests.";
        }
    }

    [Trait("Category", "Smoke")]
    public class SmokeTests
    {
        private const string ServerUrl = "wss://sync.automerge.org";

        // ─── Helpers ─────────────────────────────────────────────────────────

        // Generate a proper base58check doc ID required by the automerge-repo protocol.
        private static string NewDocId() => RepoSyncSession.NewDocumentId();

        private static string? TryGet(Document doc, string key)
        {
            try { return doc.Get("", key); }
            catch { return null; }
        }

        // ─── Tests ───────────────────────────────────────────────────────────

        /// <summary>
        /// Client A pushes a key/value and stays connected while Client B
        /// connects and pulls the data through the server relay.
        /// Both sessions overlap so the server can route changes between them.
        /// </summary>
        [SmokeFact]
        public async Task SyncOnce_ClientBReceivesClientAsData()
        {
            string docId = NewDocId();

            using var docA  = new Document();
            using var syncA = new SyncState();
            using var docB  = new Document();
            using var syncB = new SyncState();

            docA.Put("", "keyA", "\"valueA\"");

            // Start A's session first, then B's after a brief delay so that A
            // has time to push its changes before B sends its initial request.
            var taskA = RepoSyncSession.SyncOnceAsync(ServerUrl, docId, docA, syncA,
                stabilityTimeout: TimeSpan.FromSeconds(6));

            await Task.Delay(600); // let A's initial push reach the server

            var taskB = RepoSyncSession.SyncOnceAsync(ServerUrl, docId, docB, syncB,
                stabilityTimeout: TimeSpan.FromSeconds(6));

            await Task.WhenAll(taskA, taskB);

            // Client B should have received A's key via the server relay.
            Assert.Equal("\"valueA\"", TryGet(docB, "keyA"));
        }

        /// <summary>
        /// A continuous RunAsync loop receives a change pushed by a concurrent
        /// SyncOnceAsync client via the server relay.
        /// </summary>
        [SmokeFact]
        public async Task RunAsync_ReceivesRemoteChange()
        {
            string docId = NewDocId();
            const string expectedVal = "\"live_value\"";

            using var docRecv  = new Document();
            using var syncRecv = new SyncState();
            using var docSend  = new Document();
            using var syncSend = new SyncState();

            docSend.Put("", "live_key", expectedVal);

            // Start receiver loop first so it is already connected when the
            // sender pushes.
            await using var session = new RepoSyncSession(ServerUrl, docId);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var loopTask = session.RunAsync(docRecv, syncRecv, cts.Token);

            // Allow the receiver to connect, handshake, and send its initial sync.
            await Task.Delay(1500).ConfigureAwait(false);

            // Sender connects concurrently and pushes the change.
            await RepoSyncSession.SyncOnceAsync(ServerUrl, docId, docSend, syncSend,
                stabilityTimeout: TimeSpan.FromSeconds(6));

            // Poll up to 10 s for the receiver to apply the remote change.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (TryGet(docRecv, "live_key") == expectedVal) break;
                await Task.Delay(400).ConfigureAwait(false);
            }

            cts.Cancel();
            try { await loopTask; } catch (OperationCanceledException) { /* expected */ }

            Assert.Equal(expectedVal, TryGet(docRecv, "live_key"));
        }
    }
}
