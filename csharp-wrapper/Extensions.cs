using System;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Automerge.Windows
{
    /// <summary>
    /// Exception thrown when a native Automerge operation fails.
    /// </summary>
    public sealed class AutomergeNativeException : Exception
    {
        public AutomergeNativeException(string message)
            : base(message) { }
    }

    /// <summary>
    /// Extension methods and utilities for working with Automerge documents.
    /// </summary>
    public static class AutomergeExtensions
    {
        /// <summary>
        /// Deserialises the document root to a <see cref="JsonElement"/>.
        /// </summary>
        public static JsonElement GetRootJson(this Document document)
        {
            ArgumentNullException.ThrowIfNull(document);
            var json = document.GetValue("[]");
            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        /// <summary>
        /// Deserialises a document value at <paramref name="pathJson"/> to
        /// <typeparamref name="T"/> using <see cref="System.Text.Json"/>.
        /// </summary>
        public static T? GetValue<T>(this Document document, string pathJson)
        {
            ArgumentNullException.ThrowIfNull(document);
            var json = document.GetValue(pathJson);
            return JsonSerializer.Deserialize<T>(json);
        }

        /// <summary>
        /// Convenience overload: set a single key in the document root.
        /// </summary>
        public static void Set(this Document document, string key, string value)
        {
            ArgumentNullException.ThrowIfNull(document);
            var escaped = JsonSerializer.Serialize(value);
            document.PutJsonRoot($"{{\"{EscapeKey(key)}\":{escaped}}}");
        }

        /// <summary>
        /// Convenience overload: set a single key in the document root.
        /// </summary>
        public static void Set(this Document document, string key, long value)
        {
            ArgumentNullException.ThrowIfNull(document);
            document.PutJsonRoot($"{{\"{EscapeKey(key)}\":{value}}}");
        }

        /// <summary>
        /// Convenience overload: set a single key in the document root.
        /// </summary>
        public static void Set(this Document document, string key, double value)
        {
            ArgumentNullException.ThrowIfNull(document);
            document.PutJsonRoot($"{{\"{EscapeKey(key)}\":{value}}}");
        }

        /// <summary>
        /// Convenience overload: set a single key in the document root.
        /// </summary>
        public static void Set(this Document document, string key, bool value)
        {
            ArgumentNullException.ThrowIfNull(document);
            document.PutJsonRoot($"{{\"{EscapeKey(key)}\":{(value ? "true" : "false")}}}");
        }

        /// <summary>
        /// Run the sync protocol between <paramref name="docA"/> and
        /// <paramref name="docB"/> in memory until they converge.
        ///
        /// This is a convenience utility for testing and local merges.
        /// In production, each side would call
        /// <see cref="SyncState.GenerateSyncMessage"/> /
        /// <see cref="SyncState.ReceiveSyncMessage"/> over a transport.
        /// </summary>
        public static void SyncInMemory(Document docA, Document docB,
                                        int maxRounds = 20)
        {
            ArgumentNullException.ThrowIfNull(docA);
            ArgumentNullException.ThrowIfNull(docB);

            using var ssA = new SyncState();
            using var ssB = new SyncState();

            for (int i = 0; i < maxRounds; i++)
            {
                bool progress = false;

                var msgAB = ssA.GenerateSyncMessage(docA);
                if (msgAB.Length > 0)
                {
                    ssB.ReceiveSyncMessage(docB, msgAB);
                    progress = true;
                }

                var msgBA = ssB.GenerateSyncMessage(docB);
                if (msgBA.Length > 0)
                {
                    ssA.ReceiveSyncMessage(docA, msgBA);
                    progress = true;
                }

                if (!progress) break;
            }
        }

        private static string EscapeKey(string key) =>
            key.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Internal extension to expose <see cref="Document.ThrowIfDisposed"/>.
    /// </summary>
    internal static class DocumentInternalExtensions
    {
        internal static void ThrowIfDisposedInternal(this Document doc)
        {
            // Trigger the disposed check by reading the handle.
            _ = doc.Handle;
        }
    }
}
