using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Unslop.UnityBridge.Editor.Downloads
{
    public static class HashUtil
    {
        public static string Sha256File(string path, bool prefixed = true)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var hex = ToHex(hash);
            return prefixed ? "sha256:" + hex : hex;
        }

        /// <summary>Raw hex SHA-256 of a file (no prefix).</summary>
        public static string Sha256HexFile(string path) => Sha256File(path, prefixed: false);

        /// <summary>Prefixed sha256:hex digest of a file.</summary>
        public static string Sha256PrefixedFile(string path) => Sha256File(path, prefixed: true);

        public static string Sha256Bytes(byte[] data, bool prefixed = true)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data ?? Array.Empty<byte>());
            var hex = ToHex(hash);
            return prefixed ? "sha256:" + hex : hex;
        }

        public static string Sha256Utf8(string text, bool prefixed = true)
            => Sha256Bytes(Encoding.UTF8.GetBytes(text ?? string.Empty), prefixed);

        public static string Prefix(string hexOrPrefixed)
        {
            var hex = Normalize(hexOrPrefixed);
            return string.IsNullOrEmpty(hex) ? string.Empty : "sha256:" + hex;
        }

        /// <summary>Unprefixed hex digest of UTF-8 text.</summary>
        public static string Sha256HexUtf8(string text) => Sha256Utf8(text, prefixed: false);

        /// <summary>Ensure a hash string is in <c>sha256:hex</c> form.</summary>
        public static string PrefixSha256(string hashOrHex) => Prefix(hashOrHex);

        public static bool EqualsHash(string expected, string actual)
        {
            return Normalize(expected) == Normalize(actual);
        }

        public static bool EqualsSha256(string expected, string actual) => EqualsHash(expected, actual);

        public static string Normalize(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return string.Empty;
            }

            hash = hash.Trim();
            if (hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                hash = hash.Substring("sha256:".Length);
            }

            return hash.Replace("-", string.Empty).ToLowerInvariant();
        }

        static string ToHex(byte[] hash) =>
            BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
