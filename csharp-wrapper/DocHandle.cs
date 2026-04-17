using System;
using System.Threading;
using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// Lifecycle states of a <see cref="DocHandle"/>.
    /// </summary>
    public enum DocHandleState
    {
        /// <summary>Initial state before any network or storage activity.</summary>
        Idle,
        /// <summary>Loading from local storage.</summary>
        Loading,
        /// <summary>Requesting from remote peers.</summary>
        Requesting,
        /// <summary>Document is loaded and ready for use.</summary>
        Ready,
        /// <summary>Document has been deleted.</summary>
        Deleted,
        /// <summary>Document was not found in storage or on any peer.</summary>
        Unavailable
    }

    /// <summary>
    /// Payload for the <see cref="DocHandle.Changed"/> event.
    /// </summary>
    public sealed class DocHandleChangeEventArgs : EventArgs
    {
        /// <summary>The updated document.</summary>
        public required Document Doc { get; init; }

        /// <summary>JSON patches describing what changed (from <see cref="Document.DiffIncremental"/>), or null if not available.</summary>
        public string? PatchesJson { get; init; }

        /// <summary>Whether this change came from a remote peer (true) or a local mutation (false).</summary>
        public bool IsRemote { get; init; }
    }

    /// <summary>
    /// A live handle to an Automerge document managed by a <see cref="Repo"/>.
    ///
    /// <para>
    /// Wraps a <see cref="Document"/> with automatic persistence, network sync,
    /// and change events — matching the JS <c>DocHandle</c> from <c>@automerge/automerge-repo</c>.
    /// </para>
    ///
    /// <para><b>Usage:</b></para>
    /// <code><![CDATA[
    /// var handle = repo.Create();
    /// handle.Change(doc => doc.Put(null, "key", "\"value\""));
    /// handle.Changed += (sender, e) => Console.WriteLine($"Changed! Remote={e.IsRemote}");
    /// ]]></code>
    /// </summary>
    public sealed class DocHandle : IDisposable
    {
        private Document _doc;
        private readonly object _lock = new();
        private DocHandleState _state = DocHandleState.Idle;
        private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        /// <summary>The Automerge URL for this document (<c>automerge:&lt;base58check&gt;</c>).</summary>
        public string Url { get; }

        /// <summary>The document ID (base58check-encoded, without the <c>automerge:</c> prefix).</summary>
        public string DocumentId { get; }

        /// <summary>The current lifecycle state.</summary>
        public DocHandleState State
        {
            get => _state;
            set
            {
                _state = value;
                if (value == DocHandleState.Ready)
                    _readyTcs.TrySetResult();
                else if (value == DocHandleState.Unavailable || value == DocHandleState.Deleted)
                    _readyTcs.TrySetException(new InvalidOperationException($"Document is {value}"));
            }
        }

        /// <summary>Whether the document is loaded and ready for use.</summary>
        public bool IsReady => _state == DocHandleState.Ready;

        /// <summary>Wait until the document is ready (loaded from storage or network).</summary>
        public Task WhenReady() => _readyTcs.Task;

        /// <summary>
        /// Raised when the document changes (either from a local <see cref="Change"/> call
        /// or from an incoming network sync). Includes patches describing what changed.
        /// </summary>
        public event EventHandler<DocHandleChangeEventArgs>? Changed;

        /// <summary>
        /// Raised when this document is deleted.
        /// </summary>
        public event EventHandler? Deleted;

        // ─── Construction (internal — created by Repo) ────────────────────────

        /// <summary>Create a DocHandle wrapping a document with a given ID.</summary>
        public DocHandle(string documentId, Document doc)
        {
            DocumentId = documentId;
            Url = AutomergeUrl.Stringify(documentId);
            _doc = doc;
        }

        // ─── Document access ──────────────────────────────────────────────────

        /// <summary>
        /// Get the current document. Throws if not ready.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the document is not in the Ready state.</exception>
        public Document Doc
        {
            get
            {
                if (_state != DocHandleState.Ready)
                    throw new InvalidOperationException($"Document is not ready (state: {_state})");
                return _doc;
            }
        }

        /// <summary>
        /// Async: get the document, waiting until it's ready.
        /// </summary>
        public async Task<Document> DocAsync()
        {
            await WhenReady();
            return _doc;
        }

        /// <summary>
        /// Get the current heads of the document.
        /// </summary>
        public byte[] Heads()
        {
            lock (_lock) { return _doc.GetHeads(); }
        }

        // ─── Mutation ─────────────────────────────────────────────────────────

        /// <summary>
        /// Make changes to the document. The callback receives the <see cref="Document"/>
        /// for mutation. After the callback returns, changes are auto-committed,
        /// the <see cref="Changed"/> event fires, and the Repo persists + syncs the changes.
        /// </summary>
        /// <param name="changeFn">A function that mutates the document.</param>
        /// <param name="message">Optional commit message.</param>
        public void Change(Action<Document> changeFn, string? message = null)
        {
            if (_state != DocHandleState.Ready)
                throw new InvalidOperationException($"Cannot change document in state {_state}");

            string? patchesJson;
            lock (_lock)
            {
                // Consume any pending diff state
                _ = _doc.DiffIncremental();
                changeFn(_doc);
                if (message != null)
                    _doc.Commit(message);
                patchesJson = _doc.DiffIncremental();
            }

            Changed?.Invoke(this, new DocHandleChangeEventArgs
            {
                Doc = _doc,
                PatchesJson = patchesJson,
                IsRemote = false,
            });
        }

        /// <summary>
        /// Delete this document. Sets state to Deleted and fires the <see cref="Deleted"/> event.
        /// </summary>
        public void Delete()
        {
            State = DocHandleState.Deleted;
            Deleted?.Invoke(this, EventArgs.Empty);
        }

        // ─── Called by Repo for remote changes ───────────────────────

        /// <summary>Notify that a remote change was applied to the document.</summary>
        public void NotifyRemoteChange()
        {
            string? patchesJson;
            lock (_lock)
            {
                patchesJson = _doc.DiffIncremental();
            }
            Changed?.Invoke(this, new DocHandleChangeEventArgs
            {
                Doc = _doc,
                PatchesJson = patchesJson,
                IsRemote = true,
            });
        }

        /// <summary>Access the underlying document directly (for repo internals).</summary>
        public Document InternalDoc => _doc;

        // ─── IDisposable ──────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _doc?.Dispose();
            }
        }
    }
}
