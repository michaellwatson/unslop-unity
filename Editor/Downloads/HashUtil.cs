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

        public static bool EqualsHash(string expected, string actual)
        {
            return Normalize(expected) == Normalize(actual);
        }

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
