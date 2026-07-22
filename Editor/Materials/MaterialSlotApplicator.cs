using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    /// <summary>
    /// Assigns generated managed materials onto Visual prefab renderers after install.
    /// </summary>
    public static class MaterialSlotApplicator
    {
        public static int ApplyToPrefab(
            string visualPrefabPath,
            MaterialsManifest materials,
            IReadOnlyDictionary<string, string> materialPathsById)
        {
            if (string.IsNullOrWhiteSpace(visualPrefabPath) || materialPathsById == null || materialPathsById.Count == 0)
            {
                return 0;
            }

            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(visualPrefabPath);
            if (visual == null)
            {
                BridgeLog.Warn("Material assignment skipped — Visual prefab missing: " + visualPrefabPath);
                return 0;
            }

            var loaded = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in materialPathsById)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(kv.Value);
                if (mat != null)
                {
                    loaded[kv.Key] = mat;
                }
                else
                {
                    BridgeLog.Warn($"Managed material not found at '{kv.Value}' (id={kv.Key})");
                }
            }

            if (loaded.Count == 0)
            {
                BridgeLog.Warn(
                    $"Material assignment skipped — 0/{materialPathsById.Count} materials loaded for {visualPrefabPath}");
                return 0;
            }

            BridgeLog.Info($"Applying {loaded.Count} managed material(s) to {visualPrefabPath}");

            var slots = materials?.slots ?? new List<MaterialSlot>();
            var contents = PrefabUtility.LoadPrefabContents(visualPrefabPath);
            var assigned = 0;
            try
            {
                var renderers = contents.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    BridgeLog.Warn("Material assignment: no renderers under " + visualPrefabPath);
                }

                foreach (var renderer in renderers)
                {
                    assigned += AssignRenderer(renderer, loaded, slots);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, visualPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // Also stamp the wrapper's nested Visual instance if the Asset prefab already exists beside it.
            var prefabsDir = Path.GetDirectoryName(visualPrefabPath)?.Replace('\\', '/');
            var installedRoot = string.IsNullOrEmpty(prefabsDir)
                ? null
                : Path.GetDirectoryName(prefabsDir)?.Replace('\\', '/');
            var assetPrefabPath = !string.IsNullOrEmpty(installedRoot)
                ? ManagedPaths.ResolveWrapperPrefabPath(installedRoot)
                : visualPrefabPath.Replace("/Visual.prefab", "/Asset.prefab")
                    .Replace("_Visual.prefab", ".prefab");
            if (!string.Equals(assetPrefabPath, visualPrefabPath, StringComparison.Ordinal)
                && AssetDatabase.LoadAssetAtPath<GameObject>(assetPrefabPath) != null)
            {
                ApplyToWrapperNestedVisual(assetPrefabPath, loaded, slots);
            }

            AssetDatabase.SaveAssets();
            BridgeLog.Info($"Assigned managed materials on {assigned} renderer slot(s) for {visualPrefabPath}.");
            return assigned;
        }

        static void ApplyToWrapperNestedVisual(
            string assetPrefabPath,
            IReadOnlyDictionary<string, Material> loaded,
            IReadOnlyList<MaterialSlot> slots)
        {
            var contents = PrefabUtility.LoadPrefabContents(assetPrefabPath);
            try
            {
                foreach (var renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    AssignRenderer(renderer, loaded, slots);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, assetPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static int AssignRenderer(
            Renderer renderer,
            IReadOnlyDictionary<string, Material> loaded,
            IReadOnlyList<MaterialSlot> slots)
        {
            if (renderer == null)
            {
                return 0;
            }

            var shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0)
            {
                // FBX with no materials — still apply a single default if we have one.
                if (loaded.Count >= 1)
                {
                    renderer.sharedMaterial = loaded.Values.First();
                    EditorUtility.SetDirty(renderer);
                    return 1;
                }

                return 0;
            }

            var changed = 0;
            var next = (Material[])shared.Clone();
            for (var i = 0; i < next.Length; i++)
            {
                var material = ResolveMaterialForSlot(renderer, i, next[i], loaded, slots);
                if (material == null)
                {
                    continue;
                }

                // Always assign — FBX placeholder materials often share names but are different assets.
                if (next[i] != material)
                {
                    next[i] = material;
                    changed++;
                }
            }

            if (changed > 0)
            {
                renderer.sharedMaterials = next;
                EditorUtility.SetDirty(renderer);
                BridgeLog.Info(
                    $"Assigned material(s) on renderer '{renderer.name}' ({changed} slot(s))");
            }
            else if (loaded.Count == 1)
            {
                // Force single-material apply even if Unity considers refs "equal" after reimport quirks.
                renderer.sharedMaterial = loaded.Values.First();
                EditorUtility.SetDirty(renderer);
                changed = 1;
                BridgeLog.Info($"Force-assigned single managed material on renderer '{renderer.name}'");
            }

            return changed;
        }

        static Material ResolveMaterialForSlot(
            Renderer renderer,
            int slotIndex,
            Material current,
            IReadOnlyDictionary<string, Material> loaded,
            IReadOnlyList<MaterialSlot> slots)
        {
            // 1) Explicit slot mapping by slot_id / display_name vs renderer or current material name.
            foreach (var slot in slots ?? Enumerable.Empty<MaterialSlot>())
            {
                if (string.IsNullOrWhiteSpace(slot.material_id) || !loaded.TryGetValue(slot.material_id, out var mapped))
                {
                    continue;
                }

                if (NameMatches(slot.slot_id, renderer.name)
                    || NameMatches(slot.display_name, renderer.name)
                    || NameMatches(slot.slot_id, current?.name)
                    || NameMatches(slot.display_name, current?.name)
                    || NameMatches(slot.material_id, current?.name))
                {
                    return mapped;
                }
            }

            // 2) Single managed material → apply to every slot.
            if (loaded.Count == 1)
            {
                return loaded.Values.First();
            }

            // 3) Match material_id to existing material name (e.g. mat_main).
            if (current != null)
            {
                foreach (var kv in loaded)
                {
                    if (NameMatches(kv.Key, current.name) || NameMatches(kv.Value.name, current.name))
                    {
                        return kv.Value;
                    }
                }
            }

            // 4) Index fallback when slot count matches material count.
            if (slotIndex < loaded.Count)
            {
                return loaded.Values.ElementAt(slotIndex);
            }

            return null;
        }

        static bool NameMatches(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)
                   || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0
                   || a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
