using System;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Importing
{
    /// <summary>
    /// After a canonical_scale_bake install, VisualCorrection must return to identity
    /// (scale is baked into the mesh). World placement of scene instances is preserved.
    /// </summary>
    public static class VisualCorrectionReset
    {
        public static bool IsCanonicalScaleBake(string pipelineOrigin)
        {
            if (string.IsNullOrWhiteSpace(pipelineOrigin))
            {
                return false;
            }

            var normalized = pipelineOrigin.Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .ToLowerInvariant();
            return normalized == "canonical_scale_bake"
                   || normalized.Contains("canonical_scale_bake");
        }

        /// <summary>
        /// Reset VisualCorrection on the Asset prefab and all matching scene instances.
        /// Scene instances keep their world anchor (bottom-centre of renderer bounds).
        /// </summary>
        public static int ApplyForCanonicalBake(string assetId, string assetPrefabPath)
        {
            ResetOnPrefab(assetPrefabPath);
            return ResetOnSceneInstances(assetId);
        }

        public static void ResetOnPrefab(string assetPrefabPath)
        {
            if (string.IsNullOrWhiteSpace(assetPrefabPath)
                || AssetDatabase.LoadAssetAtPath<GameObject>(assetPrefabPath) == null)
            {
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(assetPrefabPath);
            try
            {
                var visual = Scale.ScaleMeasurementService.FindVisualCorrection(contents);
                if (visual == null)
                {
                    return;
                }

                if (ApproximatelyOne(visual.localScale))
                {
                    BridgeLog.Info($"VisualCorrection already 1,1,1 on prefab {assetPrefabPath}");
                    return;
                }

                BridgeLog.Info(
                    $"canonical_scale_bake: resetting prefab VisualCorrection {visual.localScale} → (1,1,1) on {assetPrefabPath}");
                visual.localScale = Vector3.one;
                EditorUtility.SetDirty(visual);
                PrefabUtility.SaveAsPrefabAsset(contents, assetPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        public static int ResetOnSceneInstances(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return 0;
            }

            var count = 0;
            foreach (var reference in UnityEngine.Object.FindObjectsByType<UnslopAssetReference>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (reference == null
                    || !string.Equals(reference.AssetId, assetId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (ResetInstanceKeepingWorldAnchor(reference.gameObject))
                {
                    count++;
                }
            }

            if (count > 0)
            {
                BridgeLog.Info(
                    $"canonical_scale_bake: reset VisualCorrection to 1,1,1 on {count} scene instance(s) (world anchor preserved).");
            }

            return count;
        }

        /// <summary>
        /// Sets VisualCorrection to identity while keeping the bottom-centre of the mesh
        /// in the same world place so the asset does not appear to jump.
        /// </summary>
        public static bool ResetInstanceKeepingWorldAnchor(GameObject wrapperRoot)
        {
            if (wrapperRoot == null)
            {
                return false;
            }

            var visual = Scale.ScaleMeasurementService.FindVisualCorrection(wrapperRoot);
            if (visual == null)
            {
                return false;
            }

            if (ApproximatelyOne(visual.localScale))
            {
                return false;
            }

            var before = CaptureBottomCentre(wrapperRoot);
            var previous = visual.localScale;
            Undo.RecordObject(visual, "Unslop Reset VisualCorrection");
            Undo.RecordObject(wrapperRoot.transform, "Unslop Preserve Bake Placement");

            visual.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);

            if (before.HasValue)
            {
                var after = CaptureBottomCentre(wrapperRoot);
                if (after.HasValue)
                {
                    var delta = before.Value - after.Value;
                    if (delta.sqrMagnitude > 1e-8f)
                    {
                        wrapperRoot.transform.position += delta;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(wrapperRoot.transform);
                    }
                }
            }

            EditorUtility.SetDirty(visual);
            EditorUtility.SetDirty(wrapperRoot);
            if (!string.IsNullOrEmpty(wrapperRoot.scene.path))
            {
                EditorSceneManager.MarkSceneDirty(wrapperRoot.scene);
            }

            BridgeLog.Info(
                $"canonical_scale_bake: scene '{wrapperRoot.name}' VisualCorrection {previous} → (1,1,1); rootPos={wrapperRoot.transform.position}");
            return true;
        }

        static Vector3? CaptureBottomCentre(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return root.transform.position;
            }

            Bounds bounds = default;
            var init = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!init)
                {
                    bounds = renderer.bounds;
                    init = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!init)
            {
                return root.transform.position;
            }

            // Bottom-centre: stable floor contact for static props.
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static bool ApproximatelyOne(Vector3 scale) =>
            Mathf.Abs(scale.x - 1f) < 0.0001f
            && Mathf.Abs(scale.y - 1f) < 0.0001f
            && Mathf.Abs(scale.z - 1f) < 0.0001f;
    }
}
