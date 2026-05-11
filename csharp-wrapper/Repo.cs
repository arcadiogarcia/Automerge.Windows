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

        // ─── Persist coalescing ───────────────────────────────────────────────
        // Background persists are coalesced per-document: at any moment we
        // hold at most one in-flight save Task per docId. Subsequent calls
        // that arrive while a save is running mark the entry "dirty" so a
        // follow-up save runs as soon as the current one finishes,
        // capturing the document's then-latest state. This guarantees
        // FlushAsync() can drain to a known durable point.
        private sealed class PersistEntry
        {
            public Task Current = Task.CompletedTask;
            public bool Dirty;
            /// <summary>True while <see cref="RunPersistLoopAsync"/> is
            /// actively looping for this entry. Set under the entry lock
            /// at loop entry and cleared under the lock at exit (only if
            /// Dirty is false). Lets <see cref="SchedulePersist"/> decide
            /// "queue dirty vs start a new loop" without racing on
            /// <see cref="Task.IsCompleted"/> — Current may briefly read
            /// as completed before the loop has actually returned.</summary>
            public bool LoopRunning;
        }
        private readonly ConcurrentDictionary<string, PersistEntry> _persistEntries = new();

        // ─── Pending Find peer-request waiters ────────────────────────────────
        // When Find can't load from storage it broadcasts a "request"
        // message and registers a TaskCompletionSource here. Incoming sync
        // messages for that docId complete the TCS, releasing the Find.
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingFindWaiters = new();

        /// <summary>Default timeout for Find's peer-request fallback when no
        /// local copy exists (ms). Find returns a Ready-but-empty handle
        /// after this elapses if no peer responds; callers can listen on
        /// <see cref="DocHandle.Changed"/> for content arriving later.</summary>
        public int FindPeerRequestTimeoutMs { get; set; } = 5000;

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
            SchedulePersist(handle);

            // Wire up change events for auto-save
            handle.Changed += OnHandleChanged;
            handle.Deleted += OnHandleDeleted;

            DocumentAdded?.Invoke(handle);
            return handle;
        }

        /// <summary>
        /// Find (or load) a document by its URL or document ID.
        ///
        /// <para>Resolution order:
        /// <list type="number">
        ///   <item>Return the in-memory handle if one already exists.</item>
        ///   <item>Load from local storage via the configured <see cref="IStorageAdapter"/>.</item>
        ///   <item>Broadcast a peer "request" message and wait for any peer
        ///         to respond with content (up to <see cref="FindPeerRequestTimeoutMs"/>).</item>
        ///   <item>If no peer responded within the timeout, return a
        ///         <see cref="DocHandleState.Ready"/> but empty handle.
        ///         Content arriving later via sync raises the handle's
        ///         <see cref="DocHandle.Changed"/> event.</item>
        /// </list>
        /// </para>
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

            // Storage didn't have it. Move to Requesting state and ask
            // peers. If any peer responds with content before the
            // timeout, OnNetworkMessage promotes us to Ready and resolves
            // the TCS. Otherwise we resolve as Ready-but-empty so the
            // caller isn't blocked forever; content can still arrive
            // later via the normal sync path.
            handle.State = DocHandleState.Requesting;
            DocumentAdded?.Invoke(handle);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingFindWaiters[docId] = tcs;
            BroadcastDocumentRequest(docId);

            using var cts = new System.Threading.CancellationTokenSource(FindPeerRequestTimeoutMs);
            try
            {
                await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                // Peer responded; OnNetworkMessage already set state=Ready.
            }
            catch (OperationCanceledException)
            {
                // Timed out. Resolve to a Ready-but-empty handle. We do
                // NOT mark as Unavailable because content may arrive
                // later (e.g. peer connects after our timeout).
                _pendingFindWaiters.TryRemove(docId, out _);
                if (handle.State == DocHandleState.Requesting)
                    handle.State = DocHandleState.Ready;
            }
            return handle;
        }

        /// <summary>Broadcast a "request" message for the given document id
        /// to every connected peer. Used by <see cref="Find"/> and by the
        /// peer-connected handler so newly-arriving peers can answer
        /// pending requests.</summary>
        private void BroadcastDocumentRequest(string docId)
        {
            foreach (var adapter in _network)
            {
                _ = adapter.SendAsync(new NetworkMessage
                {
                    SenderId = PeerId,
                    TargetId = "*",
                    DocumentId = docId,
                    Type = "request",
                    Data = Array.Empty<byte>(),
                });
            }
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

            SchedulePersist(handle);
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

            SchedulePersist(handle);
            DocumentAdded?.Invoke(handle);
            return handle;
        }

        // ─── Storage helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Schedule a coalesced background persist for this handle. If
        /// nothing is in flight for the doc, kicks off a save immediately.
        /// If a save is already in flight, marks the entry dirty so a
        /// follow-up save (capturing the handle's latest state) is queued
        /// once the current save completes. Multiple rapid <see cref="DocHandle.Change"/>
        /// calls produce at most one extra save after the current one —
        /// never an unbounded fan-out of concurrent saves of the same doc.
        /// </summary>
        private void SchedulePersist(DocHandle handle)
        {
            if (_storage == null) return;
            var entry = _persistEntries.GetOrAdd(handle.DocumentId, _ => new PersistEntry());
            lock (entry)
            {
                if (entry.LoopRunning)
                {
                    // A save loop is already running. Mark dirty; the
                    // loop's post-save check (also under entry's lock)
                    // will see this and run another pass.
                    entry.Dirty = true;
                    return;
                }
                entry.LoopRunning = true;
                entry.Current = RunPersistLoopAsync(handle, entry);
            }
        }

        private async Task RunPersistLoopAsync(DocHandle handle, PersistEntry entry)
        {
            // Hop off the caller's thread so the caller (Change → Changed
            // event handler) returns promptly. This DOES NOT change
            // anything observable about entry.LoopRunning, which we hold
            // until the loop terminates under the entry lock below.
            await Task.Yield();
            while (true)
            {
                try { await PersistOnceAsync(handle); }
                catch { /* storage failure is non-fatal; FlushAsync still completes */ }
                lock (entry)
                {
                    if (!entry.Dirty)
                    {
                        // Mark loop stopped while STILL holding the lock,
                        // so any concurrent SchedulePersist call that
                        // arrives later sees LoopRunning=false and starts
                        // a fresh loop. Any SchedulePersist that arrives
                        // BEFORE we reach this point has already set
                        // Dirty=true (under the same lock), so we'd be
                        // looping again instead of returning.
                        entry.LoopRunning = false;
                        return;
                    }
                    entry.Dirty = false;
                }
            }
        }

        private async Task PersistOnceAsync(DocHandle handle)
        {
            if (_storage == null) return;
            byte[] bytes;
            byte[] headsRaw;
            try
            {
                // Snapshot under DocHandle's lock so Save and the heads
                // we use for the key correspond to the same document
                // state, even if Change() fires concurrently.
                bytes = handle.SaveLocked();
                headsRaw = handle.HeadsLocked();
            }
            catch (ObjectDisposedException) { return; }
            // Skip empty docs (no heads). Persisting one would key the
            // snapshot under an empty hex string, which collides with the
            // "snapshot" directory used for real snapshots on filesystems
            // that don't tolerate empty path segments. The empty doc is
            // recreated on Find anyway, so there's nothing to lose.
            if (headsRaw.Length == 0) return;
            var headsHex = BitConverter.ToString(headsRaw).Replace("-", "").ToLowerInvariant();
            var key = new StorageKey(handle.DocumentId, "snapshot", headsHex);
            await _storage.SaveAsync(key, bytes).ConfigureAwait(false);
        }

        /// <summary>
        /// Await all in-flight (and any queued follow-up) persists, optionally
        /// limited to a specific set of document IDs. Resolves once every
        /// document has at least one persist run since the most recent
        /// <see cref="DocHandle.Change"/> or remote sync.
        ///
        /// <para>Matches the JS <c>Repo.flush(documentIds?)</c> API from
        /// <c>@automerge/automerge-repo</c>. Callers that need a durable
        /// shutdown should <c>await FlushAsync()</c> before exiting — or
        /// use <see cref="DisposeAsync"/>, which flushes implicitly.</para>
        /// </summary>
        public async Task FlushAsync(IEnumerable<string>? documentIds = null)
        {
            if (_storage == null) return;
            // Materialize once so we iterate the same set of IDs on each
            // pass and aren't surprised by handles added concurrently.
            string[] ids = (documentIds ?? _handles.Keys).ToArray();
            // Drain in a loop: as long as ANY tracked entry still has
            // work pending (loop running or dirty queued), wait for the
            // current Task and re-check.
            while (true)
            {
                var pending = new List<Task>();
                bool anyLoopRunningOrDirty = false;
                foreach (var id in ids)
                {
                    if (!_persistEntries.TryGetValue(id, out var entry)) continue;
                    Task? current = null;
                    lock (entry)
                    {
                        if (entry.LoopRunning || entry.Dirty)
                        {
                            anyLoopRunningOrDirty = true;
                            current = entry.Current;
                        }
                    }
                    if (current is not null && !current.IsCompleted)
                        pending.Add(current);
                }
                if (!anyLoopRunningOrDirty) return;
                if (pending.Count > 0)
                {
                    try { await Task.WhenAll(pending).ConfigureAwait(false); }
                    catch { /* persist failures are non-fatal */ }
                }
                else
                {
                    // Dirty set but Current already completed — the loop
                    // is between iterations under the entry lock. Yield
                    // and re-check on the next iteration.
                    await Task.Yield();
                }
            }
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
            if (msg.DocumentId == null) return;
            if (msg.Type is not ("sync" or "request")) return;

            if (!_handles.TryGetValue(msg.DocumentId, out var handle)) return;
            // Accept sync messages even while still Loading / Requesting:
            // they're how a doc's initial content arrives when storage was
            // empty and Find is waiting on peers.
            if (handle.State == DocHandleState.Deleted || handle.State == DocHandleState.Unavailable) return;

            // Get or create a per-peer sync state
            var syncState = _peerSyncStates.GetOrAdd(msg.SenderId, _ => new SyncState());

            // Sync messages (msg.Type == "sync") carry content bytes that
            // we apply via ReceiveSyncMessage. Request messages
            // (msg.Type == "request") have no content; they're prompts
            // for us to send our state if we have any.
            bool isRequest = msg.Type == "request";
            bool gotContent = false;
            if (!isRequest && msg.Data is not null && msg.Data.Length > 0)
            {
                lock (handle)
                {
                    syncState.ReceiveSyncMessage(handle.InternalDoc, msg.Data);
                }
                gotContent = true;
            }

            if (gotContent)
            {
                // A Requesting handle that just received content can now
                // be promoted to Ready; this also resolves WhenReady.
                if (handle.State == DocHandleState.Requesting || handle.State == DocHandleState.Loading)
                {
                    handle.State = DocHandleState.Ready;
                    if (_pendingFindWaiters.TryRemove(handle.DocumentId, out var tcs))
                        tcs.TrySetResult(true);
                }
                handle.NotifyRemoteChange();
            }

            // Respond if we have content. For a "request" we replace the
            // per-peer sync state with a fresh one so the response
            // contains a full sync of the document, not an empty
            // "nothing new for you" message that assumes the peer
            // already saw our prior pushes.
            if (handle.State == DocHandleState.Ready)
            {
                SyncState replyState = syncState;
                if (isRequest)
                {
                    replyState = new SyncState();
                    var stale = _peerSyncStates.AddOrUpdate(msg.SenderId, _ => replyState, (_, old) =>
                    {
                        old.Dispose();
                        return replyState;
                    });
                    if (!ReferenceEquals(stale, replyState))
                    {
                        // AddOrUpdate already replaced it; replyState is correct.
                    }
                }
                var response = replyState.GenerateSyncMessage(handle.InternalDoc);
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
        }

        private void OnPeerConnected(string peerId, PeerMetadata? metadata)
        {
            // When a new peer connects:
            //  1. Sync all Ready documents (push our state).
            //  2. Re-broadcast any pending Find requests so the new peer
            //     has a chance to fulfill them.
            foreach (var (docId, handle) in _handles)
            {
                if (handle.State == DocHandleState.Ready)
                {
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
                else if (handle.State == DocHandleState.Requesting
                         && _pendingFindWaiters.ContainsKey(docId))
                {
                    BroadcastDocumentRequest(docId);
                }
            }
        }

        // ─── Handle event wiring ──────────────────────────────────────────────

        private void OnHandleChanged(object? sender, DocHandleChangeEventArgs e)
        {
            if (sender is not DocHandle handle) return;
            // Auto-persist on change
            SchedulePersist(handle);
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

        /// <summary>
        /// Synchronous dispose. Blocks while flushing pending storage
        /// writes so a caller that doesn't use <see cref="DisposeAsync"/>
        /// still gets durable shutdown semantics. Prefer
        /// <c>await using</c> and <see cref="DisposeAsync"/> when possible
        /// since blocking on async work from arbitrary call sites can
        /// deadlock with single-threaded sync contexts.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel anyone waiting on a peer-request find.
            foreach (var (_, tcs) in _pendingFindWaiters)
                tcs.TrySetCanceled();
            _pendingFindWaiters.Clear();

            foreach (var adapter in _network)
            {
                adapter.MessageReceived -= OnNetworkMessage;
                adapter.PeerConnected -= OnPeerConnected;
            }

            // Best-effort durable flush. We catch all exceptions because
            // Dispose must not throw.
            try { FlushAsync().GetAwaiter().GetResult(); } catch { }

            foreach (var (_, syncState) in _peerSyncStates)
                syncState.Dispose();
            _peerSyncStates.Clear();

            foreach (var (_, handle) in _handles)
                handle.Dispose();
            _handles.Clear();
        }

        /// <summary>
        /// Async shutdown. Detaches network adapters, awaits all in-flight
        /// storage writes via <see cref="FlushAsync"/>, then disposes
        /// handles and sync states. This is the recommended shutdown path
        /// — it guarantees no unsaved changes remain in flight.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel anyone waiting on a peer-request find.
            foreach (var (_, tcs) in _pendingFindWaiters)
                tcs.TrySetCanceled();
            _pendingFindWaiters.Clear();

            foreach (var adapter in _network)
            {
                adapter.MessageReceived -= OnNetworkMessage;
                adapter.PeerConnected -= OnPeerConnected;
                try { await adapter.DisconnectAsync(); } catch { }
                try { await adapter.DisposeAsync(); } catch { }
            }

            // Durable flush: drain every pending persist (and any
            // follow-up "dirty" pass) before tearing down handles. This
            // is the contract that makes "Change then Dispose" safe.
            try { await FlushAsync().ConfigureAwait(false); } catch { }

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
