using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Unslop.UnityBridge.Editor.Locking;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Authentication
{
    public sealed class BridgeCredentialStore
    {
        const string FileName = "session.bin";

        [Serializable]
        sealed class SessionPayload
        {
            public string api_key;
            public string updated_at;
        }

        public bool HasApiKey => !string.IsNullOrWhiteSpace(LoadApiKey());

        public string LoadApiKey()
        {
            var payload = ReadPayload();
            return payload?.api_key;
        }

        public void SaveApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Clear();
                return;
            }

            if (!apiKey.StartsWith("usk_", StringComparison.Ordinal))
            {
                throw new ArgumentException("Bridge API key must start with usk_.");
            }

            ManagedPaths.EnsureOperationalDirectories();
            WritePayload(new SessionPayload
            {
                api_key = apiKey.Trim(),
                updated_at = DateTime.UtcNow.ToString("O")
            });
        }

        public void Clear()
        {
            var path = SessionPath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        string SessionPath => Path.Combine(ManagedPaths.AuthDir, FileName);

        SessionPayload ReadPayload()
        {
            var path = SessionPath;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var all = File.ReadAllBytes(path);
                if (all.Length < 16)
                {
                    return null;
                }

                var iv = new byte[16];
                Buffer.BlockCopy(all, 0, iv, 0, 16);
                var cipher = new byte[all.Length - 16];
                Buffer.BlockCopy(all, 16, cipher, 0, cipher.Length);
                var plain = Decrypt(cipher, DeriveKey(), iv);
                return JsonConvert.DeserializeObject<SessionPayload>(Encoding.UTF8.GetString(plain));
            }
            catch (Exception)
            {
                return null;
            }
        }

        void WritePayload(SessionPayload payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            var plain = Encoding.UTF8.GetBytes(json);
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            aes.GenerateIV();
            var cipher = Encrypt(plain, aes.Key, aes.IV);
            var output = new byte[aes.IV.Length + cipher.Length];
            Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
            Buffer.BlockCopy(cipher, 0, output, aes.IV.Length, cipher.Length);
            File.WriteAllBytes(SessionPath, output);
        }

        static byte[] DeriveKey()
        {
            var material = Application.dataPath + "|unslop-bridge-auth-v1|" + Environment.UserName;
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
        }

        static byte[] Encrypt(byte[] plain, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(plain, 0, plain.Length);
        }

        static byte[] Decrypt(byte[] cipher, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        }
    }
}
