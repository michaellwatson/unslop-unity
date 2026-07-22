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
            var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            return prefixed ? "sha256:" + hex : hex;
        }

        public static string Sha256Bytes(byte[] data, bool prefixed = true)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data ?? Array.Empty<byte>());
            var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            return prefixed ? "sha256:" + hex : hex;
        }

        public static string Sha256Utf8(string text, bool prefixed = true)
            => Sha256Bytes(Encoding.UTF8.GetBytes(text ?? string.Empty), prefixed);

        /// <summary>Unprefixed hex digest of UTF-8 text.</summary>
        public static string Sha256HexUtf8(string text) => Sha256Utf8(text, prefixed: false);

        /// <summary>Ensure a hash string is in <c>sha256:hex</c> form.</summary>
        public static string PrefixSha256(string hashOrHex)
        {
            if (string.IsNullOrWhiteSpace(hashOrHex))
            {
                return string.Empty;
            }

            var hex = Normalize(hashOrHex);
            return string.IsNullOrEmpty(hex) ? string.Empty : "sha256:" + hex;
        }

        public static string Sha256PrefixedFile(string path) => Sha256File(path, prefixed: true);

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

            return hash.ToLowerInvariant();
        }
    }
}
