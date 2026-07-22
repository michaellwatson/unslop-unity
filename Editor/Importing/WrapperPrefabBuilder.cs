using System;
using System.IO;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using UnityEditor;
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

    public static class WrapperPrefabBuilder
    {
        public const string RootName = "UnslopAssetRoot";
        public const string VisualCorrectionName = "VisualCorrection";
        public const string ModelName = "Model";
        public const string UserContentName = "UserContent";

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
            PreserveGuidMeta(visualPath);
            PreserveGuidMeta(assetPath);

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
            if (modelPrefab == null)
            {
                throw new InvalidOperationException("Could not load model at " + modelAssetPath);
            }

            var sourceFbxGuid = AssetDatabase.AssetPathToGUID(modelAssetPath);

            var visualRoot = new GameObject(string.IsNullOrWhiteSpace(displayName) ? "Visual" : displayName + "_Visual");
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

            var wrapperRoot = new GameObject(
                string.IsNullOrWhiteSpace(displayName) ? RootName : SanitizeHierarchyName(displayName));
            try
            {
                var visualCorrection = new GameObject(VisualCorrectionName);
                visualCorrection.transform.SetParent(wrapperRoot.transform, false);

                var modelSlot = new GameObject(ModelName);
                modelSlot.transform.SetParent(visualCorrection.transform, false);

                var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
                if (visualPrefab != null)
                {
                    var nested = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
                    nested.name = "Visual";
                    nested.transform.SetParent(modelSlot.transform, false);
                }

                var userContent = new GameObject(UserContentName);
                userContent.transform.SetParent(wrapperRoot.transform, false);

                var reference = wrapperRoot.AddComponent<UnslopAssetReference>();
                // Wrapper GUID is unknown until saved; configure after save.
                reference.Configure(assetId, versionId, physicalSpecId ?? string.Empty, string.Empty);

                PrefabUtility.SaveAsPrefabAsset(wrapperRoot, assetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrapperRoot);
            }

            var wrapperGuid = AssetDatabase.AssetPathToGUID(assetPath);
            var visualGuidFinal = AssetDatabase.AssetPathToGUID(visualPath);

            // Re-open wrapper and stamp stable GUIDs onto UnslopAssetReference.
            var wrapperContents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var reference = wrapperContents.GetComponent<UnslopAssetReference>();
                if (reference != null)
                {
                    reference.Configure(assetId, versionId, physicalSpecId ?? string.Empty, wrapperGuid);
                    EditorUtility.SetDirty(reference);
                }

                PrefabUtility.SaveAsPrefabAsset(wrapperContents, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(wrapperContents);
            }

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

        static string SanitizeHierarchyName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? RootName : name.Trim();
        }

        /// <summary>
        /// If a .meta already exists for the target prefab path, keep it so GUIDs remain stable across rebuilds.
        /// </summary>
        static void PreserveGuidMeta(string prefabPath)
        {
            var metaPath = prefabPath + ".meta";
            var fullMeta = Path.Combine(ManagedPaths.ProjectRoot, metaPath.Replace('/', Path.DirectorySeparatorChar));
            var fullPrefab = Path.Combine(ManagedPaths.ProjectRoot, prefabPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullMeta))
            {
                return;
            }

            // Ensure parent exists; leave meta in place. Deleting the prefab asset without meta preserves GUID on recreate.
            if (File.Exists(fullPrefab))
            {
                File.Delete(fullPrefab);
            }
        }
    }
}
