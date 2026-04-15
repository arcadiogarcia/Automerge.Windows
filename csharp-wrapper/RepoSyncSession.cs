using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// Syncs a <see cref="Document"/> with an automerge-repo compatible WebSocket
    /// sync server — including the public community server at
    /// <c>wss://sync.automerge.org</c>.
    ///
    /// <para>
    /// All collaborating peers must share the same <b>document ID</b>: any stable
    /// non-empty string, typically a UUID generated once when the document is first
    /// created and stored alongside it.
    /// </para>
    ///
    /// <para><b>Wire protocol (automerge-repo v1):</b></para>
    /// <list type="number">
    ///   <item>Client → server: CBOR <c>join</c> frame containing the client's peer ID.</item>
    ///   <item>Server → client: CBOR <c>peer</c> frame containing the server's peer ID.</item>
    ///   <item>Both sides exchange CBOR <c>sync</c> frames whose <c>data</c> byte string
    ///         is the raw automerge sync protocol payload produced/consumed by
    ///         <see cref="SyncState"/>.</item>
    /// </list>
    ///
    /// <para><b>Basic usage (continuous sync):</b></para>
    /// <code><![CDATA[
    /// var docId  = "550e8400-e29b-41d4-a716-446655440000"; // stored with the doc
    /// using var doc    = new Document();
    /// using var sync   = new SyncState();
    /// await using var session = new RepoSyncSession("wss://sync.automerge.org", docId);
    ///
    /// using var cts = new CancellationTokenSource();
    /// _ = session.RunAsync(doc, sync, cts.Token);  // background loop
    ///
    /// // …make changes to doc…
    /// // …session picks them up within ~1 second…
    ///
    /// cts.Cancel(); // disconnect
    /// ]]></code>
    ///
    /// <para><b>One-shot sync:</b></para>
    /// <code><![CDATA[
    /// await RepoSyncSession.SyncOnceAsync(
    ///     "wss://sync.automerge.org", docId, doc, sync);
    /// ]]></code>
    /// </summary>
    public sealed class RepoSyncSession : IAsyncDisposable
    {
        private readonly string _serverUrl;
        private readonly string _documentId;
        private readonly string _peerId;
        private ClientWebSocket? _ws;
        private string? _remotePeerId;
        private bool _disposed;

        /// <summary>
        /// Create a session. Does not connect until <see cref="RunAsync"/> or
        /// <see cref="SyncOnceAsync"/> is called.
        /// </summary>
        /// <param name="serverUrl">
        ///   WebSocket URL of the sync server,
        ///   e.g. <c>"wss://sync.automerge.org"</c>.
        /// </param>
        /// <param name="documentId">
        ///   Unique, stable identifier for the document shared by all collaborating
        ///   peers.  Any non-empty string works; a UUID is recommended.
        /// </param>
        /// <param name="peerId">
        ///   Stable identifier for this device/process.
        ///   Defaults to a newly generated UUID (not persisted across restarts).
        ///   For reliable deduplication by the server, persist and reuse this value.
        /// </param>
        public RepoSyncSession(string serverUrl, string documentId, string? peerId = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(serverUrl);
            ArgumentException.ThrowIfNullOrEmpty(documentId);
            _serverUrl   = serverUrl;
            _documentId  = documentId;
            _peerId      = peerId ?? Guid.NewGuid().ToString();
        }

        // ─── Continuous sync ──────────────────────────────────────────────────

        /// <summary>
        /// Connect to the server, perform the handshake, then run the sync loop
        /// until <paramref name="cancellationToken"/> is cancelled or the server
        /// closes the connection.
        ///
        /// <para>
        /// All incoming <c>sync</c> frames are applied to
        /// <paramref name="doc"/> immediately.  Local changes made to
        /// <paramref name="doc"/> between received frames are pushed to the server
        /// on the next polling tick (≤ 1 s).
        /// </para>
        /// </summary>
        /// <param name="doc">The document to sync.</param>
        /// <param name="syncState">
        ///   Per-peer sync state.  Pass a fresh <see cref="SyncState"/> for a new
        ///   server relationship; reuse the same instance to avoid re-exchanging
        ///   history on reconnect.
        /// </param>
        /// <param name="cancellationToken">Token used to stop the loop.</param>
        public async Task RunAsync(
            Document doc,
            SyncState syncState,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(doc);
            ArgumentNullException.ThrowIfNull(syncState);

            await ConnectAndHandshakeAsync(cancellationToken);
            await PushAsync(doc, syncState, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? frame;
                try
                {
                    // 1-second poll so local changes are pushed even with no server traffic.
                    using var poll = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    poll.CancelAfter(TimeSpan.FromSeconds(1));
                    frame = await ReadFrameAsync(poll.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await PushAsync(doc, syncState, cancellationToken);
                    continue;
                }

                if (frame == null) break; // server closed

                var (type, fields) = ParseFrame(frame);
                if (type == "sync" && fields.TryGetValue("data", out var raw))
                {
                    syncState.ReceiveSyncMessage(doc, (byte[])raw);
                    await PushAsync(doc, syncState, cancellationToken);
                }
                else if (type == "error")
                {
                    var msg = fields.TryGetValue("message", out var m) ? (string)m : "unknown";
                    throw new AutomergeNativeException($"Sync server error: {msg}");
                }
            }
        }

        // ─── One-shot sync ────────────────────────────────────────────────────

        /// <summary>
        /// Connect to the server, sync the document to convergence, then
        /// disconnect.
        ///
        /// <para>
        /// Convergence is declared when no new sync frame arrives from the server
        /// within <paramref name="stabilityTimeout"/> (default: 2 s).  Increase this
        /// value on high-latency connections.
        /// </para>
        /// </summary>
        /// <param name="serverUrl">WebSocket URL, e.g. <c>"wss://sync.automerge.org"</c>.</param>
        /// <param name="documentId">Document identifier shared by all collaborators.</param>
        /// <param name="doc">The document to sync.</param>
        /// <param name="syncState">Sync state for this peer relationship.</param>
        /// <param name="stabilityTimeout">
        ///   Idle period that signals convergence.  Defaults to 2 seconds.
        /// </param>
        /// <param name="cancellationToken">Optional external cancellation.</param>
        public static async Task SyncOnceAsync(
            string serverUrl,
            string documentId,
            Document doc,
            SyncState syncState,
            TimeSpan? stabilityTimeout = null,
            CancellationToken cancellationToken = default)
        {
            var timeout = stabilityTimeout ?? TimeSpan.FromSeconds(2);

            await using var session = new RepoSyncSession(serverUrl, documentId);
            await session.ConnectAndHandshakeAsync(cancellationToken);
            await session.PushAsync(doc, syncState, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? frame;
                try
                {
                    using var stab = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    stab.CancelAfter(timeout);
                    frame = await session.ReadFrameAsync(stab.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break; // no message within timeout — synced
                }

                if (frame == null) break;

                var (type, fields) = ParseFrame(frame);
                if (type == "sync" && fields.TryGetValue("data", out var raw))
                {
                    syncState.ReceiveSyncMessage(doc, (byte[])raw);
                    await session.PushAsync(doc, syncState, cancellationToken);
                }
            }
        }

        // ─── IAsyncDisposable ─────────────────────────────────────────────────

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ws != null)
            {
                try
                {
                    switch (_ws.State)
                    {
                        case WebSocketState.Open:
                            // We initiate close and wait for the echo.
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                                 "disposing", CancellationToken.None);
                            break;
                        case WebSocketState.CloseReceived:
                            // Remote already sent close; echo back to complete the handshake.
                            await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                                                       "disposing", CancellationToken.None);
                            break;
                    }
                }
                catch { /* ignore errors on dispose */ }
                _ws.Dispose();
                _ws = null;
            }
        }

        // ─── Connection helpers ───────────────────────────────────────────────

        private async Task ConnectAndHandshakeAsync(CancellationToken ct)
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_serverUrl), ct);
            await SendFrameAsync(EncodeJoin(), ct);
            _remotePeerId = await WaitForPeerAsync(ct);
        }

        private async Task<string> WaitForPeerAsync(CancellationToken ct)
        {
            while (true)
            {
                var frame = await ReadFrameAsync(ct)
                    ?? throw new InvalidOperationException(
                        "Server closed connection before sending 'peer' message.");
                var (type, fields) = ParseFrame(frame);
                if (type == "peer")
                    return fields.TryGetValue("senderId", out var id) ? (string)id : string.Empty;
                if (type == "error")
                {
                    var msg = fields.TryGetValue("message", out var m) ? (string)m : "unknown";
                    throw new AutomergeNativeException($"Sync server error during handshake: {msg}");
                }
                // ignore other messages during handshake
            }
        }

        private async Task PushAsync(Document doc, SyncState syncState, CancellationToken ct)
        {
            var outgoing = syncState.GenerateSyncMessage(doc);
            if (outgoing.Length > 0)
                await SendFrameAsync(EncodeSyncMessage(outgoing), ct);
        }

        // ─── CBOR encode ──────────────────────────────────────────────────────

        private byte[] EncodeJoin()
        {
            var w = new CborWriter(CborConformanceMode.Lax);
            w.WriteStartMap(null);                                    // indefinite map
            Kv(w, "type",    "join");
            Kv(w, "senderId", _peerId);
            w.WriteTextString("peerMetadata");
            w.WriteStartMap(null); w.WriteEndMap();                   // {}
            w.WriteTextString("supportedProtocolVersions");
            w.WriteStartArray(null); w.WriteTextString("1"); w.WriteEndArray();
            w.WriteEndMap();
            return w.Encode();
        }

        private byte[] EncodeSyncMessage(byte[] syncData)
        {
            var w = new CborWriter(CborConformanceMode.Lax);
            w.WriteStartMap(null);
            Kv(w, "type",       "sync");
            Kv(w, "senderId",    _peerId);
            Kv(w, "targetId",    _remotePeerId ?? string.Empty);
            Kv(w, "documentId",  _documentId);
            w.WriteTextString("data");
            w.WriteByteString(syncData);
            w.WriteEndMap();
            return w.Encode();
        }

        private static void Kv(CborWriter w, string key, string value)
        {
            w.WriteTextString(key);
            w.WriteTextString(value);
        }

        // ─── CBOR decode ──────────────────────────────────────────────────────

        private static (string type, Dictionary<string, object> fields) ParseFrame(byte[] data)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                var r = new CborReader(data, CborConformanceMode.Lax);
                int? count = r.ReadStartMap();
                int remaining = count ?? int.MaxValue;
                while (remaining-- > 0
                    && r.PeekState() != CborReaderState.EndMap
                    && r.PeekState() != CborReaderState.Finished)
                {
                    if (r.PeekState() != CborReaderState.TextString) break;
                    var key = r.ReadTextString();
                    switch (r.PeekState())
                    {
                        case CborReaderState.TextString:
                            fields[key] = r.ReadTextString();
                            break;
                        case CborReaderState.ByteString:
                            fields[key] = r.ReadByteString();
                            break;
                        default:
                            r.SkipValue();
                            break;
                    }
                }
            }
            catch { /* tolerate malformed frames */ }

            fields.TryGetValue("type", out var typeObj);
            return (typeObj as string ?? string.Empty, fields);
        }

        // ─── WebSocket I/O ────────────────────────────────────────────────────

        private async Task SendFrameAsync(byte[] data, CancellationToken ct)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected.");
            await _ws.SendAsync(new ArraySegment<byte>(data),
                                WebSocketMessageType.Binary,
                                endOfMessage: true, ct);
        }

        private async Task<byte[]?> ReadFrameAsync(CancellationToken ct)
        {
            if (_ws == null) return null;
            var buf    = new byte[64 * 1024]; // 64 KiB receive buffer per chunk
            var chunks = new List<byte[]>();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.Count > 0)
                {
                    var chunk = new byte[result.Count];
                    Buffer.BlockCopy(buf, 0, chunk, 0, result.Count);
                    chunks.Add(chunk);
                }
            } while (!result.EndOfMessage);

            if (chunks.Count == 0) return Array.Empty<byte>();
            if (chunks.Count == 1) return chunks[0];

            int total = 0;
            foreach (var c in chunks) total += c.Length;
            var full = new byte[total];
            int off = 0;
            foreach (var c in chunks) { Buffer.BlockCopy(c, 0, full, off, c.Length); off += c.Length; }
            return full;
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
