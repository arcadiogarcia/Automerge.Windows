using System;
using System.Numerics;
using System.Security.Cryptography;

namespace Automerge.Windows
{
    /// <summary>
    /// Utilities for working with Automerge URLs (<c>automerge:&lt;base58check&gt;</c>).
    /// </summary>
    public static class AutomergeUrl
    {
        /// <summary>The URL scheme prefix.</summary>
        public const string Prefix = "automerge:";

        private static readonly string B58Alphabet =
            "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        /// <summary>
        /// Generate a new random Automerge URL with a fresh UUID document ID.
        /// </summary>
        /// <returns>A string like <c>"automerge:AMbKhQJnDV5RicD7oS1h1wceMgG"</c>.</returns>
        public static string Generate()
        {
            return Prefix + GenerateDocumentId();
        }

        /// <summary>
        /// Generate a new base58check-encoded document ID (without the <c>automerge:</c> prefix).
        /// </summary>
        public static string GenerateDocumentId()
        {
            var uid = Guid.NewGuid().ToByteArray();
            return Base58CheckEncode(uid);
        }

        /// <summary>
        /// Parse an Automerge URL and return the document ID portion.
        /// </summary>
        /// <param name="url">A string like <c>"automerge:AMbKhQJnDV5RicD7oS1h1wceMgG"</c>.</param>
        /// <returns>The document ID (base58check-encoded).</returns>
        /// <exception cref="ArgumentException">If the URL is not valid.</exception>
        public static string Parse(string url)
        {
            if (!IsValid(url))
                throw new ArgumentException($"Invalid Automerge URL: '{url}'", nameof(url));
            return url[Prefix.Length..];
        }

        /// <summary>
        /// Check whether a string is a valid Automerge URL.
        /// </summary>
        public static bool IsValid(string? url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            var docId = url[Prefix.Length..];
            return IsValidDocumentId(docId);
        }

        /// <summary>
        /// Check whether a string is a valid base58check-encoded document ID.
        /// </summary>
        public static bool IsValidDocumentId(string? docId)
        {
            if (string.IsNullOrEmpty(docId)) return false;
            // Try to decode and verify checksum
            var bytes = Base58Decode(docId);
            if (bytes == null || bytes.Length < 5) return false;
            // Last 4 bytes are the checksum
            var payload = bytes.AsSpan(0, bytes.Length - 4);
            var checksum = bytes.AsSpan(bytes.Length - 4);
            Span<byte> h1 = stackalloc byte[32];
            Span<byte> h2 = stackalloc byte[32];
            SHA256.TryHashData(payload, h1, out _);
            SHA256.TryHashData(h1, h2, out _);
            return h2[..4].SequenceEqual(checksum);
        }

        /// <summary>
        /// Convert a document ID to an Automerge URL by adding the <c>automerge:</c> prefix.
        /// </summary>
        public static string Stringify(string documentId)
        {
            return Prefix + documentId;
        }

        /// <summary>
        /// Convert a binary document ID (16-byte UUID) to a base58check-encoded string.
        /// </summary>
        public static string DocumentIdFromBinary(ReadOnlySpan<byte> binary)
        {
            return Base58CheckEncode(binary.ToArray());
        }

        /// <summary>
        /// Decode a base58check document ID back to binary (16-byte UUID).
        /// Returns null if the ID is invalid.
        /// </summary>
        public static byte[]? DocumentIdToBinary(string documentId)
        {
            var bytes = Base58Decode(documentId);
            if (bytes == null || bytes.Length < 5) return null;
            // Verify checksum
            var payload = bytes.AsSpan(0, bytes.Length - 4);
            var checksum = bytes.AsSpan(bytes.Length - 4);
            Span<byte> h1 = stackalloc byte[32];
            Span<byte> h2 = stackalloc byte[32];
            SHA256.TryHashData(payload, h1, out _);
            SHA256.TryHashData(h1, h2, out _);
            if (!h2[..4].SequenceEqual(checksum)) return null;
            return payload.ToArray();
        }

        // ─── Internal base58check helpers ─────────────────────────────────────

        internal static string Base58CheckEncode(byte[] payload)
        {
            Span<byte> h1 = stackalloc byte[32];
            Span<byte> h2 = stackalloc byte[32];
            SHA256.TryHashData(payload, h1, out _);
            SHA256.TryHashData(h1, h2, out _);
            var full = new byte[payload.Length + 4];
            payload.CopyTo(full, 0);
            h2[..4].CopyTo(full.AsSpan(payload.Length));
            return Base58Encode(full);
        }

        private static string Base58Encode(byte[] data)
        {
            var sb = new System.Text.StringBuilder();
            var num = new BigInteger(data, isUnsigned: true, isBigEndian: true);
            var b58 = new BigInteger(58);
            while (num > BigInteger.Zero)
            {
                num = BigInteger.DivRem(num, b58, out var rem);
                sb.Insert(0, B58Alphabet[(int)rem]);
            }
            for (int i = 0; i < data.Length && data[i] == 0; i++)
                sb.Insert(0, '1');
            return sb.ToString();
        }

        private static byte[]? Base58Decode(string s)
        {
            try
            {
                var num = BigInteger.Zero;
                var b58 = new BigInteger(58);
                foreach (char c in s)
                {
                    int idx = B58Alphabet.IndexOf(c);
                    if (idx < 0) return null;
                    num = num * b58 + idx;
                }
                var bytes = num.ToByteArray(isUnsigned: true, isBigEndian: true);
                // Count leading '1's → leading zero bytes
                int leadingZeros = 0;
                foreach (char c in s) { if (c == '1') leadingZeros++; else break; }
                if (leadingZeros > 0)
                {
                    var padded = new byte[leadingZeros + bytes.Length];
                    bytes.CopyTo(padded, leadingZeros);
                    return padded;
                }
                return bytes;
            }
            catch { return null; }
        }
    }
}
