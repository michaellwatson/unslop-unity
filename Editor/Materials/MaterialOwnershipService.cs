using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    public enum MaterialResolution
    {
        AcceptRemote,
        KeepLocal,
        TexturesOnly,
        PropertiesOnly,
        RemapToProjectMaterial
    }

    public sealed class MaterialConflict
    {
        public string SlotId { get; set; }
        public string MaterialId { get; set; }
        public string Mode { get; set; }
        public string MaterialGuid { get; set; }
        public string MaterialPath { get; set; }
        public string ExpectedStateHash { get; set; }
        public string CurrentStateHash { get; set; }
        public bool IsLocallyModified { get; set; }
        public bool IsLocalOverride { get; set; }
        public bool IsOrphan { get; set; }
        public bool IsRemovedFromRemote { get; set; }
        public MaterialResolution SuggestedResolution { get; set; } = MaterialResolution.KeepLocal;
    }

    public sealed class MaterialOwnershipReport
    {
        public string AssetId { get; set; }
        public List<MaterialConflict> Conflicts { get; } = new List<MaterialConflict>();
        public List<string> QuarantinedPaths { get; } = new List<string>();
    }

    /// <summary>
    /// Managed-state hashes, local-override detection, and conflict resolutions.
    /// </summary>
    public sealed class MaterialOwnershipService
    {
        public MaterialOwnershipReport Analyse(string assetId, LockAssetEntry entry, MaterialsManifest remoteMaterials = null)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_material_resolution_v1"))
            {
                throw new InvalidOperationException("unity_bridge_material_resolution_v1 is off.");
            }

            var report = new MaterialOwnershipReport { AssetId = assetId };
            entry ??= new LockAssetEntry();
            var bindings = entry.material_bindings ?? new Dictionary<string, LockMaterialBinding>();
            var remoteSlots = (remoteMaterials?.slots ?? new List<MaterialSlot>())
                .ToDictionary(s => s.slot_id ?? s.material_id ?? string.Empty, s => s, StringComparer.Ordinal);
            var remoteMats = (remoteMaterials?.materials ?? new List<MaterialDefinition>())
                .ToDictionary(m => m.material_id ?? string.Empty, m => m, StringComparer.Ordinal);

            foreach (var kv in bindings)
            {
                var slotId = kv.Key;
                var binding = kv.Value ?? new LockMaterialBinding();
                var path = string.IsNullOrEmpty(binding.material_guid)
                    ? null
                    : AssetDatabase.GUIDToAssetPath(binding.material_guid);
                var material = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Material>(path);

                var currentHash = material != null ? CaptureMaterialStateHash(material) : null;
                var isOverride = string.Equals(binding.mode, "local_override", StringComparison.OrdinalIgnoreCase);
                var modified = !isOverride
                               && !string.IsNullOrEmpty(binding.managed_state_hash)
                               && !HashUtil.EqualsHash(binding.managed_state_hash, currentHash);

                var removed = remoteMaterials != null && !remoteSlots.ContainsKey(slotId) && !remoteMats.ContainsKey(slotId);
                var conflict = new MaterialConflict
                {
                    SlotId = slotId,
                    MaterialId = slotId,
                    Mode = binding.mode,
                    MaterialGuid = binding.material_guid,
                    MaterialPath = path,
                    ExpectedStateHash = binding.managed_state_hash,
                    CurrentStateHash = currentHash,
                    IsLocalOverride = isOverride,
                    IsLocallyModified = modified,
                    IsRemovedFromRemote = removed,
                    IsOrphan = material == null && !string.IsNullOrEmpty(binding.material_guid),
                    SuggestedResolution = isOverride || modified
                        ? MaterialResolution.KeepLocal
                        : MaterialResolution.AcceptRemote
                };

                if (conflict.IsLocallyModified || conflict.IsLocalOverride || conflict.IsOrphan || conflict.IsRemovedFromRemote)
                {
                    report.Conflicts.Add(conflict);
                }
            }

            if (remoteMaterials?.slots != null)
            {
                foreach (var slot in remoteMaterials.slots)
                {
                    var key = slot.slot_id ?? slot.material_id;
                    if (string.IsNullOrEmpty(key) || bindings.ContainsKey(key))
                    {
                        continue;
                    }

                    report.Conflicts.Add(new MaterialConflict
                    {
                        SlotId = key,
                        MaterialId = slot.material_id,
                        Mode = "managed",
                        IsRemovedFromRemote = false,
                        SuggestedResolution = MaterialResolution.AcceptRemote
                    });
                }
            }

            return report;
        }

        public void ApplyResolution(
            string assetId,
            LockAssetEntry entry,
            MaterialConflict conflict,
            MaterialResolution resolution,
            MaterialsManifest remoteMaterials = null,
            string remapMaterialGuid = null)
        {
            if (entry?.material_bindings == null || conflict == null)
            {
                return;
            }

            if (!entry.material_bindings.TryGetValue(conflict.SlotId, out var binding))
            {
                binding = new LockMaterialBinding { mode = "managed" };
                entry.material_bindings[conflict.SlotId] = binding;
            }

            switch (resolution)
            {
                case MaterialResolution.KeepLocal:
                    binding.mode = "local_override";
                    if (!string.IsNullOrEmpty(conflict.MaterialPath))
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(conflict.MaterialPath);
                        binding.managed_state_hash = mat != null ? CaptureMaterialStateHash(mat) : binding.managed_state_hash;
                        binding.material_guid = conflict.MaterialGuid;
                    }

                    BridgeLog.Info($"Material {conflict.SlotId}: keep local (local_override).");
                    break;

                case MaterialResolution.AcceptRemote:
                    binding.mode = "managed";
                    ApplyRemoteTexturesAndProperties(conflict, remoteMaterials, texturesOnly: false, propertiesOnly: false);
                    RefreshBindingHash(binding, conflict);
                    BridgeLog.Info($"Material {conflict.SlotId}: accept remote.");
                    break;

                case MaterialResolution.TexturesOnly:
                    binding.mode = "managed";
                    ApplyRemoteTexturesAndProperties(conflict, remoteMaterials, texturesOnly: true, propertiesOnly: false);
                    RefreshBindingHash(binding, conflict);
                    BridgeLog.Info($"Material {conflict.SlotId}: textures only from remote.");
                    break;

                case MaterialResolution.PropertiesOnly:
                    binding.mode = "managed";
                    ApplyRemoteTexturesAndProperties(conflict, remoteMaterials, texturesOnly: false, propertiesOnly: true);
                    RefreshBindingHash(binding, conflict);
                    BridgeLog.Info($"Material {conflict.SlotId}: properties only from remote.");
                    break;

                case MaterialResolution.RemapToProjectMaterial:
                    if (string.IsNullOrEmpty(remapMaterialGuid))
                    {
                        throw new ArgumentException("remapMaterialGuid is required for RemapToProjectMaterial.");
                    }

                    binding.mode = "local_override";
                    binding.material_guid = remapMaterialGuid;
                    var remapPath = AssetDatabase.GUIDToAssetPath(remapMaterialGuid);
                    var remapMat = AssetDatabase.LoadAssetAtPath<Material>(remapPath);
                    binding.managed_state_hash = remapMat != null ? CaptureMaterialStateHash(remapMat) : null;
                    BridgeLog.Info($"Material {conflict.SlotId}: remapped to {remapPath}.");
                    break;
            }

            var settings = Services.BridgeServices.Settings;
            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            LockFileService.UpsertAsset(lockFile, assetId, entry);
        }

        public IReadOnlyList<string> QuarantineRemovedOrOrphans(string assetId, MaterialOwnershipReport report)
        {
            var quarantined = new List<string>();
            if (report == null)
            {
                return quarantined;
            }

            var quarantineRoot = $"{ManagedPaths.InstalledAssetDir(assetId)}/Materials/_Quarantine";
            ManagedPaths.EnsureDirectory(quarantineRoot);

            foreach (var conflict in report.Conflicts.Where(c => c.IsOrphan || c.IsRemovedFromRemote))
            {
                if (string.IsNullOrEmpty(conflict.MaterialPath) || !File.Exists(ToFull(conflict.MaterialPath)))
                {
                    continue;
                }

                var fileName = Path.GetFileName(conflict.MaterialPath);
                var dest = $"{quarantineRoot}/{fileName}";
                AssetDatabase.MoveAsset(conflict.MaterialPath, dest);
                quarantined.Add(dest);
                report.QuarantinedPaths.Add(dest);
            }

            if (quarantined.Count > 0)
            {
                AssetDatabase.SaveAssets();
                BridgeLog.Info($"Quarantined {quarantined.Count} material(s) for {assetId}.");
            }

            return quarantined;
        }

        public static string CaptureMaterialStateHash(Material material)
        {
            if (material == null)
            {
                return string.Empty;
            }

            var props = new List<string>
            {
                "shader=" + (material.shader != null ? material.shader.name : string.Empty)
            };

            var count = material.shader != null ? material.shader.GetPropertyCount() : 0;
            for (var i = 0; i < count; i++)
            {
                var name = material.shader.GetPropertyName(i);
                var type = material.shader.GetPropertyType(i);
                switch (type)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        props.Add(name + "=" + material.GetColor(name));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        props.Add(name + "=" + material.GetFloat(name).ToString("R"));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        var tex = material.GetTexture(name);
                        props.Add(name + "=" + (tex != null ? AssetDatabase.GetAssetPath(tex) : string.Empty));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        props.Add(name + "=" + material.GetVector(name));
                        break;
                }
            }

            props.Sort(StringComparer.Ordinal);
            return HashUtil.Sha256Utf8(string.Join("\n", props));
        }

        static void RefreshBindingHash(LockMaterialBinding binding, MaterialConflict conflict)
        {
            if (string.IsNullOrEmpty(conflict.MaterialPath))
            {
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(conflict.MaterialPath);
            if (mat != null)
            {
                binding.material_guid = AssetDatabase.AssetPathToGUID(conflict.MaterialPath);
                binding.managed_state_hash = CaptureMaterialStateHash(mat);
            }
        }

        static void ApplyRemoteTexturesAndProperties(
            MaterialConflict conflict,
            MaterialsManifest remoteMaterials,
            bool texturesOnly,
            bool propertiesOnly)
        {
            if (string.IsNullOrEmpty(conflict.MaterialPath) || remoteMaterials == null)
            {
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(conflict.MaterialPath);
            if (material == null)
            {
                return;
            }

            var definition = remoteMaterials.materials?.FirstOrDefault(m =>
                string.Equals(m.material_id, conflict.MaterialId, StringComparison.Ordinal)
                || string.Equals(m.material_id, conflict.SlotId, StringComparison.Ordinal));
            if (definition == null)
            {
                return;
            }

            var adapter = MaterialGenerator.ResolveAdapter();
            if (!adapter.IsAvailable)
            {
                return;
            }

            var textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            if (!propertiesOnly && definition.textures != null)
            {
                foreach (var kv in definition.textures)
                {
                    // Prefer installed textures folder
                    var installedRoot = Path.GetDirectoryName(conflict.MaterialPath)?.Replace('\\', '/');
                    var candidate = installedRoot != null
                        ? $"{installedRoot.Replace("/Materials", "")}/Textures/{Path.GetFileName(kv.Value)}"
                        : null;
                    var tex = !string.IsNullOrEmpty(candidate)
                        ? AssetDatabase.LoadAssetAtPath<Texture2D>(candidate)
                        : null;
                    if (tex != null)
                    {
                        textures[kv.Key] = tex;
                    }
                }
            }

            if (texturesOnly)
            {
                adapter.ApplyTextures(material, definition, textures);
            }
            else if (propertiesOnly)
            {
                // Re-apply via create path properties only: keep textures, reset metallic/smoothness defaults.
                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", 0f);
                }

                if (material.HasProperty("_Smoothness") && !material.HasProperty("_MetallicGlossMap"))
                {
                    material.SetFloat("_Smoothness", 0.5f);
                }
            }
            else
            {
                adapter.ApplyTextures(material, definition, textures);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        static string ToFull(string assetPath) =>
            Path.Combine(ManagedPaths.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
