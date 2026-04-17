using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// A <see cref="IStorageAdapter"/> that stores chunks as files on the local filesystem.
    /// Equivalent to the JS <c>NodeFSStorageAdapter</c> from <c>@automerge/automerge-repo-storage-nodefs</c>.
    ///
    /// <para>
    /// Keys are mapped to file paths: each segment becomes a directory, and the final
    /// segment is the filename. Binary data is stored as-is.
    /// </para>
    ///
    /// <para>
    /// This adapter is safe for concurrent use from multiple processes (following
    /// the same concurrency model as the JS adapter).
    /// </para>
    /// </summary>
    public sealed class FileStorageAdapter : IStorageAdapter
    {
        private readonly string _baseDir;

        /// <summary>
        /// Create a file-based storage adapter.
        /// </summary>
        /// <param name="baseDirectory">
        /// Root directory for storage. Created if it doesn't exist.
        /// Defaults to <c>./automerge-repo-data</c> in the current directory.
        /// </param>
        public FileStorageAdapter(string? baseDirectory = null)
        {
            _baseDir = baseDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "automerge-repo-data");
            Directory.CreateDirectory(_baseDir);
        }

        /// <inheritdoc />
        public Task<byte[]?> LoadAsync(StorageKey key)
        {
            var path = KeyToPath(key);
            if (!File.Exists(path)) return Task.FromResult<byte[]?>(null);
            return Task.FromResult<byte[]?>(File.ReadAllBytes(path));
        }

        /// <inheritdoc />
        public Task SaveAsync(StorageKey key, byte[] data)
        {
            var path = KeyToPath(key);
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, data);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveAsync(StorageKey key)
        {
            var path = KeyToPath(key);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<StorageChunk[]> LoadRangeAsync(StorageKey prefix)
        {
            var dir = KeyToPath(prefix);
            if (!Directory.Exists(dir))
                return Task.FromResult(Array.Empty<StorageChunk>());

            var results = new List<StorageChunk>();
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(dir, file);
                var segments = prefix.Segments
                    .Concat(relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .ToArray();
                var data = File.ReadAllBytes(file);
                results.Add(new StorageChunk(new StorageKey(segments), data));
            }
            return Task.FromResult(results.ToArray());
        }

        /// <inheritdoc />
        public Task RemoveRangeAsync(StorageKey prefix)
        {
            var dir = KeyToPath(prefix);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            return Task.CompletedTask;
        }

        private string KeyToPath(StorageKey key)
        {
            // Sanitize segments to be filesystem-safe
            var safeParts = key.Segments.Select(SanitizeSegment).ToArray();
            return Path.Combine(_baseDir, Path.Combine(safeParts));
        }

        private static string SanitizeSegment(string segment)
        {
            // Replace invalid filename chars with underscore
            var invalid = Path.GetInvalidFileNameChars();
            var chars = segment.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
