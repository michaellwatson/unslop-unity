using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Diagnostics
{
    public enum DriftKind
    {
        MissingInstalledFolder,
        MissingWrapperPrefab,
        WrapperGuidMismatch,
        VisualGuidMismatch,
        SourceFbxMissing,
        LockEntryOrphan,
        SceneReferenceBroken,
        LocalMetadataMismatch
    }

    public enum DriftRepairAction
    {
        None,
        RestoreDeclared,
        AdoptLocal,
        ReconnectWrapper,
        ExportDiagnostics
    }

    public sealed class DriftFinding
    {
        public string AssetId { get; set; }
        public DriftKind Kind { get; set; }
        public string Detail { get; set; }
        public DriftRepairAction SuggestedRepair { get; set; }
    }

    public sealed class DriftReport
    {
        public List<DriftFinding> Findings { get; } = new List<DriftFinding>();
        public bool HasDrift => Findings.Count > 0;
    }

    /// <summary>
    /// Project drift diagnostics vs Unslop.lock.json with repair suggestions.
    /// </summary>
    public static class DriftDiagnostics
    {
        public static DriftReport Scan()
        {
            var settings = UnslopProjectSettings.EnsureExists();
            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            var report = new DriftReport();

            foreach (var kv in lockFile.assets)
            {
                AnalyseAsset(kv.Key, kv.Value, report);
            }

            // Folders under Installed with no lock entry
            var installedRoot = Path.Combine(
                ManagedPaths.ProjectRoot,
                ManagedPaths.InstalledRoot.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(installedRoot))
            {
                foreach (var dir in Directory.GetDirectories(installedRoot))
                {
                    var id = Path.GetFileName(dir);
                    if (!lockFile.assets.ContainsKey(id))
                    {
                        report.Findings.Add(new DriftFinding
                        {
                            AssetId = id,
                            Kind = DriftKind.LockEntryOrphan,
                            Detail = "Installed folder exists without lock entry.",
                            SuggestedRepair = DriftRepairAction.AdoptLocal
                        });
                    }
                }
            }

            return report;
        }

        public static string ApplyRepair(DriftFinding finding)
        {
            if (finding == null)
            {
                return "No finding.";
            }

            switch (finding.SuggestedRepair)
            {
                case DriftRepairAction.RestoreDeclared:
                    return "Restore declared: re-run Install/Update for the lock-declared version, or discard incomplete transactions.";
                case DriftRepairAction.AdoptLocal:
                    return "Adopt local: add/update lock entry from on-disk wrapper GUIDs (manual review recommended).";
                case DriftRepairAction.ReconnectWrapper:
                    return ReconnectWrapper(finding.AssetId);
                case DriftRepairAction.ExportDiagnostics:
                    return SupportExport.ExportRedactedBundle();
                default:
                    return "No automatic repair.";
            }
        }

        static void AnalyseAsset(string assetId, LockAssetEntry entry, DriftReport report)
        {
            var installed = ManagedPaths.InstalledAssetDir(assetId);
            var installedFull = Path.Combine(
                ManagedPaths.ProjectRoot,
                installed.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(installedFull))
            {
                report.Findings.Add(new DriftFinding
                {
                    AssetId = assetId,
                    Kind = DriftKind.MissingInstalledFolder,
                    Detail = "Lock entry present but Installed folder missing.",
                    SuggestedRepair = DriftRepairAction.RestoreDeclared
                });
                return;
            }

            if (string.IsNullOrEmpty(entry.wrapper_prefab_guid))
            {
                report.Findings.Add(new DriftFinding
                {
                    AssetId = assetId,
                    Kind = DriftKind.MissingWrapperPrefab,
                    Detail = "wrapper_prefab_guid empty in lock.",
                    SuggestedRepair = DriftRepairAction.ReconnectWrapper
                });
            }
            else
            {
                var path = AssetDatabase.GUIDToAssetPath(entry.wrapper_prefab_guid);
                if (string.IsNullOrEmpty(path))
                {
                    report.Findings.Add(new DriftFinding
                    {
                        AssetId = assetId,
                        Kind = DriftKind.WrapperGuidMismatch,
                        Detail = $"GUID {entry.wrapper_prefab_guid} does not resolve.",
                        SuggestedRepair = DriftRepairAction.ReconnectWrapper
                    });
                }
                else
                {
                    var expected = ManagedPaths.ResolveWrapperPrefabPath(installed);
                    if (!string.Equals(path.Replace('\\', '/'), expected, StringComparison.OrdinalIgnoreCase))
                    {
                        report.Findings.Add(new DriftFinding
                        {
                            AssetId = assetId,
                            Kind = DriftKind.WrapperGuidMismatch,
                            Detail = $"Wrapper path {path} != declared {expected}",
                            SuggestedRepair = DriftRepairAction.AdoptLocal
                        });
                    }
                }
            }

            if (!string.IsNullOrEmpty(entry.visual_prefab_guid))
            {
                var visualPath = AssetDatabase.GUIDToAssetPath(entry.visual_prefab_guid);
                if (string.IsNullOrEmpty(visualPath))
                {
                    report.Findings.Add(new DriftFinding
                    {
                        AssetId = assetId,
                        Kind = DriftKind.VisualGuidMismatch,
                        Detail = $"visual_prefab_guid {entry.visual_prefab_guid} missing.",
                        SuggestedRepair = DriftRepairAction.RestoreDeclared
                    });
                }
            }

            if (!string.IsNullOrEmpty(entry.source_fbx_guid)
                && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.source_fbx_guid)))
            {
                report.Findings.Add(new DriftFinding
                {
                    AssetId = assetId,
                    Kind = DriftKind.SourceFbxMissing,
                    Detail = "source_fbx_guid does not resolve.",
                    SuggestedRepair = DriftRepairAction.RestoreDeclared
                });
            }

            var local = LockFileService.LoadLocalMetadata(assetId);
            if (local != null
                && !string.Equals(local.installed_version_id, entry.installed_version_id, StringComparison.Ordinal))
            {
                report.Findings.Add(new DriftFinding
                {
                    AssetId = assetId,
                    Kind = DriftKind.LocalMetadataMismatch,
                    Detail = $"asset.local.json version {local.installed_version_id} != lock {entry.installed_version_id}",
                    SuggestedRepair = DriftRepairAction.AdoptLocal
                });
            }

            // Scene references
            foreach (var reference in UnityEngine.Object.FindObjectsByType<UnslopAssetReference>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (!string.Equals(reference.AssetId, assetId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.wrapper_prefab_guid)
                    && !string.Equals(reference.WrapperPrefabGuid, entry.wrapper_prefab_guid, StringComparison.Ordinal))
                {
                    report.Findings.Add(new DriftFinding
                    {
                        AssetId = assetId,
                        Kind = DriftKind.SceneReferenceBroken,
                        Detail = $"Scene instance GUID {reference.WrapperPrefabGuid} != lock {entry.wrapper_prefab_guid}",
                        SuggestedRepair = DriftRepairAction.ReconnectWrapper
                    });
                }
            }
        }

        static string ReconnectWrapper(string assetId)
        {
            var settings = UnslopProjectSettings.EnsureExists();
            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            if (!lockFile.assets.TryGetValue(assetId, out var entry))
            {
                return "No lock entry.";
            }

            var wrapperPath = ManagedPaths.ResolveWrapperPrefabPath(ManagedPaths.InstalledAssetDir(assetId));
            var visualPath = ManagedPaths.ResolveVisualPrefabPath(ManagedPaths.InstalledAssetDir(assetId));
            var wrapperGuid = AssetDatabase.AssetPathToGUID(wrapperPath);
            var visualGuid = AssetDatabase.AssetPathToGUID(visualPath);
            if (string.IsNullOrEmpty(wrapperGuid))
            {
                return "Wrapper prefab not found on disk.";
            }

            entry.wrapper_prefab_guid = wrapperGuid;
            if (!string.IsNullOrEmpty(visualGuid))
            {
                entry.visual_prefab_guid = visualGuid;
            }

            LockFileService.UpsertAsset(lockFile, assetId, entry);

            var wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
            if (wrapper != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(wrapperPath);
                try
                {
                    var reference = contents.GetComponent<UnslopAssetReference>();
                    if (reference != null)
                    {
                        reference.Configure(assetId, entry.installed_version_id, entry.physical_spec_id, wrapperGuid);
                        PrefabUtility.SaveAsPrefabAsset(contents, wrapperPath);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            BridgeLog.Info($"Reconnected wrapper for {assetId} → {wrapperGuid}");
            return $"Reconnected wrapper GUID {wrapperGuid}";
        }
    }
}
