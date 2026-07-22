using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Diagnostics;

namespace Unslop.UnityBridge.Editor.Locking
{
    public static class LockFileService
    {
        static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new OrderedContractResolver()
        };

        public static UnslopLockFile LoadOrCreate(string projectId, string environment)
        {
            ManagedPaths.EnsureOperationalDirectories();
            if (!File.Exists(ManagedPaths.LockFilePath))
            {
                var created = new UnslopLockFile
                {
                    schema_version = 1,
                    project_id = projectId ?? string.Empty,
                    environment = environment ?? "production",
                    generated_by = $"{BridgePackageInfo.PackageId}@{BridgePackageInfo.Version}"
                };
                Save(created);
                return created;
            }

            var json = File.ReadAllText(ManagedPaths.LockFilePath, Encoding.UTF8);
            var lockFile = JsonConvert.DeserializeObject<UnslopLockFile>(json, SerializerSettings)
                           ?? new UnslopLockFile();
            lockFile.assets ??= new System.Collections.Generic.Dictionary<string, LockAssetEntry>();
            return lockFile;
        }

        public static void Save(UnslopLockFile lockFile)
        {
            if (lockFile == null)
            {
                throw new ArgumentNullException(nameof(lockFile));
            }

            lockFile.generated_by = $"{BridgePackageInfo.PackageId}@{BridgePackageInfo.Version}";
            var json = CanonicalSerialize(lockFile);
            var temp = ManagedPaths.LockFilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(ManagedPaths.LockFilePath))
            {
                File.Replace(temp, ManagedPaths.LockFilePath, ManagedPaths.LockFilePath + ".bak");
            }
            else
            {
                File.Move(temp, ManagedPaths.LockFilePath);
            }
        }

        public static void UpsertAsset(UnslopLockFile lockFile, string assetId, LockAssetEntry entry)
        {
            lockFile.assets[assetId] = entry ?? throw new ArgumentNullException(nameof(entry));
            entry.state_hash = ComputeStateHash(entry);
            Save(lockFile);
        }

        public static AssetLocalMetadata LoadLocalMetadata(string assetId)
        {
            var path = Path.Combine(ManagedPaths.ProjectRoot, ManagedPaths.LocalMetadataPath(assetId).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<AssetLocalMetadata>(File.ReadAllText(path, Encoding.UTF8), SerializerSettings);
        }

        public static void SaveLocalMetadata(AssetLocalMetadata metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            var relative = ManagedPaths.LocalMetadataPath(metadata.asset_id);
            ManagedPaths.EnsureDirectory(Path.GetDirectoryName(relative));
            var path = Path.Combine(ManagedPaths.ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(path, CanonicalSerialize(metadata), new UTF8Encoding(false));
        }

        public static string ComputeStateHash(LockAssetEntry entry)
        {
            var payload = CanonicalSerialize(new
            {
                entry.installed_version_id,
                entry.installed_version_number,
                entry.physical_spec_id,
                entry.wrapper_prefab_guid,
                entry.visual_prefab_guid,
                entry.source_fbx_guid,
                entry.import_profile_hash,
                entry.manifest_sha256,
                pin = entry.pin,
                material_bindings = entry.material_bindings?
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
            });
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return "sha256:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string CanonicalSerialize(object value)
        {
            var token = JToken.FromObject(value, JsonSerializer.Create(SerializerSettings));
            OrderToken(token);
            return token.ToString(Formatting.Indented) + "\n";
        }

        static void OrderToken(JToken token)
        {
            if (token is JObject obj)
            {
                var props = obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                foreach (var p in props)
                {
                    p.Remove();
                }

                foreach (var p in props)
                {
                    OrderToken(p.Value);
                    obj.Add(p);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var child in arr)
                {
                    OrderToken(child);
                }
            }
        }
    }

    sealed class OrderedContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override System.Collections.Generic.IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(
            Type type,
            MemberSerialization memberSerialization)
        {
            return base.CreateProperties(type, memberSerialization)
                .OrderBy(p => p.PropertyName, StringComparer.Ordinal)
                .ToList();
        }
    }
}
