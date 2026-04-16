using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests
{
    /// <summary>
    /// Tests for <see cref="RepoSyncSession"/> using minimal in-process WebSocket
    /// servers that speak the automerge-repo v1 protocol.  No external network required.
    /// </summary>
    public class RepoSyncSessionTests
    {
        // ─── Fake server infrastructure ───────────────────────────────────────

        private static int FreeTcpPort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>
        /// Starts a single-connection WebSocket server on a free port.
        /// Returns (ws:// URL, background server task).
        /// </summary>
        private static (string url, Task task) StartServer(
            Func<WebSocket, CancellationToken, Task> handler,
            CancellationToken ct)
        {
            var port = FreeTcpPort();
            var url  = $"ws://localhost:{port}/";
            var hl   = new HttpListener();
            hl.Prefixes.Add($"http://localhost:{port}/");
            hl.Start();

            var task = Task.Run(async () =>
            {
                try
                {
                    var ctx   = await hl.GetContextAsync().WaitAsync(ct);
                    var wsCtx = await ctx.AcceptWebSocketAsync(null!);
                    await handler(wsCtx.WebSocket, ct);
                }
                finally { hl.Stop(); }
            }, ct);

            return (url, task);
        }

        // ─── CBOR I/O helpers ─────────────────────────────────────────────────

        private static byte[] CborMap(Action<CborWriter> fill)
        {
            var w = new CborWriter(CborConformanceMode.Lax);
            w.WriteStartMap(null);
            fill(w);
            w.WriteEndMap();
            return w.Encode();
        }

        private static (string type, Dictionary<string, object> fields) Decode(byte[] data)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                var r = new CborReader(data, CborConformanceMode.Lax);
                int? count = r.ReadStartMap();
                int rem = count ?? int.MaxValue;
                while (rem-- > 0
                    && r.PeekState() != CborReaderState.EndMap
                    && r.PeekState() != CborReaderState.Finished)
                {
                    if (r.PeekState() != CborReaderState.TextString) break;
                    var key = r.ReadTextString();
                    switch (r.PeekState())
                    {
                        case CborReaderState.TextString: fields[key] = r.ReadTextString(); break;
                        case CborReaderState.ByteString: fields[key] = r.ReadByteString();  break;
                        default: r.SkipValue(); break;
                    }
                }
            }
            catch { }
            fields.TryGetValue("type", out var t);
            return (t as string ?? "", fields);
        }

        private static async Task<(string, Dictionary<string, object>)> RecvAsync(
            WebSocket ws, CancellationToken ct)
        {
            var buf = new byte[128 * 1024];
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct); }
            catch (WebSocketException)         { return ("_close", []); }
            catch (OperationCanceledException) { return ("_timeout", []); }
            if (r.MessageType == WebSocketMessageType.Close) return ("_close", []);
            return Decode(buf[..r.Count]);
        }

        private static Task WsSendAsync(WebSocket ws, byte[] data, CancellationToken ct) =>
            ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, ct);

        private static async Task SafeCloseAsync(WebSocket ws)
        {
            try
            {
                switch (ws.State)
                {
                    case WebSocketState.Open:
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "",
                                            CancellationToken.None);
                        break;
                    case WebSocketState.CloseReceived:
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "",
                                                  CancellationToken.None);
                        break;
                }
            }
            catch { }
        }

        private static byte[] PeerMsg(string serverId, string clientId) =>
            CborMap(w => {
                w.WriteTextString("type");     w.WriteTextString("peer");
                w.WriteTextString("senderId"); w.WriteTextString(serverId);
                w.WriteTextString("targetId"); w.WriteTextString(clientId);
                w.WriteTextString("peerMetadata"); w.WriteStartMap(null); w.WriteEndMap();
                w.WriteTextString("selectedProtocolVersion"); w.WriteTextString("1");
            });

        private static byte[] SyncMsg(string from, string to, string docId, byte[] data) =>
            CborMap(w => {
                w.WriteTextString("type");       w.WriteTextString("sync");
                w.WriteTextString("senderId");   w.WriteTextString(from);
                w.WriteTextString("targetId");   w.WriteTextString(to);
                w.WriteTextString("documentId"); w.WriteTextString(docId);
                w.WriteTextString("data");       w.WriteByteString(data);
            });

        /// <summary>
        /// Runs a complete automerge sync protocol exchange with the connected peer.
        /// <paramref name="serverDoc"/> is updated in-place.
        /// </summary>
        private static async Task RunSyncProtocolAsync(
            WebSocket ws, Document serverDoc, SyncState serverSync,
            string serverId, string docId, CancellationToken ct)
        {
            const int maxRounds = 20;
            for (int i = 0; i < maxRounds; i++)
            {
                // Try to receive from client (500 ms timeout = stability window).
                string type;
                Dictionary<string, object> df;
                try
                {
                    using var rcvCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    rcvCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                    (type, df) = await RecvAsync(ws, rcvCts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { break; }

                if (type is "_close" or "_timeout" || type is not ("sync" or "request")) break;

                serverSync.ReceiveSyncMessage(serverDoc, (byte[])df["data"]);

                // Reconstruct the client ID from the targetId we'll send to.
                var clientId = df.TryGetValue("senderId", out var s) ? (string)s : "unknown";

                var response = serverSync.GenerateSyncMessage(serverDoc);
                if (response.Length > 0)
                    await WsSendAsync(ws, SyncMsg(serverId, clientId, docId, response), ct);
                else
                    break; // server has nothing more to add — sync complete
            }
        }

        private const string DocId = "test-doc-12345";

        // ─── Tests ────────────────────────────────────────────────────────────

        [Fact]
        public async Task SyncOnce_SendsJoinAndHandshakes()
        {
            string? receivedType     = null;
            string? receivedSenderId = null;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (type, fields) = await RecvAsync(ws, ct); // receive join
                receivedType     = type;
                receivedSenderId = fields.TryGetValue("senderId", out var s) ? (string)s : null;

                await WsSendAsync(ws, PeerMsg("server", receivedSenderId!), ct);

                // Drain the client's sync messages.
                using var serverDoc  = new Document();
                using var serverSync = new SyncState();
                await RunSyncProtocolAsync(ws, serverDoc, serverSync, "server", DocId, ct);
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();
            await RepoSyncSession.SyncOnceAsync(url, DocId, doc, sync,
                stabilityTimeout: TimeSpan.FromMilliseconds(300), cancellationToken: cts.Token);

            await serverTask;
            Assert.Equal("join", receivedType);
            Assert.NotNull(receivedSenderId);
        }

        [Fact]
        public async Task SyncOnce_AppliesIncomingChanges()
        {
            // The server has a document with data; the client should end up with it.
            using var serverDoc  = new Document();
            serverDoc.Put(null, "hello", "\"world\"");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (_, f)      = await RecvAsync(ws, ct); // join
                var clientId    = (string)f["senderId"];
                await WsSendAsync(ws, PeerMsg("server", clientId), ct);

                // Run the full sync protocol — client starts empty, server sends changes.
                using var serverSync = new SyncState();
                await RunSyncProtocolAsync(ws, serverDoc, serverSync, "server", DocId, ct);
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();
            await RepoSyncSession.SyncOnceAsync(url, DocId, doc, sync,
                stabilityTimeout: TimeSpan.FromMilliseconds(300), cancellationToken: cts.Token);

            await serverTask;
            Assert.Equal("\"world\"", doc.Get(null, "hello"));
        }

        [Fact]
        public async Task SyncOnce_PushesLocalChanges()
        {
            // Client has local changes; after sync the server document should have them.
            using var serverDoc  = new Document();
            using var serverSync = new SyncState();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (_, f)  = await RecvAsync(ws, ct); // join
                var clientId = (string)f["senderId"];
                await WsSendAsync(ws, PeerMsg("server", clientId), ct);

                await RunSyncProtocolAsync(ws, serverDoc, serverSync, "server", DocId, ct);
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();
            doc.Put(null, "pushed", "\"yes\"");

            await RepoSyncSession.SyncOnceAsync(url, DocId, doc, sync,
                stabilityTimeout: TimeSpan.FromMilliseconds(300), cancellationToken: cts.Token);

            await serverTask;
            // The server document should now have the pushed key.
            Assert.Equal("\"yes\"", serverDoc.Get(null, "pushed"));
        }

        [Fact]
        public async Task SyncOnce_TwoPeersViaServerDoc_Converge()
        {
            // A server document acts as a relay.  Alice syncs first (server gets Alice's
            // changes), then Bob syncs (Bob gets Alice's changes, server gets Bob's changes),
            // then Alice syncs once more (Alice gets Bob's changes).

            using var serverDoc = new Document();

            using var aliceDoc  = new Document();
            using var bobDoc    = new Document();
            aliceDoc.Put(null, "alice", "\"A\"");
            bobDoc.Put(null, "bob",   "\"B\"");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            // Alice syncs to server (server now has alice: A)
            {
                using var serverSync = new SyncState();
                var (url, st) = StartServer(async (ws, ct) =>
                {
                    var (_, f) = await RecvAsync(ws, ct);
                    await WsSendAsync(ws, PeerMsg("srv", (string)f["senderId"]), ct);
                    await RunSyncProtocolAsync(ws, serverDoc, serverSync, "srv", DocId, ct);
                    await SafeCloseAsync(ws);
                }, cts.Token);

                using var syncState = new SyncState();
                await RepoSyncSession.SyncOnceAsync(url, DocId, aliceDoc, syncState,
                    TimeSpan.FromMilliseconds(300), cts.Token);
                await st;
            }

            // Bob syncs to server (Bob gets alice: A, server gets bob: B)
            {
                using var serverSync = new SyncState();
                var (url, st) = StartServer(async (ws, ct) =>
                {
                    var (_, f) = await RecvAsync(ws, ct);
                    await WsSendAsync(ws, PeerMsg("srv", (string)f["senderId"]), ct);
                    await RunSyncProtocolAsync(ws, serverDoc, serverSync, "srv", DocId, ct);
                    await SafeCloseAsync(ws);
                }, cts.Token);

                using var syncState = new SyncState();
                await RepoSyncSession.SyncOnceAsync(url, DocId, bobDoc, syncState,
                    TimeSpan.FromMilliseconds(300), cts.Token);
                await st;
            }

            // Alice syncs again (Alice gets bob: B)
            {
                using var serverSync = new SyncState();
                var (url, st) = StartServer(async (ws, ct) =>
                {
                    var (_, f) = await RecvAsync(ws, ct);
                    await WsSendAsync(ws, PeerMsg("srv", (string)f["senderId"]), ct);
                    await RunSyncProtocolAsync(ws, serverDoc, serverSync, "srv", DocId, ct);
                    await SafeCloseAsync(ws);
                }, cts.Token);

                using var syncState = new SyncState();
                await RepoSyncSession.SyncOnceAsync(url, DocId, aliceDoc, syncState,
                    TimeSpan.FromMilliseconds(300), cts.Token);
                await st;
            }

            // Both Alice and Bob should now have both keys.
            Assert.Equal("\"A\"", aliceDoc.Get(null, "alice"));
            Assert.Equal("\"B\"", aliceDoc.Get(null, "bob"));
            Assert.Equal("\"A\"", bobDoc.Get(null, "alice"));
            Assert.Equal("\"B\"", bobDoc.Get(null, "bob"));
        }

        [Fact]
        public async Task RunAsync_CancellationToken_StopsLoop()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (_, f) = await RecvAsync(ws, ct);
                await WsSendAsync(ws, PeerMsg("srv", (string)f["senderId"]), ct);
                using var serverDoc  = new Document();
                using var serverSync = new SyncState();
                await RunSyncProtocolAsync(ws, serverDoc, serverSync, "srv", DocId, ct);
                // Stay open until client disconnects.
                try { await RecvAsync(ws, ct); } catch { }
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();
            await using var session = new RepoSyncSession(url, DocId);

            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var runTask = session.RunAsync(doc, sync, runCts.Token);

            await Task.Delay(200, cts.Token);
            runCts.Cancel();

            await Task.WhenAny(runTask, Task.Delay(3000));
            Assert.True(runTask.IsCompleted || runTask.IsCanceled);

            cts.Cancel();
            try { await serverTask; } catch { }
        }

        [Fact]
        public async Task SyncOnce_ServerError_Throws()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (_, f) = await RecvAsync(ws, ct);
                var error = CborMap(w => {
                    w.WriteTextString("type");     w.WriteTextString("error");
                    w.WriteTextString("senderId"); w.WriteTextString("server");
                    w.WriteTextString("targetId"); w.WriteTextString((string)f["senderId"]);
                    w.WriteTextString("message");  w.WriteTextString("access denied");
                });
                await WsSendAsync(ws, error, ct);
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();
            var ex = await Assert.ThrowsAsync<AutomergeNativeException>(() =>
                RepoSyncSession.SyncOnceAsync(url, DocId, doc, sync,
                    stabilityTimeout: TimeSpan.FromMilliseconds(200),
                    cancellationToken: cts.Token));

            Assert.Contains("access denied", ex.Message);
            try { await serverTask; } catch { }
        }

        [Fact]
        public async Task SyncOnce_OnRemoteChange_FiresWhenRemoteDataArrives()
        {
            // The server has data; the client starts empty.  OnRemoteChange should
            // fire at least once when the server's sync frames are applied.
            using var serverDoc  = new Document();
            serverDoc.Put(null, "from_server", "\"hello\"");

            int callbackCount = 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (_, f)   = await RecvAsync(ws, ct);
                var clientId = (string)f["senderId"];
                await WsSendAsync(ws, PeerMsg("server", clientId), ct);

                using var serverSync = new SyncState();
                await RunSyncProtocolAsync(ws, serverDoc, serverSync, "server", DocId, ct);
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();

            await RepoSyncSession.SyncOnceAsync(url, DocId, doc, sync,
                stabilityTimeout: TimeSpan.FromMilliseconds(300),
                onRemoteChange: () => Interlocked.Increment(ref callbackCount),
                cancellationToken: cts.Token);

            await serverTask;
            Assert.True(callbackCount >= 1, $"Expected ≥1 OnRemoteChange calls but got {callbackCount}");
            Assert.Equal("\"hello\"", doc.Get(null, "from_server"));
        }

        [Fact]
        public async Task RunAsync_OnRemoteChange_FiresDuringLoop()
        {
            // Start a RunAsync loop, have the server push data, verify callback fires.
            using var serverDoc  = new Document();
            serverDoc.Put(null, "key", "\"value\"");

            int callbackCount = 0;
            var callbackFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (url, serverTask) = StartServer(async (ws, ct) =>
            {
                var (_, f) = await RecvAsync(ws, ct);
                await WsSendAsync(ws, PeerMsg("srv", (string)f["senderId"]), ct);
                using var serverSync = new SyncState();
                await RunSyncProtocolAsync(ws, serverDoc, serverSync, "srv", DocId, ct);
                // Stay open until client disconnects.
                try { await RecvAsync(ws, ct); } catch { }
                await SafeCloseAsync(ws);
            }, cts.Token);

            using var doc  = new Document();
            using var sync = new SyncState();
            await using var session = new RepoSyncSession(url, DocId);
            session.OnRemoteChange = () =>
            {
                Interlocked.Increment(ref callbackCount);
                callbackFired.TrySetResult();
            };

            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var runTask = session.RunAsync(doc, sync, runCts.Token);

            // Wait for the callback (or timeout).
            await Task.WhenAny(callbackFired.Task, Task.Delay(5000, cts.Token));

            runCts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
            cts.Cancel();
            try { await serverTask; } catch { }

            Assert.True(callbackCount >= 1, $"Expected ≥1 OnRemoteChange calls but got {callbackCount}");
        }
    }
}
