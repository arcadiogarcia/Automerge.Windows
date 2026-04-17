using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// A <see cref="INetworkAdapter"/> that connects to an automerge-repo v1
    /// sync server over WebSocket, multiplexing all documents over a single connection.
    /// Equivalent to the JS <c>WebSocketClientAdapter</c>.
    ///
    /// <para><b>Usage:</b></para>
    /// <code><![CDATA[
    /// var adapter = new WebSocketNetworkAdapter("wss://sync.automerge.org");
    /// await using var repo = new Repo(new RepoOptions
    /// {
    ///     Network = [adapter],
    ///     Storage = new FileStorageAdapter(),
    /// });
    /// ]]></code>
    /// </summary>
    public sealed class WebSocketNetworkAdapter : INetworkAdapter
    {
        private readonly string _serverUrl;
        private ClientWebSocket? _ws;
        private string? _peerId;
        private string? _remotePeerId;
        private CancellationTokenSource? _cts;
        private Task? _receiveLoop;
        private bool _disposed;

        /// <inheritdoc />
        public event Action<NetworkMessage>? MessageReceived;
        /// <inheritdoc />
        public event Action<string, PeerMetadata?>? PeerConnected;
        /// <inheritdoc />
        public event Action<string>? PeerDisconnected;

        /// <summary>
        /// Create a WebSocket network adapter.
        /// </summary>
        /// <param name="serverUrl">WebSocket URL, e.g. <c>"wss://sync.automerge.org"</c>.</param>
        public WebSocketNetworkAdapter(string serverUrl)
        {
            _serverUrl = serverUrl;
        }

        /// <inheritdoc />
        public async Task ConnectAsync(string peerId, PeerMetadata? metadata = null, CancellationToken ct = default)
        {
            _peerId = peerId;
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_serverUrl), ct);

            // Send join
            await SendFrameAsync(EncodeJoin(peerId), ct);

            // Wait for peer response
            var frame = await ReadFrameAsync(ct);
            if (frame != null)
            {
                var (type, fields) = ParseFrame(frame);
                if (type == "peer" && fields.TryGetValue("senderId", out var sid))
                {
                    _remotePeerId = (string)sid;
                    PeerConnected?.Invoke(_remotePeerId, null);
                }
            }

            // Start receive loop
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        /// <inheritdoc />
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_ws?.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { }
            }
            if (_remotePeerId != null)
                PeerDisconnected?.Invoke(_remotePeerId);
        }

        /// <inheritdoc />
        public async Task SendAsync(NetworkMessage message, CancellationToken ct = default)
        {
            if (_ws?.State != WebSocketState.Open || _peerId == null) return;

            byte[] frame;
            if (message.Type == "request" || message.Type == "sync")
            {
                frame = EncodeSyncMessage(message.Type, _peerId, message.TargetId,
                    message.DocumentId ?? "", message.Data ?? Array.Empty<byte>());
            }
            else return; // unsupported type

            await SendFrameAsync(frame, ct);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await DisconnectAsync();
            if (_receiveLoop != null)
            {
                try { await _receiveLoop; } catch { }
            }
            _ws?.Dispose();
        }

        // ─── Receive loop ─────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
                {
                    var frame = await ReadFrameAsync(ct);
                    if (frame == null) break;

                    var (type, fields) = ParseFrame(frame);
                    if ((type == "sync" || type == "request") && fields.TryGetValue("data", out var data))
                    {
                        MessageReceived?.Invoke(new NetworkMessage
                        {
                            SenderId = fields.TryGetValue("senderId", out var s) ? (string)s : "",
                            TargetId = _peerId ?? "",
                            DocumentId = fields.TryGetValue("documentId", out var d) ? (string)d : null,
                            Type = type,
                            Data = (byte[])data,
                        });
                    }
                    else if (type == "error")
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }

            if (_remotePeerId != null)
                PeerDisconnected?.Invoke(_remotePeerId);
        }

        // ─── WebSocket I/O ────────────────────────────────────────────────────

        private async Task SendFrameAsync(byte[] data, CancellationToken ct)
        {
            if (_ws?.State != WebSocketState.Open) return;
            await _ws.SendAsync(new ArraySegment<byte>(data),
                WebSocketMessageType.Binary, endOfMessage: true, ct);
        }

        private async Task<byte[]?> ReadFrameAsync(CancellationToken ct)
        {
            if (_ws == null) return null;
            if (_ws.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
                return null;

            var buf = new byte[64 * 1024];
            var chunks = new List<byte[]>();
            WebSocketReceiveResult result;
            try
            {
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
            }
            catch (WebSocketException) { return null; }

            if (chunks.Count == 0) return Array.Empty<byte>();
            if (chunks.Count == 1) return chunks[0];
            int total = 0;
            foreach (var c in chunks) total += c.Length;
            var full = new byte[total];
            int off = 0;
            foreach (var c in chunks) { Buffer.BlockCopy(c, 0, full, off, c.Length); off += c.Length; }
            return full;
        }

        // ─── CBOR encode/decode ───────────────────────────────────────────────

        private static byte[] EncodeJoin(string peerId)
        {
            var w = new CborWriter(CborConformanceMode.Lax);
            w.WriteStartMap(4);
            Kv(w, "type", "join");
            Kv(w, "senderId", peerId);
            w.WriteTextString("peerMetadata");
            w.WriteStartMap(0); w.WriteEndMap();
            w.WriteTextString("supportedProtocolVersions");
            w.WriteStartArray(1); w.WriteTextString("1"); w.WriteEndArray();
            w.WriteEndMap();
            return w.Encode();
        }

        private static byte[] EncodeSyncMessage(string type, string senderId, string targetId,
            string documentId, byte[] data)
        {
            var w = new CborWriter(CborConformanceMode.Lax);
            w.WriteStartMap(5);
            Kv(w, "type", type);
            Kv(w, "senderId", senderId);
            Kv(w, "targetId", targetId);
            Kv(w, "documentId", documentId);
            w.WriteTextString("data");
            w.WriteByteString(data);
            w.WriteEndMap();
            return w.Encode();
        }

        private static void Kv(CborWriter w, string key, string value)
        {
            w.WriteTextString(key);
            w.WriteTextString(value);
        }

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
                        case CborReaderState.TextString: fields[key] = r.ReadTextString(); break;
                        case CborReaderState.ByteString: fields[key] = r.ReadByteString(); break;
                        default: r.SkipValue(); break;
                    }
                }
            }
            catch { }
            fields.TryGetValue("type", out var typeObj);
            return (typeObj as string ?? string.Empty, fields);
        }
    }
}
