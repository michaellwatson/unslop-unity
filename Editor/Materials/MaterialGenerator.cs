using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using Unslop.UnityBridge.Editor.Security;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    public sealed class MaterialGenerationResult
    {
        public Dictionary<string, string> MaterialPathsById { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, LockMaterialBinding> Bindings { get; } = new Dictionary<string, LockMaterialBinding>(StringComparer.Ordinal);
        public int ManagedCount => MaterialPathsById.Count;
        public string AdapterPipeline { get; set; }
        public bool AutoMaterialsAvailable { get; set; }
    }

    public sealed class MaterialGenerator
    {
        readonly IRenderPipelineMaterialAdapter _adapter;

        public MaterialGenerator(IRenderPipelineMaterialAdapter adapter = null)
        {
            _adapter = adapter ?? new UrpMaterialAdapter();
        }

        public static IRenderPipelineMaterialAdapter ResolveAdapter(string pipeline = "urp")
        {
            if (string.Equals(pipeline, "hdrp", StringComparison.OrdinalIgnoreCase))
            {
                return new HdrpMaterialAdapterPlaceholder();
            }

            return new UrpMaterialAdapter();
        }

        public MaterialGenerationResult Generate(
            MaterialsManifest materials,
            string stagingAssetPath,
            string installedMaterialsDir)
        {
            materials ??= new MaterialsManifest();
            var result = new MaterialGenerationResult
            {
                AdapterPipeline = _adapter.PipelineId,
                AutoMaterialsAvailable = _adapter.IsAvailable
            };

            if (!_adapter.IsAvailable)
            {
                BridgeLog.Warn(_adapter.UnavailableReason);
                return result;
            }

            ManagedPaths.EnsureDirectory(installedMaterialsDir);
            AssetDatabase.Refresh();

            foreach (var definition in materials.materials ?? Enumerable.Empty<MaterialDefinition>())
            {
                if (string.IsNullOrWhiteSpace(definition.material_id))
                {
                    continue;
                }

                var textures = LoadTextures(definition, stagingAssetPath);
                var material = _adapter.CreateMaterial(definition, textures);
                var matPath = $"{installedMaterialsDir}/{SanitizeFileName(definition.material_id)}.mat";
                EnsureParent(matPath);

                var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing != null)
                {
                    _adapter.ApplyTextures(existing, definition, textures);
                    EditorUtility.SetDirty(existing);
                    result.MaterialPathsById[definition.material_id] = matPath;
                }
                else
                {
                    AssetDatabase.CreateAsset(material, matPath);
                    result.MaterialPathsById[definition.material_id] = matPath;
                }

                var guid = AssetDatabase.AssetPathToGUID(matPath);
                var hash = ComputeManagedStateHash(definition, matPath);
                result.Bindings[definition.material_id] = new LockMaterialBinding
                {
                    mode = "managed",
                    material_guid = guid,
                    managed_state_hash = hash
                };
            }

            AssetDatabase.SaveAssets();
            BridgeLog.Info($"Generated {result.ManagedCount} managed material(s) via {_adapter.PipelineId}.");
            return result;
        }

        public static string ComputeManagedStateHash(MaterialDefinition definition, string materialPath)
        {
            var tex = definition?.textures == null
                ? string.Empty
                : string.Join("|", definition.textures.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
            var payload = $"{definition?.material_id}|{definition?.model}|{tex}|{materialPath}";
            return HashUtil.PrefixSha256(HashUtil.Sha256HexUtf8(payload));
        }

        Dictionary<string, Texture2D> LoadTextures(MaterialDefinition definition, string stagingAssetPath)
        {
            var map = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            if (definition.textures == null)
            {
                return map;
            }

            foreach (var kv in definition.textures)
            {
                if (string.IsNullOrWhiteSpace(kv.Value))
                {
                    continue;
                }

                PackageContentGuard.EnsureSafeRelativePath(kv.Value);
                var assetPath = $"{stagingAssetPath.TrimEnd('/')}/{PackageContentGuard.NormalizePath(kv.Value)}";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null)
                {
                    BridgeLog.Warn($"Texture not found for material {definition.material_id}: {assetPath}");
                    continue;
                }

                map[kv.Key] = texture;
            }

            return map;
        }

        static void EnsureParent(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir))
            {
                ManagedPaths.EnsureDirectory(dir);
            }
        }

        static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
