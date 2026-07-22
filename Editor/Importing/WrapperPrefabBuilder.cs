using System;
using System.Collections.Generic;
using System.IO;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Importing
{
    public sealed class WrapperPrefabResult
    {
        public string InstalledRoot { get; set; }
        public string VisualPrefabPath { get; set; }
        public string AssetPrefabPath { get; set; }
        public string VisualPrefabGuid { get; set; }
        public string WrapperPrefabGuid { get; set; }
        public string SourceFbxGuid { get; set; }
    }

    /// <summary>
    /// Builds or refreshes stable wrapper prefabs. Existing Asset.prefab roots are updated in place
    /// so scene instances keep their transforms / UserContent.
    /// </summary>
    public static class WrapperPrefabBuilder
    {
        public const string RootName = "UnslopAssetRoot";
        public const string VisualCorrectionName = "VisualCorrection";
        public const string ModelName = "Model";
        public const string UserContentName = "UserContent";
        public const string VisualNestedName = "Visual";

        public static WrapperPrefabResult Build(
            string assetId,
            string versionId,
            string physicalSpecId,
            string modelAssetPath,
            string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                throw new ArgumentException("assetId is required.", nameof(assetId));
            }

            if (string.IsNullOrWhiteSpace(modelAssetPath))
            {
                throw new ArgumentException("modelAssetPath is required.", nameof(modelAssetPath));
            }

            var installedRoot = ManagedPaths.InstalledAssetDir(assetId);
            ManagedPaths.EnsureDirectory(installedRoot);
            ManagedPaths.EnsureDirectory(installedRoot + "/Materials");
            ManagedPaths.EnsureDirectory(installedRoot + "/Prefabs");

            var visualPath = $"{installedRoot}/Prefabs/Visual.prefab";
            var assetPath = $"{installedRoot}/Prefabs/Asset.prefab";

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
            if (modelPrefab == null)
            {
                throw new InvalidOperationException("Could not load model at " + modelAssetPath);
            }

            var sourceFbxGuid = AssetDatabase.AssetPathToGUID(modelAssetPath);
            BuildOrUpdateVisualPrefab(visualPath, modelPrefab, displayName);
            BuildOrUpdateAssetPrefab(assetPath, visualPath, assetId, versionId, physicalSpecId, displayName);

            var wrapperGuid = AssetDatabase.AssetPathToGUID(assetPath);
            var visualGuidFinal = AssetDatabase.AssetPathToGUID(visualPath);
            AssetDatabase.SaveAssets();
            BridgeLog.Info($"Built wrapper prefabs for {assetId} (visual={visualGuidFinal}, wrapper={wrapperGuid}).");

            return new WrapperPrefabResult
            {
                InstalledRoot = installedRoot,
                VisualPrefabPath = visualPath,
                AssetPrefabPath = assetPath,
                VisualPrefabGuid = visualGuidFinal,
                WrapperPrefabGuid = wrapperGuid,
                SourceFbxGuid = sourceFbxGuid
            };
        }

        static void BuildOrUpdateVisualPrefab(string visualPath, GameObject modelPrefab, string displayName)
        {
            var visualName = string.IsNullOrWhiteSpace(displayName) ? "Visual" : displayName + "_Visual";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(visualPath) != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(visualPath);
                try
                {
                    contents.name = visualName;
                    ReplaceChildPrefab(contents.transform, ModelName, modelPrefab, ModelName);
                    PrefabUtility.SaveAsPrefabAsset(contents, visualPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                return;
            }

            var visualRoot = new GameObject(visualName);
            try
            {
                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                modelInstance.name = ModelName;
                modelInstance.transform.SetParent(visualRoot.transform, false);
                PrefabUtility.SaveAsPrefabAsset(visualRoot, visualPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(visualRoot);
            }
        }

        static void BuildOrUpdateAssetPrefab(
            string assetPath,
            string visualPath,
            string assetId,
            string versionId,
            string physicalSpecId,
            string displayName)
        {
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            var desiredRootName = string.IsNullOrWhiteSpace(displayName)
                ? RootName
                : SanitizeHierarchyName(displayName);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(assetPath);
                try
                {
                    // Keep the prefab root transform at identity; scene instances own placement.
                    contents.transform.localPosition = Vector3.zero;
                    contents.transform.localRotation = Quaternion.identity;
                    contents.transform.localScale = Vector3.one;
                    contents.name = desiredRootName;

                    var visualCorrection = EnsureChild(contents.transform, VisualCorrectionName);
                    var modelSlot = EnsureChild(visualCorrection, ModelName);
                    EnsureChild(contents.transform, UserContentName);

                    ReplaceChildPrefab(modelSlot, VisualNestedName, visualPrefab, VisualNestedName);

                    var reference = contents.GetComponent<UnslopAssetReference>()
                                    ?? contents.AddComponent<UnslopAssetReference>();
                    var wrapperGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    reference.Configure(assetId, versionId, physicalSpecId ?? string.Empty, wrapperGuid);
                    EditorUtility.SetDirty(reference);

                    PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                return;
            }

            var wrapperRoot = new GameObject(desiredRootName);
            try
            {
                var visualCorrection = new GameObject(VisualCorrectionName);
                visualCorrection.transform.SetParent(wrapperRoot.transform, false);

                var modelSlot = new GameObject(ModelName);
                modelSlot.transform.SetParent(visualCorrection.transform, false);

                if (visualPrefab != null)
                {
                    var nested = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
                    nested.name = VisualNestedName;
                    nested.transform.SetParent(modelSlot.transform, false);
                }

                var userContent = new GameObject(UserContentName);
                userContent.transform.SetParent(wrapperRoot.transform, false);

                var reference = wrapperRoot.AddComponent<UnslopAssetReference>();
                reference.Configure(assetId, versionId, physicalSpecId ?? string.Empty, string.Empty);
                PrefabUtility.SaveAsPrefabAsset(wrapperRoot, assetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrapperRoot);
            }

            var wrapperGuidFinal = AssetDatabase.AssetPathToGUID(assetPath);
            var stamped = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var reference = stamped.GetComponent<UnslopAssetReference>();
                if (reference != null)
                {
                    reference.Configure(assetId, versionId, physicalSpecId ?? string.Empty, wrapperGuidFinal);
                    EditorUtility.SetDirty(reference);
                    PrefabUtility.SaveAsPrefabAsset(stamped, assetPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(stamped);
            }
        }

        static Transform EnsureChild(Transform parent, string childName)
        {
            var existing = FindDirectChild(parent, childName);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void ReplaceChildPrefab(Transform parent, string childName, GameObject sourcePrefab, string instanceName)
        {
            if (sourcePrefab == null)
            {
                return;
            }

            var existing = FindDirectChild(parent, childName);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var nested = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            nested.name = instanceName;
            // Keep the prefab's native localScale (FBX file-scale / unit conversion often lives here).
            var nativeScale = nested.transform.localScale;
            nested.transform.SetParent(parent, false);
            nested.transform.localPosition = Vector3.zero;
            nested.transform.localRotation = Quaternion.identity;
            nested.transform.localScale = nativeScale;
            BridgeLog.Info(
                $"Nested '{instanceName}' under '{parent.name}' preserving localScale={nativeScale} " +
                $"(lossy={nested.transform.lossyScale})");
        }

        static Transform FindDirectChild(Transform parent, string childName)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        static string SanitizeHierarchyName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? RootName : name.Trim();
        }
    }

    /// <summary>
    /// Captures and restores scene placement for Unslop wrappers across version installs.
    /// </summary>
    public static class SceneInstancePosePreserver
    {
        public sealed class PoseSnapshot
        {
            public int InstanceId;
            public string ScenePath;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public Vector3 WorldPosition;
            public Quaternion WorldRotation;
            public bool HadParent;
        }

        public static List<PoseSnapshot> Capture(string assetId)
        {
            var poses = new List<PoseSnapshot>();
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return poses;
            }

            foreach (var reference in UnityEngine.Object.FindObjectsByType<UnslopAssetReference>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (reference == null
                    || !string.Equals(reference.AssetId, assetId, StringComparison.Ordinal))
                {
                    continue;
                }

                var t = reference.transform;
                poses.Add(new PoseSnapshot
                {
                    InstanceId = reference.GetInstanceID(),
                    ScenePath = reference.gameObject.scene.path,
                    LocalPosition = t.localPosition,
                    LocalRotation = t.localRotation,
                    LocalScale = t.localScale,
                    WorldPosition = t.position,
                    WorldRotation = t.rotation,
                    HadParent = t.parent != null
                });
            }

            return poses;
        }

        public static int Restore(string assetId, IReadOnlyList<PoseSnapshot> poses)
        {
            if (poses == null || poses.Count == 0 || string.IsNullOrWhiteSpace(assetId))
            {
                return 0;
            }

            var restored = 0;
            var remaining = new List<PoseSnapshot>(poses);
            foreach (var reference in UnityEngine.Object.FindObjectsByType<UnslopAssetReference>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (reference == null
                    || !string.Equals(reference.AssetId, assetId, StringComparison.Ordinal))
                {
                    continue;
                }

                PoseSnapshot match = null;
                for (var i = 0; i < remaining.Count; i++)
                {
                    if (remaining[i].InstanceId == reference.GetInstanceID()
                        || string.Equals(remaining[i].ScenePath, reference.gameObject.scene.path, StringComparison.Ordinal))
                    {
                        match = remaining[i];
                        remaining.RemoveAt(i);
                        break;
                    }
                }

                if (match == null && remaining.Count > 0)
                {
                    match = remaining[0];
                    remaining.RemoveAt(0);
                }

                if (match == null)
                {
                    continue;
                }

                var t = reference.transform;
                Undo.RecordObject(t, "Unslop Preserve Scene Pose");
                if (match.HadParent)
                {
                    t.localPosition = match.LocalPosition;
                    t.localRotation = match.LocalRotation;
                    t.localScale = match.LocalScale;
                }
                else
                {
                    t.SetPositionAndRotation(match.WorldPosition, match.WorldRotation);
                    t.localScale = match.LocalScale;
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(reference);
                EditorUtility.SetDirty(reference.gameObject);
                if (!string.IsNullOrEmpty(reference.gameObject.scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(reference.gameObject.scene);
                }

                restored++;
            }

            if (restored > 0)
            {
                BridgeLog.Info($"Restored scene pose for {restored} instance(s) of {assetId}.");
            }

            return restored;
        }
    }
}
