using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Importing;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Materials;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;
using UnityEditor;

namespace Unslop.UnityBridge.Editor.Install
{
    public sealed class AssetInstallResult
    {
        public string AssetId { get; set; }
        public string VersionId { get; set; }
        public string TransactionId { get; set; }
        public string WrapperPrefabPath { get; set; }
        public string ImportProfileHash { get; set; }
        public LockAssetEntry LockEntry { get; set; }
    }

    public sealed class AssetInstallService
    {
        readonly IUnslopApiClient _api;

        public AssetInstallService(IUnslopApiClient api = null)
        {
            _api = api ?? BridgeServices.CreateApiClient();
        }

        public async Task<AssetInstallResult> InstallAsync(
            string assetId,
            string versionId,
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_enabled"))
            {
                throw new InvalidOperationException("unity_bridge_enabled feature flag is off.");
            }

            if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException("assetId and versionId are required.");
            }

            var settings = UnslopProjectSettings.EnsureExists();
            if (string.IsNullOrWhiteSpace(settings.BoundProjectId))
            {
                throw new InvalidOperationException("Bind a project before installing assets.");
            }

            var transactionId = Guid.NewGuid().ToString("N");
            status?.Report("Fetching version detail…");
            var detail = await _api.GetAssetVersionAsync(assetId, versionId, cancellationToken);
            var previous = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            previous.assets.TryGetValue(assetId, out var previousEntry);

            status?.Report("Downloading package…");
            var downloader = new DownloadManager(_api);
            var download = await downloader.DownloadVersionAsync(assetId, versionId, detail, null, cancellationToken);

            status?.Report("Materialising staging…");
            var staging = StagingMaterialiser.Materialise(download.DownloadRoot, download.Manifest, assetId, versionId);

            // Settle the friendly installed folder BEFORE writing materials/textures/prefabs
            // so AssetDatabase paths stay valid (rename-after-write left .mat paths stale).
            var displayName = download.Manifest?.display_name;
            status?.Report("Preparing installed folder…");
            var installedRoot = ManagedPaths.EnsureFriendlyInstalledDir(assetId, displayName);
            ManagedPaths.EnsureDirectory(installedRoot);
            ManagedPaths.EnsureDirectory(installedRoot + "/Materials");
            ManagedPaths.EnsureDirectory(installedRoot + "/textures");
            ManagedPaths.EnsureDirectory(installedRoot + "/Prefabs");

            // Copy textures first so material generation references Installed paths (not __Staging).
            var texturesSource = staging.StagingAssetPath + "/textures";
            var texturesDest = installedRoot + "/textures";
            if (AssetDatabase.IsValidFolder(texturesSource) || Directory.Exists(ToFull(texturesSource)))
            {
                CopyFolderContents(texturesSource, texturesDest);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                // Re-apply import profile on installed textures.
                if (Directory.Exists(ToFull(texturesDest)))
                {
                    foreach (var file in Directory.GetFiles(ToFull(texturesDest), "*.*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var assetPath = ToAssetPath(file);
                        if (AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter)
                        {
                            ImportProfile.ApplyTextureImporter(textureImporter, Path.GetFileName(file));
                            textureImporter.SaveAndReimport();
                        }
                    }
                }
            }

            status?.Report("Generating materials…");
            var materialsDir = $"{installedRoot}/Materials";
            var generator = new MaterialGenerator(MaterialGenerator.ResolveAdapter(BridgeServices.CreateClientContext().render_pipeline));
            // Use installedRoot so materials.json texture paths (textures/...) resolve under Installed.
            var materials = generator.Generate(download.Materials, installedRoot, materialsDir);
            if (materials.ManagedCount == 0)
            {
                BridgeLog.Warn("No managed materials generated — check URP Lit shader and materials.json.");
            }

            // Promote model into Installed before wrapper build so source GUID is under Installed when possible.
            status?.Report("Promoting model into Installed…");
            var modelFileName = Path.GetFileName(staging.ModelAssetPath);
            var installedModelPath = $"{installedRoot}/{modelFileName}";
            var modelManifestFile = download.Manifest?.files?.FirstOrDefault(f =>
                string.Equals(
                    Path.GetFileName(f.relative_path),
                    modelFileName,
                    StringComparison.OrdinalIgnoreCase));
            var expectedModelSha = modelManifestFile?.sha256 ?? download.ManifestSha256;

            MeshImportDiagnostics.LogFile(
                "Model download cache",
                Path.Combine(download.DownloadRoot, (download.Manifest?.model?.relative_path ?? "model.fbx").Replace('/', Path.DirectorySeparatorChar)),
                expectedModelSha);
            MeshImportDiagnostics.LogFile("Model staging", ToFull(staging.ModelAssetPath), expectedModelSha);

            CopyAssetPreservingMeta(staging.ModelAssetPath, installedModelPath);
            ForceReimportModel(installedModelPath);
            MeshImportDiagnostics.LogFile("Model installed", ToFull(installedModelPath), expectedModelSha);
            MeshImportDiagnostics.LogAssetMeshBounds("Model installed (Unity import)", installedModelPath);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            status?.Report("Building wrapper prefabs…");
            var poses = SceneInstancePosePreserver.Capture(assetId);
            var modelPathForPrefab = File.Exists(ToFull(installedModelPath)) ? installedModelPath : staging.ModelAssetPath;
            var physicalSpecId = await SafePhysicalSpecId(assetId, cancellationToken);
            BridgeLog.Info(
                $"Install version={versionId} modelPath={modelPathForPrefab} physical_spec={physicalSpecId ?? "(none)"} display={displayName}");
            var wrapper = WrapperPrefabBuilder.Build(
                assetId,
                versionId,
                physicalSpecId,
                modelPathForPrefab,
                displayName);
            MeshImportDiagnostics.LogAssetMeshBounds("Visual prefab after build", wrapper.VisualPrefabPath);
            MeshImportDiagnostics.LogAssetMeshBounds("Asset prefab after build", wrapper.AssetPrefabPath);

            status?.Report("Assigning materials to Visual…");
            var assigned = MaterialSlotApplicator.ApplyToPrefab(
                wrapper.VisualPrefabPath,
                download.Materials,
                materials.MaterialPathsById);
            if (assigned == 0 && materials.ManagedCount > 0)
            {
                BridgeLog.Warn(
                    $"Materials were generated ({materials.ManagedCount}) but none were assigned to renderers. " +
                    $"Visual={wrapper.VisualPrefabPath}");
            }

            SceneInstancePosePreserver.Restore(assetId, poses);
            WrapperPrefabBuilder.RenameSceneInstances(assetId, displayName);

            var pipeline = FirstNonEmpty(
                detail.pipeline_origin,
                detail.compatibility?.pipeline_origin);
            BridgeLog.Info($"Install pipeline_origin={pipeline ?? "(none)"}");
            if (VisualCorrectionReset.IsCanonicalScaleBake(pipeline))
            {
                status?.Report("canonical_scale_bake: resetting VisualCorrection to 1,1,1…");
                VisualCorrectionReset.ApplyForCanonicalBake(assetId, wrapper.AssetPrefabPath);
            }

            var lockEntry = new LockAssetEntry
            {
                installed_version_id = versionId,
                installed_version_number = detail.version_number,
                physical_spec_id = physicalSpecId ?? string.Empty,
                wrapper_prefab_guid = wrapper.WrapperPrefabGuid,
                visual_prefab_guid = wrapper.VisualPrefabGuid,
                source_fbx_guid = wrapper.SourceFbxGuid,
                import_profile_hash = staging.ImportProfileHash,
                manifest_sha256 = download.ManifestSha256,
                display_name = displayName ?? string.Empty,
                material_bindings = materials.Bindings
            };

            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            LockFileService.UpsertAsset(lockFile, assetId, lockEntry);

            LockFileService.SaveLocalMetadata(new AssetLocalMetadata
            {
                schema_version = 1,
                asset_id = assetId,
                display_name = displayName ?? string.Empty,
                installed_version_id = versionId,
                physical_spec_id = physicalSpecId ?? string.Empty,
                visual_correction = new[] { 1f, 1f, 1f },
                last_transaction_id = transactionId,
                accepted_at = DateTime.UtcNow.ToString("o")
            });

            status?.Report("Submitting install report…");
            if (FeatureFlagService.IsEnabled("unity_bridge_enabled"))
            {
                var report = new InstallReportDto
                {
                    transaction_id = transactionId,
                    operation = previousEntry == null ? "install" : "reinstall",
                    previous_version_id = previousEntry?.installed_version_id,
                    installed_version_id = versionId,
                    physical_spec_id = physicalSpecId,
                    manifest_sha256 = download.ManifestSha256,
                    import_profile_hash = staging.ImportProfileHash,
                    lock_entry_hash = lockEntry.state_hash,
                    material_summary = new MaterialSummaryDto
                    {
                        managed = materials.ManagedCount,
                        local_override = materials.Bindings.Count(kv => kv.Value.mode == "local_override"),
                        locally_modified_managed = 0
                    },
                    client = BridgeServices.CreateClientContext(),
                    committed_at = DateTime.UtcNow.ToString("o")
                };

                await _api.UpsertInstallReportAsync(
                    settings.BoundProjectId,
                    assetId,
                    report,
                    transactionId,
                    cancellationToken);
            }

            BridgeLog.Info($"Installed asset {assetId} version {versionId} (tx={transactionId}).");
            return new AssetInstallResult
            {
                AssetId = assetId,
                VersionId = versionId,
                TransactionId = transactionId,
                WrapperPrefabPath = wrapper.AssetPrefabPath,
                ImportProfileHash = staging.ImportProfileHash,
                LockEntry = lockEntry
            };
        }

        async Task<string> SafePhysicalSpecId(string assetId, CancellationToken cancellationToken)
        {
            try
            {
                var asset = await _api.GetAssetAsync(assetId, cancellationToken);
                return asset?.current_physical_spec_id;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn("Could not resolve physical_spec_id: " + BridgeLog.Redact(ex.Message));
                return null;
            }
        }

        static string ToFull(string assetPath) =>
            Path.Combine(ManagedPaths.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        static void CopyAssetPreservingMeta(string sourceAssetPath, string destAssetPath)
        {
            var src = ToFull(sourceAssetPath);
            var dst = ToFull(destAssetPath);
            if (!File.Exists(src))
            {
                BridgeLog.Warn($"Copy skipped — source missing: {sourceAssetPath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? ManagedPaths.ProjectRoot);
            var destMeta = dst + ".meta";
            var existingMeta = File.Exists(destMeta) ? File.ReadAllText(destMeta) : null;
            File.Copy(src, dst, true);
            if (existingMeta != null)
            {
                File.WriteAllText(destMeta, existingMeta);
            }

            BridgeLog.Info(
                $"Copied asset {sourceAssetPath} → {destAssetPath} " +
                $"(preservedMeta={existingMeta != null}, sha={HashUtil.ShortHash(HashUtil.Sha256File(dst))})");
        }

        static void ForceReimportModel(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(ToFull(assetPath)))
            {
                return;
            }

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            BridgeLog.Info($"Force-reimported model {assetPath}");
        }

        static string ToAssetPath(string fullPath)
        {
            var normalized = fullPath.Replace('\\', '/');
            var root = ManagedPaths.ProjectRoot.Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(root.Length).TrimStart('/');
            }

            var assetsIdx = normalized.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            return assetsIdx >= 0 ? normalized.Substring(assetsIdx) : normalized;
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        static void CopyFolderContents(string sourceAssetFolder, string destAssetFolder)
        {
            var src = ToFull(sourceAssetFolder);
            if (!Directory.Exists(src))
            {
                return;
            }

            ManagedPaths.EnsureDirectory(destAssetFolder);
            var dst = ToFull(destAssetFolder);
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rel = Path.GetRelativePath(src, file);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dst);
                File.Copy(file, target, true);
            }
        }
    }
}
