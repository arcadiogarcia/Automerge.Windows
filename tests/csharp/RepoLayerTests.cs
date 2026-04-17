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
            // Give async persist a moment
            await Task.Delay(100);

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
                await Task.Delay(100);
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
}
