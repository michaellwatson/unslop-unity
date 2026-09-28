using System;
using System.Collections.Generic;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Install;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Publishing
{
    /// <summary>
    /// After publish + install, pulls the new version onto the prefab the user published from
    /// and any matching scene instances.
    /// </summary>
    public static class PublishedPrefabRefresher
    {
        public static GameObject RefreshAfterInstall(GameObject publishSource, AssetInstallResult install)
        {
            if (install == null || string.IsNullOrWhiteSpace(install.WrapperPrefabPath))
            {
                return publishSource;
            }

            var wrapperPath = install.WrapperPrefabPath.Replace('\\', '/');
            AssetDatabase.ImportAsset(
                wrapperPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var wrapperAsset = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
            if (wrapperAsset == null)
            {
                BridgeLog.Warn("Publish refresh skipped — wrapper prefab missing at " + wrapperPath);
                return publishSource;
            }

            var wrapperGuid = install.LockEntry?.wrapper_prefab_guid
                              ?? AssetDatabase.AssetPathToGUID(wrapperPath);
            var versionId = install.VersionId ?? install.LockEntry?.installed_version_id;
            var physicalSpecId = install.LockEntry?.physical_spec_id;

            var refreshedInstances = RevertMatchingSceneInstances(
                install.AssetId,
                wrapperPath,
                versionId,
                physicalSpecId,
                wrapperGuid);
            BridgeLog.Info(
                $"Publish refresh asset={install.AssetId} version={versionId} " +
                $"wrapper={wrapperPath} sceneInstances={refreshedInstances}");

            if (publishSource == null)
            {
                return wrapperAsset;
            }

            var sourcePath = ResolveSourceAssetPath(publishSource);
            if (string.Equals(sourcePath, wrapperPath, StringComparison.OrdinalIgnoreCase))
            {
                return wrapperAsset;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(publishSource))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(publishSource);
                if (root != null)
                {
                    TryRevertInstanceRoot(root);
                    return root;
                }
            }

            var reference = publishSource.GetComponentInChildren<UnslopAssetReference>(true);
            if (reference != null
                && string.Equals(reference.AssetId, install.AssetId, StringComparison.Ordinal))
            {
                return wrapperAsset;
            }

            return wrapperAsset;
        }

        static int RevertMatchingSceneInstances(
            string assetId,
            string wrapperPath,
            string versionId,
            string physicalSpecId,
            string wrapperGuid)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return 0;
            }

            var reverted = 0;
            var seenRoots = new HashSet<int>();

            foreach (var reference in UnityEngine.Object.FindObjectsByType<UnslopAssetReference>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (reference == null
                    || !string.Equals(reference.AssetId, assetId, StringComparison.Ordinal))
                {
                    continue;
                }

                var go = reference.gameObject;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go) ?? go;
                var rootId = root.GetInstanceID();
                if (!seenRoots.Add(rootId))
                {
                    continue;
                }

                if (PrefabUtility.IsPartOfPrefabInstance(root))
                {
                    if (TryRevertInstanceRoot(root))
                    {
                        reverted++;
                    }
                }
                else
                {
                    StampReference(reference, assetId, versionId, physicalSpecId, wrapperGuid);
                }

                if (!string.IsNullOrEmpty(root.scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(root.scene);
                }
            }

            EnsureWrapperAssetStamped(wrapperPath, assetId, versionId, physicalSpecId, wrapperGuid);
            return reverted;
        }

        static void EnsureWrapperAssetStamped(
            string wrapperPath,
            string assetId,
            string versionId,
            string physicalSpecId,
            string wrapperGuid)
        {
            if (string.IsNullOrEmpty(wrapperPath)
                || AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath) == null)
            {
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(wrapperPath);
            try
            {
                var reference = contents.GetComponent<UnslopAssetReference>()
                                ?? contents.GetComponentInChildren<UnslopAssetReference>(true);
                if (reference == null)
                {
                    return;
                }

                reference.Configure(assetId, versionId, physicalSpecId ?? string.Empty, wrapperGuid);
                EditorUtility.SetDirty(reference);
                PrefabUtility.SaveAsPrefabAsset(contents, wrapperPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void StampReference(
            UnslopAssetReference reference,
            string assetId,
            string versionId,
            string physicalSpecId,
            string wrapperGuid)
        {
            if (reference == null)
            {
                return;
            }

            reference.Configure(
                assetId,
                versionId ?? reference.InstalledVersionId,
                physicalSpecId ?? reference.PhysicalSpecId,
                wrapperGuid ?? reference.WrapperPrefabGuid);

            EditorUtility.SetDirty(reference);
            PrefabUtility.RecordPrefabInstancePropertyModifications(reference);
        }

        static bool TryRevertInstanceRoot(GameObject root)
        {
            if (root == null || !PrefabUtility.IsPartOfPrefabInstance(root))
            {
                return false;
            }

            try
            {
                PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
                EditorUtility.SetDirty(root);
                return true;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn("Could not revert prefab instance after publish: " + BridgeLog.Redact(ex.Message));
                return false;
            }
        }

        static string ResolveSourceAssetPath(GameObject source)
        {
            if (source == null)
            {
                return null;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(source))
            {
                return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
            }

            return AssetDatabase.GetAssetPath(source);
        }
    }
}
