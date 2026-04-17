using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// Options for creating a <see cref="Repo"/>.
    /// </summary>
    public sealed class RepoOptions
    {
        /// <summary>Persistent storage (optional). If null, documents exist only in memory.</summary>
        public IStorageAdapter? Storage { get; init; }

        /// <summary>Network adapters for syncing with remote peers.</summary>
        public IReadOnlyList<INetworkAdapter>? Network { get; init; }

        /// <summary>
        /// Stable peer ID for this repo instance.
        /// If null, a random UUID is generated.
        /// </summary>
        public string? PeerId { get; init; }

        /// <summary>Whether this repo's storage ID should be shared with peers.</summary>
        public bool SharePolicy { get; init; } = true;
    }

    /// <summary>
    /// Central hub for creating, finding, and syncing Automerge documents.
    ///
    /// <para>
    /// Matches the JS <c>Repo</c> from <c>@automerge/automerge-repo</c>.
    /// Manages document lifecycle, pluggable storage persistence, and network sync.
    /// </para>
    ///
    /// <para><b>Usage:</b></para>
    /// <code><![CDATA[
    /// await using var repo = new Repo(new RepoOptions
    /// {
    ///     Storage = new MyStorageAdapter(),
    ///     Network = [new MyWebSocketAdapter("wss://sync.example.com")],
    /// });
    ///
    /// var handle = repo.Create();
    /// handle.Change(doc => doc.Put(null, "title", "\"Hello\""));
    ///
    /// var found = await repo.Find("automerge:abc123");
    /// ]]></code>
    /// </summary>
    public sealed class Repo : IAsyncDisposable, IDisposable
    {
        private readonly ConcurrentDictionary<string, DocHandle> _handles = new();
        private readonly IStorageAdapter? _storage;
        private readonly IReadOnlyList<INetworkAdapter> _network;
        private readonly ConcurrentDictionary<string, SyncState> _peerSyncStates = new();
        private bool _disposed;

        /// <summary>This repo's peer ID.</summary>
        public string PeerId { get; }

        /// <summary>All document handles currently in the repo (by document ID).</summary>
        public IReadOnlyDictionary<string, DocHandle> Handles => _handles;

        /// <summary>Raised when a new document is created or found.</summary>
        public event Action<DocHandle>? DocumentAdded;

        /// <summary>Raised when a document is deleted.</summary>
        public event Action<DocHandle>? DocumentDeleted;

        // ─── Construction ─────────────────────────────────────────────────────

        /// <summary>Create a new Repo with the given options.</summary>
        public Repo(RepoOptions? options = null)
        {
            var opts = options ?? new RepoOptions();
            PeerId = opts.PeerId ?? Guid.NewGuid().ToString();
            _storage = opts.Storage;
            _network = opts.Network ?? Array.Empty<INetworkAdapter>();

            // Subscribe to network messages
            foreach (var adapter in _network)
            {
                adapter.MessageReceived += OnNetworkMessage;
                adapter.PeerConnected += OnPeerConnected;
            }
        }

        // ─── Document lifecycle ───────────────────────────────────────────────

        /// <summary>
        /// Create a new document with a fresh URL, optionally initialized by a callback.
        /// </summary>
        /// <param name="init">Optional initialization callback.</param>
        /// <returns>A ready <see cref="DocHandle"/>.</returns>
        public DocHandle Create(Action<Document>? init = null)
        {
            ThrowIfDisposed();
            var docId = AutomergeUrl.GenerateDocumentId();
            var doc = new Document();
            init?.Invoke(doc);

            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Ready;
            _handles[docId] = handle;

            // Persist initial state
            _ = PersistAsync(handle);

            // Wire up change events for auto-save
            handle.Changed += OnHandleChanged;
            handle.Deleted += OnHandleDeleted;

            DocumentAdded?.Invoke(handle);
            return handle;
        }

        /// <summary>
        /// Find (or load) a document by its URL or document ID.
        /// Returns a handle; the document will load from storage first, then request from peers.
        /// </summary>
        /// <param name="urlOrDocId">An Automerge URL (<c>automerge:...</c>) or a raw document ID.</param>
        public async Task<DocHandle> Find(string urlOrDocId)
        {
            ThrowIfDisposed();
            var docId = urlOrDocId.StartsWith(AutomergeUrl.Prefix, StringComparison.Ordinal)
                ? AutomergeUrl.Parse(urlOrDocId)
                : urlOrDocId;

            // Return existing handle if we already have it
            if (_handles.TryGetValue(docId, out var existing))
                return existing;

            // Create a handle in Loading state
            var doc = new Document();
            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Loading;
            _handles[docId] = handle;
            handle.Changed += OnHandleChanged;
            handle.Deleted += OnHandleDeleted;

            // Try loading from storage
            bool loaded = false;
            if (_storage != null)
            {
                loaded = await LoadFromStorageAsync(handle);
            }

            if (loaded)
            {
                handle.State = DocHandleState.Ready;
                DocumentAdded?.Invoke(handle);
                return handle;
            }

            // Request from peers
            handle.State = DocHandleState.Requesting;
            // TODO: implement peer request protocol
            // For now, if no storage had it, mark as ready (empty doc)
            handle.State = DocHandleState.Ready;
            DocumentAdded?.Invoke(handle);
            return handle;
        }

        /// <summary>
        /// Delete a document by URL or ID. Removes from storage and cache.
        /// </summary>
        public async Task DeleteAsync(string urlOrDocId)
        {
            ThrowIfDisposed();
            var docId = urlOrDocId.StartsWith(AutomergeUrl.Prefix, StringComparison.Ordinal)
                ? AutomergeUrl.Parse(urlOrDocId)
                : urlOrDocId;

            if (_handles.TryRemove(docId, out var handle))
            {
                handle.Delete();
                DocumentDeleted?.Invoke(handle);
            }
            if (_storage != null)
            {
                await _storage.RemoveRangeAsync(new StorageKey(docId));
            }
        }

        /// <summary>
        /// Import a binary document into the repo.
        /// </summary>
        /// <param name="binary">Document bytes (from <see cref="Document.Save"/>).</param>
        /// <param name="documentId">Optional document ID; a new one is generated if null.</param>
        /// <returns>A ready <see cref="DocHandle"/>.</returns>
        public DocHandle Import(byte[] binary, string? documentId = null)
        {
            ThrowIfDisposed();
            var docId = documentId ?? AutomergeUrl.GenerateDocumentId();
            var doc = Document.Load(binary);

            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Ready;
            _handles[docId] = handle;
            handle.Changed += OnHandleChanged;
            handle.Deleted += OnHandleDeleted;

            _ = PersistAsync(handle);
            DocumentAdded?.Invoke(handle);
            return handle;
        }

        /// <summary>
        /// Export a document to binary.
        /// </summary>
        public byte[] Export(string urlOrDocId)
        {
            ThrowIfDisposed();
            var docId = urlOrDocId.StartsWith(AutomergeUrl.Prefix, StringComparison.Ordinal)
                ? AutomergeUrl.Parse(urlOrDocId)
                : urlOrDocId;

            if (!_handles.TryGetValue(docId, out var handle))
                throw new KeyNotFoundException($"Document not found: {docId}");
            return handle.Doc.Save();
        }

        /// <summary>
        /// Clone an existing document handle, producing a new handle with shared history but a new URL.
        /// </summary>
        public DocHandle Clone(DocHandle source)
        {
            ThrowIfDisposed();
            var docId = AutomergeUrl.GenerateDocumentId();
            var forked = source.Doc.Fork();

            var handle = new DocHandle(docId, forked);
            handle.State = DocHandleState.Ready;
            _handles[docId] = handle;
            handle.Changed += OnHandleChanged;
            handle.Deleted += OnHandleDeleted;

            _ = PersistAsync(handle);
            DocumentAdded?.Invoke(handle);
            return handle;
        }

        // ─── Storage helpers ──────────────────────────────────────────────────

        private async Task PersistAsync(DocHandle handle)
        {
            if (_storage == null) return;
            try
            {
                var bytes = handle.InternalDoc.Save();
                var headsHex = BitConverter.ToString(handle.InternalDoc.GetHeads()).Replace("-", "").ToLowerInvariant();
                var key = new StorageKey(handle.DocumentId, "snapshot", headsHex);
                await _storage.SaveAsync(key, bytes);
            }
            catch { /* storage failure should not crash the app */ }
        }

        private async Task<bool> LoadFromStorageAsync(DocHandle handle)
        {
            if (_storage == null) return false;
            try
            {
                var chunks = await _storage.LoadRangeAsync(new StorageKey(handle.DocumentId));
                if (chunks.Length == 0) return false;

                foreach (var chunk in chunks)
                {
                    handle.InternalDoc.LoadIncremental(chunk.Data);
                }
                return true;
            }
            catch { return false; }
        }

        // ─── Network message handling ─────────────────────────────────────────

        private void OnNetworkMessage(NetworkMessage msg)
        {
            if (msg.DocumentId == null || msg.Data == null) return;
            if (msg.Type is not ("sync" or "request")) return;

            if (!_handles.TryGetValue(msg.DocumentId, out var handle)) return;
            if (handle.State != DocHandleState.Ready) return;

            // Get or create a per-peer sync state
            var syncState = _peerSyncStates.GetOrAdd(msg.SenderId, _ => new SyncState());

            lock (handle)
            {
                syncState.ReceiveSyncMessage(handle.InternalDoc, msg.Data);
            }

            handle.NotifyRemoteChange();

            // Send response
            var response = syncState.GenerateSyncMessage(handle.InternalDoc);
            if (response.Length > 0)
            {
                foreach (var adapter in _network)
                {
                    _ = adapter.SendAsync(new NetworkMessage
                    {
                        SenderId = PeerId,
                        TargetId = msg.SenderId,
                        DocumentId = msg.DocumentId,
                        Type = "sync",
                        Data = response,
                    });
                }
            }
        }

        private void OnPeerConnected(string peerId, PeerMetadata? metadata)
        {
            // When a new peer connects, sync all our documents
            foreach (var (docId, handle) in _handles)
            {
                if (handle.State != DocHandleState.Ready) continue;
                var syncState = _peerSyncStates.GetOrAdd(peerId, _ => new SyncState());
                var msg = syncState.GenerateSyncMessage(handle.InternalDoc);
                if (msg.Length > 0)
                {
                    foreach (var adapter in _network)
                    {
                        _ = adapter.SendAsync(new NetworkMessage
                        {
                            SenderId = PeerId,
                            TargetId = peerId,
                            DocumentId = docId,
                            Type = "sync",
                            Data = msg,
                        });
                    }
                }
            }
        }

        // ─── Handle event wiring ──────────────────────────────────────────────

        private void OnHandleChanged(object? sender, DocHandleChangeEventArgs e)
        {
            if (sender is not DocHandle handle) return;
            // Auto-persist on change
            _ = PersistAsync(handle);
            // Auto-sync to peers (push local changes)
            if (!e.IsRemote)
            {
                foreach (var (peerId, syncState) in _peerSyncStates)
                {
                    var msg = syncState.GenerateSyncMessage(handle.InternalDoc);
                    if (msg.Length > 0)
                    {
                        foreach (var adapter in _network)
                        {
                            _ = adapter.SendAsync(new NetworkMessage
                            {
                                SenderId = PeerId,
                                TargetId = peerId,
                                DocumentId = handle.DocumentId,
                                Type = "sync",
                                Data = msg,
                            });
                        }
                    }
                }
            }
        }

        private void OnHandleDeleted(object? sender, EventArgs e)
        {
            if (sender is not DocHandle handle) return;
            _handles.TryRemove(handle.DocumentId, out _);
        }

        // ─── Shutdown ─────────────────────────────────────────────────────────

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        /// <summary>Synchronous dispose — calls async shutdown synchronously.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var adapter in _network)
            {
                adapter.MessageReceived -= OnNetworkMessage;
                adapter.PeerConnected -= OnPeerConnected;
            }

            foreach (var (_, syncState) in _peerSyncStates)
                syncState.Dispose();
            _peerSyncStates.Clear();

            foreach (var (_, handle) in _handles)
                handle.Dispose();
            _handles.Clear();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var adapter in _network)
            {
                adapter.MessageReceived -= OnNetworkMessage;
                adapter.PeerConnected -= OnPeerConnected;
                try { await adapter.DisconnectAsync(); } catch { }
                try { await adapter.DisposeAsync(); } catch { }
            }

            foreach (var (_, syncState) in _peerSyncStates)
            {
                syncState.Dispose();
            }
            _peerSyncStates.Clear();

            foreach (var (_, handle) in _handles)
            {
                handle.Dispose();
            }
            _handles.Clear();
        }
    }
}
