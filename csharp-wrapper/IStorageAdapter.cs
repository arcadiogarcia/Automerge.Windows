using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// A storage key is an array of string segments, e.g. ["docId", "snapshot", "hash"].
    /// Matches the JS automerge-repo StorageAdapter key model.
    /// </summary>
    public sealed class StorageKey
    {
        /// <summary>The key segments.</summary>
        public string[] Segments { get; }

        /// <summary>Create a key from segments.</summary>
        public StorageKey(params string[] segments) { Segments = segments; }

        /// <summary>Join segments with a separator for display/debug.</summary>
        public override string ToString() => string.Join("/", Segments);
    }

    /// <summary>
    /// A loaded chunk from storage.
    /// </summary>
    public sealed record StorageChunk(StorageKey Key, byte[] Data);

    /// <summary>
    /// Pluggable persistence interface for the <see cref="Repo"/>.
    /// Matches the JS <c>StorageAdapter</c> abstract class from <c>@automerge/automerge-repo</c>.
    ///
    /// <para>
    /// Implementations store binary chunks keyed by <see cref="StorageKey"/> arrays.
    /// The repo stores documents as a combination of "snapshot" and "incremental" chunks.
    /// </para>
    ///
    /// <para>
    /// This interface is safe for concurrent use: multiple <see cref="Repo"/> instances
    /// may share the same storage adapter (just like in the JS ecosystem).
    /// </para>
    /// </summary>
    public interface IStorageAdapter
    {
        /// <summary>Load a single chunk by its exact key. Returns null if not found.</summary>
        Task<byte[]?> LoadAsync(StorageKey key);

        /// <summary>Save a binary chunk at the given key.</summary>
        Task SaveAsync(StorageKey key, byte[] data);

        /// <summary>Remove the chunk at the given key.</summary>
        Task RemoveAsync(StorageKey key);

        /// <summary>Load all chunks whose key starts with <paramref name="prefix"/>.</summary>
        Task<StorageChunk[]> LoadRangeAsync(StorageKey prefix);

        /// <summary>Remove all chunks whose key starts with <paramref name="prefix"/>.</summary>
        Task RemoveRangeAsync(StorageKey prefix);
    }
}
