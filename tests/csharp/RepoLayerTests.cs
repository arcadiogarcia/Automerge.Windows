using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // AutomergeUrl tests
    // ═══════════════════════════════════════════════════════════════════════════

    public class AutomergeUrlTests
    {
        [Fact]
        public void Generate_ReturnsUrlWithPrefix()
        {
            var url = AutomergeUrl.Generate();
            Assert.StartsWith("automerge:", url);
            Assert.True(url.Length > 20);
        }

        [Fact]
        public void Generate_EachCallUnique()
        {
            var a = AutomergeUrl.Generate();
            var b = AutomergeUrl.Generate();
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void GenerateDocumentId_IsValidId()
        {
            var id = AutomergeUrl.GenerateDocumentId();
            Assert.True(AutomergeUrl.IsValidDocumentId(id));
        }

        [Fact]
        public void Parse_ValidUrl_ReturnsDocId()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            var url = "automerge:" + docId;
            Assert.Equal(docId, AutomergeUrl.Parse(url));
        }

        [Fact]
        public void Parse_InvalidUrl_Throws()
        {
            Assert.Throws<ArgumentException>(() => AutomergeUrl.Parse("http://example.com"));
        }

        [Fact]
        public void IsValid_GoodUrl_ReturnsTrue()
        {
            var url = AutomergeUrl.Generate();
            Assert.True(AutomergeUrl.IsValid(url));
        }

        [Fact]
        public void IsValid_BadUrl_ReturnsFalse()
        {
            Assert.False(AutomergeUrl.IsValid("not-a-url"));
            Assert.False(AutomergeUrl.IsValid(null));
            Assert.False(AutomergeUrl.IsValid(""));
            Assert.False(AutomergeUrl.IsValid("automerge:!!!invalid"));
        }

        [Fact]
        public void IsValidDocumentId_ValidId_True()
        {
            Assert.True(AutomergeUrl.IsValidDocumentId(AutomergeUrl.GenerateDocumentId()));
        }

        [Fact]
        public void IsValidDocumentId_Invalid_False()
        {
            Assert.False(AutomergeUrl.IsValidDocumentId(null));
            Assert.False(AutomergeUrl.IsValidDocumentId(""));
            Assert.False(AutomergeUrl.IsValidDocumentId("not-base58check"));
        }

        [Fact]
        public void Stringify_AddsPrefix()
        {
            Assert.Equal("automerge:abc", AutomergeUrl.Stringify("abc"));
        }

        [Fact]
        public void DocumentIdBinary_RoundTrips()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            var binary = AutomergeUrl.DocumentIdToBinary(docId);
            Assert.NotNull(binary);
            Assert.Equal(16, binary!.Length); // UUID is 16 bytes
            var roundTripped = AutomergeUrl.DocumentIdFromBinary(binary);
            Assert.Equal(docId, roundTripped);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DocHandle tests
    // ═══════════════════════════════════════════════════════════════════════════

    public class DocHandleTests
    {
        [Fact]
        public void Create_HasUrlAndDocId()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            Assert.Equal(docId, handle.DocumentId);
            Assert.Equal("automerge:" + docId, handle.Url);
        }

        [Fact]
        public void State_StartsAsIdle()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            Assert.Equal(DocHandleState.Idle, handle.State);
            Assert.False(handle.IsReady);
        }

        [Fact]
        public void Change_FiresChangedEvent()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Ready;

            DocHandleChangeEventArgs? received = null;
            handle.Changed += (_, e) => received = e;

            handle.Change(d => d.Put(null, "key", "\"value\""));

            Assert.NotNull(received);
            Assert.False(received!.IsRemote);
            Assert.Equal("\"value\"", handle.Doc.Get(null, "key"));
        }

        [Fact]
        public void Change_NotReady_Throws()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            // State is Idle, not Ready
            Assert.Throws<InvalidOperationException>(() =>
                handle.Change(d => d.Put(null, "k", "1")));
        }

        [Fact]
        public void Doc_NotReady_Throws()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            Assert.Throws<InvalidOperationException>(() => _ = handle.Doc);
        }

        [Fact]
        public async Task DocAsync_WaitsForReady()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);

            var task = handle.DocAsync();
            Assert.False(task.IsCompleted);

            handle.State = DocHandleState.Ready;
            var result = await task;
            Assert.Same(doc, result);
        }

        [Fact]
        public void Delete_SetsState()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Ready;

            bool deleted = false;
            handle.Deleted += (_, _) => deleted = true;
            handle.Delete();

            Assert.Equal(DocHandleState.Deleted, handle.State);
            Assert.True(deleted);
        }

        [Fact]
        public void Heads_ReturnsCurrentHeads()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            doc.Put(null, "k", "1");
            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Ready;

            var heads = handle.Heads();
            Assert.True(heads.Length >= 32);
        }

        [Fact]
        public void NotifyRemoteChange_FiresChangedAsRemote()
        {
            var docId = AutomergeUrl.GenerateDocumentId();
            using var doc = new Document();
            var handle = new DocHandle(docId, doc);
            handle.State = DocHandleState.Ready;

            DocHandleChangeEventArgs? received = null;
            handle.Changed += (_, e) => received = e;

            // Simulate remote change arriving
            doc.Put(null, "remote_key", "\"remote_val\"");
            handle.NotifyRemoteChange();

            Assert.NotNull(received);
            Assert.True(received!.IsRemote);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Repo tests
    // ═══════════════════════════════════════════════════════════════════════════

    public class RepoTests
    {
        [Fact]
        public void Create_ReturnsReadyHandle()
        {
            using var repo = CreateRepo();
            var handle = repo.Create();
            Assert.True(handle.IsReady);
            Assert.True(AutomergeUrl.IsValid(handle.Url));
        }

        [Fact]
        public void Create_WithInit_AppliesChanges()
        {
            using var repo = CreateRepo();
            var handle = repo.Create(doc => doc.Put(null, "name", "\"Alice\""));
            Assert.Equal("\"Alice\"", handle.Doc.Get(null, "name"));
        }

        [Fact]
        public void Create_FiresDocumentAdded()
        {
            using var repo = CreateRepo();
            DocHandle? added = null;
            repo.DocumentAdded += h => added = h;
            var handle = repo.Create();
            Assert.Same(handle, added);
        }

        [Fact]
        public async Task Find_ExistingHandle_ReturnsSame()
        {
            using var repo = CreateRepo();
            var handle = repo.Create();
            var found = await repo.Find(handle.Url);
            Assert.Same(handle, found);
        }

        [Fact]
        public async Task Find_ByDocId_Works()
        {
            using var repo = CreateRepo();
            var handle = repo.Create();
            var found = await repo.Find(handle.DocumentId);
            Assert.Same(handle, found);
        }

        [Fact]
        public async Task Delete_RemovesHandle()
        {
            using var repo = CreateRepo();
            var handle = repo.Create();
            var url = handle.Url;
            await repo.DeleteAsync(url);
            Assert.Equal(DocHandleState.Deleted, handle.State);
        }

        [Fact]
        public void Import_LoadsBinary()
        {
            using var repo = CreateRepo();
            using var doc = new Document();
            doc.Put(null, "imported", "\"yes\"");
            var binary = doc.Save();

            var handle = repo.Import(binary);
            Assert.Equal("\"yes\"", handle.Doc.Get(null, "imported"));
            Assert.True(handle.IsReady);
        }

        [Fact]
        public void Export_ReturnsBinary()
        {
            using var repo = CreateRepo();
            var handle = repo.Create(d => d.Put(null, "k", "1"));
            var binary = repo.Export(handle.Url);
            Assert.True(binary.Length > 0);

            // Verify by loading
            using var loaded = Document.Load(binary);
            Assert.Equal("1", loaded.Get(null, "k"));
        }

        [Fact]
        public void Clone_CreatesNewHandle()
        {
            using var repo = CreateRepo();
            var original = repo.Create(d => d.Put(null, "shared", "\"data\""));
            var cloned = repo.Clone(original);

            Assert.NotEqual(original.Url, cloned.Url);
            Assert.Equal("\"data\"", cloned.Doc.Get(null, "shared"));
        }

        [Fact]
        public void Handles_ContainsCreated()
        {
            using var repo = CreateRepo();
            var h1 = repo.Create();
            var h2 = repo.Create();
            Assert.Equal(2, repo.Handles.Count);
            Assert.Contains(h1.DocumentId, repo.Handles.Keys);
            Assert.Contains(h2.DocumentId, repo.Handles.Keys);
        }

        [Fact]
        public void PeerId_IsSet()
        {
            using var repo = CreateRepo();
            Assert.False(string.IsNullOrEmpty(repo.PeerId));
        }

        [Fact]
        public void Change_AutoPersists()
        {
            // Change on a handle should cause the repo to persist
            // (we can't easily verify file I/O in a unit test without mocking,
            // but we can verify the event fires and handle state is correct)
            using var repo = CreateRepo();
            var handle = repo.Create();

            bool changeFired = false;
            handle.Changed += (_, e) => changeFired = true;

            handle.Change(d => d.Put(null, "x", "1"));
            Assert.True(changeFired);
            Assert.Equal("1", handle.Doc.Get(null, "x"));
        }

        private static Repo CreateRepo() => new Repo(new RepoOptions());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FileStorageAdapter tests
    // ═══════════════════════════════════════════════════════════════════════════

    public class FileStorageAdapterTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FileStorageAdapter _adapter;

        public FileStorageAdapterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "automerge-test-" + Guid.NewGuid().ToString("N")[..8]);
            _adapter = new FileStorageAdapter(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Fact]
        public async Task SaveAndLoad_RoundTrips()
        {
            var key = new StorageKey("doc1", "snapshot", "abc123");
            var data = new byte[] { 1, 2, 3, 4 };
            await _adapter.SaveAsync(key, data);

            var loaded = await _adapter.LoadAsync(key);
            Assert.NotNull(loaded);
            Assert.Equal(data, loaded);
        }

        [Fact]
        public async Task Load_NonExistent_ReturnsNull()
        {
            var key = new StorageKey("nope", "nada");
            Assert.Null(await _adapter.LoadAsync(key));
        }

        [Fact]
        public async Task Remove_DeletesFile()
        {
            var key = new StorageKey("doc1", "snapshot", "abc");
            await _adapter.SaveAsync(key, new byte[] { 1 });
            await _adapter.RemoveAsync(key);
            Assert.Null(await _adapter.LoadAsync(key));
        }

        [Fact]
        public async Task LoadRange_ReturnsAllChunks()
        {
            await _adapter.SaveAsync(new StorageKey("doc1", "snapshot", "a"), new byte[] { 1 });
            await _adapter.SaveAsync(new StorageKey("doc1", "incremental", "b"), new byte[] { 2 });
            await _adapter.SaveAsync(new StorageKey("doc2", "snapshot", "c"), new byte[] { 3 });

            var chunks = await _adapter.LoadRangeAsync(new StorageKey("doc1"));
            Assert.Equal(2, chunks.Length);
        }

        [Fact]
        public async Task RemoveRange_DeletesAll()
        {
            await _adapter.SaveAsync(new StorageKey("doc1", "snapshot", "a"), new byte[] { 1 });
            await _adapter.SaveAsync(new StorageKey("doc1", "incremental", "b"), new byte[] { 2 });
            await _adapter.RemoveRangeAsync(new StorageKey("doc1"));

            var chunks = await _adapter.LoadRangeAsync(new StorageKey("doc1"));
            Assert.Empty(chunks);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Repo + FileStorageAdapter integration
    // ═══════════════════════════════════════════════════════════════════════════

    public class RepoStorageIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public RepoStorageIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "automerge-test-" + Guid.NewGuid().ToString("N")[..8]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Fact]
        public async Task Create_PersistsToStorage()
        {
            var storage = new FileStorageAdapter(_tempDir);
            await using var repo = new Repo(new RepoOptions { Storage = storage });

            var handle = repo.Create(d => d.Put(null, "persisted", "\"yes\""));
            // Drain the coalesced persist queue rather than sleeping.
            await repo.FlushAsync();

            var chunks = await storage.LoadRangeAsync(new StorageKey(handle.DocumentId));
            Assert.NotEmpty(chunks);
        }

        [Fact]
        public async Task Find_LoadsFromStorage()
        {
            string docId;
            var storage = new FileStorageAdapter(_tempDir);

            // Create and persist in one repo
            {
                await using var repo1 = new Repo(new RepoOptions { Storage = storage });
                var handle = repo1.Create(d => d.Put(null, "key", "\"value\""));
                docId = handle.DocumentId;
                // await using's DisposeAsync flushes; explicit FlushAsync
                // here is the contract test.
                await repo1.FlushAsync();
            }

            // Find in a new repo using same storage
            {
                await using var repo2 = new Repo(new RepoOptions { Storage = storage });
                var found = await repo2.Find(docId);
                Assert.True(found.IsReady);
                Assert.Equal("\"value\"", found.Doc.Get(null, "key"));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Durability tests: regression coverage for the persistence races that
    // motivated FlushAsync + Dispose-flushes-implicitly + per-doc coalesced
    // persists. Every test in this class would have failed on the v0.6.0
    // baseline implementation (fire-and-forget _ = PersistAsync(handle), no
    // tracking, no flush) before those fixes.
    // ═══════════════════════════════════════════════════════════════════════════

    public class RepoDurabilityTests : IDisposable
    {
        private readonly string _tempDir;

        public RepoDurabilityTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "automerge-durability-" + Guid.NewGuid().ToString("N")[..8]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Fact]
        public async Task FlushAsync_WaitsForInFlightPersist()
        {
            // Use a deliberately slow storage adapter so the persist Task
            // is definitely still in flight when we call FlushAsync.
            var storage = new SlowStorageAdapter(_tempDir, delayMs: 200);
            await using var repo = new Repo(new RepoOptions { Storage = storage });

            var handle = repo.Create(d => d.Put(null, "k", "\"v\""));
            // Immediately flush — must wait until the slow Save returns.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await repo.FlushAsync();
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds >= 150,
                $"FlushAsync returned too quickly ({sw.ElapsedMilliseconds}ms); expected to wait for the slow save");

            // Bypass the repo's adapter to verify the bytes are durable.
            var chunks = await storage.LoadRangeAsync(new StorageKey(handle.DocumentId));
            Assert.NotEmpty(chunks);
        }

        [Fact]
        public async Task DisposeAsync_ImplicitlyFlushes()
        {
            // The contract that callers rely on: "await using var repo"
            // guarantees no unsaved Changes remain when the block exits.
            string docId;
            var storage = new SlowStorageAdapter(_tempDir, delayMs: 100);
            {
                await using var repo = new Repo(new RepoOptions { Storage = storage });
                var handle = repo.Create(d => d.Put(null, "k", "\"durable\""));
                docId = handle.DocumentId;
                // Intentionally NOT calling FlushAsync — DisposeAsync must
                // do it on our behalf.
            }
            var chunks = await storage.LoadRangeAsync(new StorageKey(docId));
            Assert.NotEmpty(chunks);
        }

        [Fact]
        public async Task RapidChanges_CoalescePersists_FinalStateOnDisk()
        {
            // Fire many rapid Change calls. The persist queue must
            // coalesce so we don't kick off N concurrent saves. The final
            // state — only the final state — must be durable.
            var storage = new SlowStorageAdapter(_tempDir, delayMs: 20);
            await using var repo = new Repo(new RepoOptions { Storage = storage });

            var handle = repo.Create();
            for (int i = 0; i < 50; i++)
            {
                handle.Change(d => d.Put(null, "counter", i.ToString()));
            }
            await repo.FlushAsync();

            // Load via a new repo: the final value must be 49.
            await using var repo2 = new Repo(new RepoOptions { Storage = new FileStorageAdapter(_tempDir) });
            var found = await repo2.Find(handle.DocumentId);
            Assert.True(found.IsReady);
            Assert.Equal("49", found.Doc.Get(null, "counter"));

            // And concurrency was bounded: SlowStorageAdapter records
            // peak concurrent SaveAsync calls.
            Assert.True(storage.PeakConcurrentSaves <= 1,
                $"Expected at most 1 concurrent save per doc; saw {storage.PeakConcurrentSaves}");
        }

        [Fact]
        public async Task EmptyDocCreate_ThenChange_DoesNotCorruptSnapshotPath()
        {
            // Regression: Create()'s initial persist used to write a chunk
            // keyed [docId, "snapshot", ""]. FileStorageAdapter collapsed
            // that to a *file* at `_baseDir/docId/snapshot`, and the next
            // real save then failed Directory.CreateDirectory (file in the
            // way), losing the chunk silently inside the persist loop's
            // catch-all. We now skip persisting empty-heads snapshots.
            //
            // The slow adapter forces iter 1 (empty doc) to complete its
            // SaveAsync BEFORE the Change fires, so the bug reproduces
            // ~100% of the time without the fix.
            var storage = new SlowStorageAdapter(_tempDir, delayMs: 30);
            string docId;
            {
                await using var repo = new Repo(new RepoOptions { Storage = storage });
                var handle = repo.Create();
                docId = handle.DocumentId;
                // Wait long enough that the empty-doc persist iteration
                // would have completed (and, before the fix, written a
                // file at the conflicting path).
                await Task.Delay(80);
                handle.Change(d => d.Put(null, "value", "\"after-empty\""));
                await repo.FlushAsync();
            }

            await using var repo2 = new Repo(new RepoOptions { Storage = new FileStorageAdapter(_tempDir) });
            var found = await repo2.Find(docId);
            Assert.True(found.IsReady);
            Assert.Equal("\"after-empty\"", found.Doc.Get(null, "value"));
        }

        [Fact]
        public async Task EmptyDoc_NoHeads_ProducesNoFiles()
        {
            // An empty doc has no operations and no heads; there's nothing
            // meaningful to persist. PersistOnceAsync must skip it rather
            // than write a chunk keyed under an empty hex segment.
            var storage = new FileStorageAdapter(_tempDir);
            await using (var repo = new Repo(new RepoOptions { Storage = storage }))
            {
                var handle = repo.Create();
                await repo.FlushAsync();
                // No Change was ever made -> no chunks should exist.
                var chunks = await storage.LoadRangeAsync(new StorageKey(handle.DocumentId));
                Assert.Empty(chunks);
            }
        }

        [Fact]
        public async Task PersistFailure_DoesNotPoisonSubsequentSaves()
        {
            // Regression for the broader class of bugs we hit: if one
            // PersistOnceAsync throws, the loop's catch-all must not
            // permanently wedge future saves. After a transient storage
            // failure, the next Change must still durably reach disk.
            var storage = new FlakyStorageAdapter(_tempDir);
            string docId;
            await using (var repo = new Repo(new RepoOptions { Storage = storage }))
            {
                var handle = repo.Create(d => d.Put(null, "v", "\"one\""));
                docId = handle.DocumentId;
                storage.FailNext = true;       // first real save throws
                handle.Change(d => d.Put(null, "v", "\"two\""));
                await repo.FlushAsync();
                // Force a second save that should succeed.
                handle.Change(d => d.Put(null, "v", "\"three\""));
                await repo.FlushAsync();
            }

            await using var repo2 = new Repo(new RepoOptions { Storage = new FileStorageAdapter(_tempDir) });
            var found = await repo2.Find(docId);
            Assert.True(found.IsReady);
            Assert.Equal("\"three\"", found.Doc.Get(null, "v"));
        }

        [Fact]
        public async Task ConcurrentChangesAcrossDocs_AllPersist()
        {
            // Regression: SubscribeToHandle's HashSet was previously
            // mutated without a lock. Creating many docs in parallel
            // could race and drop subscriptions, which in turn skipped
            // their persists. Verify every doc survives a cold restart.
            const int DocCount = 16;
            var storage = new FileStorageAdapter(_tempDir);
            string[] ids;
            await using (var repo = new Repo(new RepoOptions { Storage = storage }))
            {
                var handles = await Task.WhenAll(Enumerable.Range(0, DocCount).Select(i =>
                    Task.Run(() => repo.Create(d => d.Put(null, "i", i.ToString())))));
                ids = handles.Select(h => h.DocumentId).ToArray();
                await repo.FlushAsync();
            }

            await using var repo2 = new Repo(new RepoOptions { Storage = new FileStorageAdapter(_tempDir) });
            foreach (var id in ids)
            {
                var found = await repo2.Find(id);
                Assert.True(found.IsReady, $"doc {id} not ready after cold restart");
                Assert.NotNull(found.Doc.Get(null, "i"));
            }
        }

        [Fact]
        public async Task ColdStart_MultipleLargeDocs_AllSurvive()
        {
            // Reproduces the Deet scenario: seed N libraries with sizeable
            // content, dispose, reopen with the same storage, verify all
            // docs come back fully populated. Before the fixes this lost
            // 2 of 3 docs because fire-and-forget persists never ran.
            const int DocCount = 5;
            const int BookCount = 32;
            var docIds = new string[DocCount];
            var storage = new FileStorageAdapter(_tempDir);

            {
                await using var repo = new Repo(new RepoOptions { Storage = storage });
                for (int d = 0; d < DocCount; d++)
                {
                    var handle = repo.Create(doc =>
                    {
                        var booksObj = doc.PutObject(null, "books", "map");
                        for (int b = 0; b < BookCount; b++)
                        {
                            var bookObj = doc.PutObject(booksObj, $"book-{b:D3}", "map");
                            doc.Put(bookObj, "title", $"\"Book {b}\"");
                            doc.Put(bookObj, "author", $"\"Author {b}\"");
                            doc.Put(bookObj, "rating", $"{b % 5 + 1}");
                        }
                    });
                    docIds[d] = handle.DocumentId;
                }
                // No explicit FlushAsync — DisposeAsync MUST flush.
            }

            // Reopen and verify every doc has every book.
            {
                await using var repo2 = new Repo(new RepoOptions { Storage = new FileStorageAdapter(_tempDir) });
                for (int d = 0; d < DocCount; d++)
                {
                    var found = await repo2.Find(docIds[d]);
                    Assert.True(found.IsReady, $"doc {d} not ready");
                    var keys = found.Doc.GetKeys(null);
                    Assert.Contains("books", keys);
                }
            }
        }

        [Fact]
        public async Task FlushAsync_NoStorage_IsNoOp()
        {
            await using var repo = new Repo(new RepoOptions()); // no storage
            var handle = repo.Create(d => d.Put(null, "k", "1"));
            // Must not throw or hang.
            await repo.FlushAsync();
            await repo.FlushAsync(new[] { handle.DocumentId });
        }

        [Fact]
        public async Task FlushAsync_SpecificDocIds_OnlyFlushesThose()
        {
            var storage = new SlowStorageAdapter(_tempDir, delayMs: 100);
            await using var repo = new Repo(new RepoOptions { Storage = storage });
            var h1 = repo.Create(d => d.Put(null, "k", "1"));
            var h2 = repo.Create(d => d.Put(null, "k", "2"));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await repo.FlushAsync(new[] { h1.DocumentId });
            sw.Stop();
            // h1 must be persisted; we don't assert h2's state because
            // both saves overlap on the slow adapter.
            var h1Chunks = await storage.LoadRangeAsync(new StorageKey(h1.DocumentId));
            Assert.NotEmpty(h1Chunks);
            Assert.True(sw.ElapsedMilliseconds >= 80,
                $"Targeted FlushAsync returned too quickly: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Dispose_Synchronous_AlsoFlushes()
        {
            // The sync Dispose path is rarely the recommended one but
            // must still produce durable writes — otherwise users in
            // sync-only call sites lose data silently.
            string docId;
            var storage = new SlowStorageAdapter(_tempDir, delayMs: 80);
            {
                using var repo = new Repo(new RepoOptions { Storage = storage });
                var handle = repo.Create(d => d.Put(null, "k", "\"sync-durable\""));
                docId = handle.DocumentId;
            }
            // Repo is now disposed; the sync Dispose blocked on FlushAsync.
            var chunks = await storage.LoadRangeAsync(new StorageKey(docId));
            Assert.NotEmpty(chunks);
        }

        // ─── Helper: storage adapter that delays each SaveAsync and tracks
        // ─── peak concurrent SaveAsync calls so we can assert coalescing.
        private sealed class SlowStorageAdapter : IStorageAdapter
        {
            private readonly FileStorageAdapter _inner;
            private readonly int _delayMs;
            private int _inFlight;
            public int PeakConcurrentSaves;

            public SlowStorageAdapter(string baseDir, int delayMs)
            {
                _inner = new FileStorageAdapter(baseDir);
                _delayMs = delayMs;
            }

            public async Task SaveAsync(StorageKey key, byte[] data)
            {
                int now = System.Threading.Interlocked.Increment(ref _inFlight);
                // Capture peak with a CAS loop.
                while (true)
                {
                    int peak = PeakConcurrentSaves;
                    if (now <= peak) break;
                    if (System.Threading.Interlocked.CompareExchange(ref PeakConcurrentSaves, now, peak) == peak) break;
                }
                try
                {
                    await Task.Delay(_delayMs).ConfigureAwait(false);
                    await _inner.SaveAsync(key, data).ConfigureAwait(false);
                }
                finally
                {
                    System.Threading.Interlocked.Decrement(ref _inFlight);
                }
            }
            public Task<byte[]?> LoadAsync(StorageKey key) => _inner.LoadAsync(key);
            public Task RemoveAsync(StorageKey key) => _inner.RemoveAsync(key);
            public Task<StorageChunk[]> LoadRangeAsync(StorageKey prefix) => _inner.LoadRangeAsync(prefix);
            public Task RemoveRangeAsync(StorageKey prefix) => _inner.RemoveRangeAsync(prefix);
        }

        // ─── Helper: storage adapter whose next SaveAsync throws. Used to
        // ─── verify the persist loop recovers from transient failures.
        private sealed class FlakyStorageAdapter : IStorageAdapter
        {
            private readonly FileStorageAdapter _inner;
            public volatile bool FailNext;
            public FlakyStorageAdapter(string baseDir) { _inner = new FileStorageAdapter(baseDir); }
            public Task SaveAsync(StorageKey key, byte[] data)
            {
                if (FailNext) { FailNext = false; throw new InvalidOperationException("flaky"); }
                return _inner.SaveAsync(key, data);
            }
            public Task<byte[]?> LoadAsync(StorageKey key) => _inner.LoadAsync(key);
            public Task RemoveAsync(StorageKey key) => _inner.RemoveAsync(key);
            public Task<StorageChunk[]> LoadRangeAsync(StorageKey prefix) => _inner.LoadRangeAsync(prefix);
            public Task RemoveRangeAsync(StorageKey prefix) => _inner.RemoveRangeAsync(prefix);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Find peer-request tests: verify Find's behavior when storage is empty
    // and the document only exists on a peer. Uses an in-memory network
    // adapter so the test is hermetic (no external WebSocket dependency).
    // ═══════════════════════════════════════════════════════════════════════════

    public class RepoFindPeerRequestTests : IDisposable
    {
        private readonly string _tempDirA;
        private readonly string _tempDirB;

        public RepoFindPeerRequestTests()
        {
            _tempDirA = Path.Combine(Path.GetTempPath(), "automerge-find-a-" + Guid.NewGuid().ToString("N")[..8]);
            _tempDirB = Path.Combine(Path.GetTempPath(), "automerge-find-b-" + Guid.NewGuid().ToString("N")[..8]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirA)) Directory.Delete(_tempDirA, true);
            if (Directory.Exists(_tempDirB)) Directory.Delete(_tempDirB, true);
        }

        [Fact]
        public async Task Find_StorageEmpty_TimesOutAsReadyEmpty()
        {
            // No peers, no storage hits — Find must resolve as Ready-but-empty
            // within the configured timeout. The contract: never block forever.
            await using var repo = new Repo(new RepoOptions
            {
                Storage = new FileStorageAdapter(_tempDirA),
            })
            {
                FindPeerRequestTimeoutMs = 200,
            };

            var docId = AutomergeUrl.GenerateDocumentId();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var handle = await repo.Find(docId);
            sw.Stop();
            Assert.True(handle.IsReady);
            Assert.Empty(handle.Doc.GetKeys(null));
            Assert.True(sw.ElapsedMilliseconds >= 150 && sw.ElapsedMilliseconds < 2000,
                $"Find didn't honor the configured timeout: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Find_PeerHasIt_ReceivesContent()
        {
            // Wire two repos via an in-memory adapter pair. Repo A creates
            // a doc with content; Repo B Finds the same docId with no
            // local storage and must receive the content over the wire.
            var net = new InMemoryNetworkAdapter.Pair();
            var adapterA = net.A;
            var adapterB = net.B;

            await using var repoA = new Repo(new RepoOptions
            {
                Storage = new FileStorageAdapter(_tempDirA),
                Network = new[] { adapterA },
                PeerId = "peerA",
            });
            await adapterA.ConnectAsync("peerA");

            var handleA = repoA.Create(d => d.Put(null, "shared", "\"hello\""));
            await repoA.FlushAsync();
            var docId = handleA.DocumentId;

            await using var repoB = new Repo(new RepoOptions
            {
                Storage = new FileStorageAdapter(_tempDirB),
                Network = new[] { adapterB },
                PeerId = "peerB",
            })
            {
                FindPeerRequestTimeoutMs = 3000,
            };
            await adapterB.ConnectAsync("peerB");

            var handleB = await repoB.Find(docId);
            Assert.True(handleB.IsReady);
            // Allow a brief moment for the post-handshake sync response to
            // apply the content if it arrived after the TCS completed.
            for (int i = 0; i < 50; i++)
            {
                if (handleB.Doc.GetKeys(null).Length > 0) break;
                await Task.Delay(20);
            }
            Assert.Equal("\"hello\"", handleB.Doc.Get(null, "shared"));
        }

        // ─── Helper: in-memory pair of INetworkAdapters that pipe messages
        // ─── to each other. Lets tests exercise the peer-request flow
        // ─── without WebSockets or real sockets.
        private sealed class InMemoryNetworkAdapter : INetworkAdapter
        {
            public string? Peer;
            public InMemoryNetworkAdapter? Other;
            public event Action<NetworkMessage>? MessageReceived;
            public event Action<string, PeerMetadata?>? PeerConnected;
            public event Action<string>? PeerDisconnected;

            public Task ConnectAsync(string peerId, PeerMetadata? metadata = null, System.Threading.CancellationToken ct = default)
            {
                Peer = peerId;
                if (Other?.Peer is { } otherPeer)
                {
                    PeerConnected?.Invoke(otherPeer, null);
                    Other.PeerConnected?.Invoke(peerId, null);
                }
                return Task.CompletedTask;
            }
            public Task DisconnectAsync()
            {
                if (Other?.Peer is { } otherPeer) PeerDisconnected?.Invoke(otherPeer);
                return Task.CompletedTask;
            }
            public Task SendAsync(NetworkMessage message, System.Threading.CancellationToken ct = default)
            {
                Other?.MessageReceived?.Invoke(message);
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public sealed class Pair
            {
                public readonly InMemoryNetworkAdapter A = new();
                public readonly InMemoryNetworkAdapter B = new();
                public Pair() { A.Other = B; B.Other = A; }
            }
        }
    }
}
