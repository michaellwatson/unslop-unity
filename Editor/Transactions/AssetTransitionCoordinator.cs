using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unslop.UnityBridge.Editor.Analysis;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Importing;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using Unslop.UnityBridge.Editor.Materials;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Transactions
{
    public interface IAssetTransitionCoordinator
    {
        Task<TransitionSession> PrepareUpdateAsync(
            string assetId,
            string targetVersionId,
            string operation = "update",
            IProgress<string> status = null,
            CancellationToken cancellationToken = default);

        Task<AssetDiffReport> SnapshotAndStageAsync(
            TransitionSession session,
            IProgress<string> status = null,
            CancellationToken cancellationToken = default);

        Task AcceptAsync(TransitionSession session, IProgress<string> status = null, CancellationToken cancellationToken = default);
        Task DiscardAsync(TransitionSession session, IProgress<string> status = null);
        Task<int> RecoverAsync(IProgress<string> status = null);
    }

    public sealed class TransitionSession
    {
        public TransactionJournalRecord Journal { get; set; }
        public AssetDiffReport Diff { get; set; }
        public string StagingAssetPath { get; set; }
        public string CandidateModelPath { get; set; }
        public MaterialsManifest CandidateMaterials { get; set; }
        public AssetVersionManifest CandidateManifest { get; set; }
        public string ManifestSha256 { get; set; }
        public string ImportProfileHash { get; set; }
        public string PipelineOrigin { get; set; }
        public LockAssetEntry PreviousEntry { get; set; }
    }

    /// <summary>
    /// Staged update coordinator: prepare → snapshot → apply → import → regenerate → verify → commit/discard.
    /// No silent install path — Accept must be called explicitly.
    /// </summary>
    public sealed class AssetTransitionCoordinator : IAssetTransitionCoordinator
    {
        readonly IUnslopApiClient _api;

        public AssetTransitionCoordinator(IUnslopApiClient api = null)
        {
            _api = api ?? BridgeServices.CreateApiClient();
        }

        public async Task<TransitionSession> PrepareUpdateAsync(
            string assetId,
            string targetVersionId,
            string operation = "update",
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            EnsureUpdatesEnabled();
            var settings = UnslopProjectSettings.EnsureExists();
            if (string.IsNullOrWhiteSpace(settings.BoundProjectId))
            {
                throw new InvalidOperationException("Bind a project before updating assets.");
            }

            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            lockFile.assets.TryGetValue(assetId, out var previous);
            if (previous == null)
            {
                throw new InvalidOperationException($"Asset {assetId} is not installed. Use Install instead of Update.");
            }

            status?.Report("Preparing transition journal…");
            var journal = TransactionJournal.Create(
                assetId,
                operation,
                previous.installed_version_id,
                targetVersionId);
            journal.wrapper_prefab_guid = previous.wrapper_prefab_guid;
            journal.visual_prefab_guid = previous.visual_prefab_guid;
            journal.lock_entry_json = JsonConvert.SerializeObject(previous);
            var local = LockFileService.LoadLocalMetadata(assetId);
            if (local != null)
            {
                journal.local_metadata_json = JsonConvert.SerializeObject(local);
            }

            TransactionJournal.Save(journal);

            return new TransitionSession
            {
                Journal = journal,
                PreviousEntry = previous
            };
        }

        public async Task<AssetDiffReport> SnapshotAndStageAsync(
            TransitionSession session,
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            if (session?.Journal == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var journal = session.Journal;
            try
            {
                status?.Report("Snapshotting installed asset…");
                SnapshotInstalled(journal);
                TransactionJournal.Advance(journal, TransactionPhases.Snapshotted);

                status?.Report("Downloading candidate…");
                var detail = await _api.GetAssetVersionAsync(
                    journal.asset_id,
                    journal.to_version_id,
                    cancellationToken);
                var downloader = new DownloadManager(_api);
                var download = await downloader.DownloadVersionAsync(
                    journal.asset_id,
                    journal.to_version_id,
                    detail,
                    null,
                    cancellationToken);
                journal.manifest_sha256 = download.ManifestSha256;
                TransactionJournal.Advance(journal, TransactionPhases.Applied);

                status?.Report("Importing into staging…");
                var staging = StagingMaterialiser.Materialise(
                    download.DownloadRoot,
                    download.Manifest,
                    journal.asset_id,
                    journal.to_version_id);
                journal.staging_path = staging.StagingAssetPath;
                journal.import_profile_hash = staging.ImportProfileHash;
                TransactionJournal.Advance(journal, TransactionPhases.Imported);

                session.StagingAssetPath = staging.StagingAssetPath;
                session.CandidateModelPath = staging.ModelAssetPath;
                session.CandidateManifest = download.Manifest;
                session.CandidateMaterials = download.Materials;
                session.ManifestSha256 = download.ManifestSha256;
                session.ImportProfileHash = staging.ImportProfileHash;
                session.PipelineOrigin = FirstNonEmpty(
                    detail.pipeline_origin,
                    detail.compatibility?.pipeline_origin);

                status?.Report("Analysing diff…");
                var installedRoot = ManagedPaths.InstalledAssetDir(journal.asset_id);
                var installedModel = FindInstalledModel(installedRoot);
                var installedMaterials = LoadMaterialsJson(installedRoot);
                var installedManifest = download.Manifest; // best-effort; installed asset.json if present
                var installedAssetJson = Path.Combine(
                    ManagedPaths.ProjectRoot,
                    installedRoot.Replace('/', Path.DirectorySeparatorChar),
                    "asset.json");
                if (File.Exists(installedAssetJson))
                {
                    try
                    {
                        installedManifest = JsonConvert.DeserializeObject<AssetVersionManifest>(
                            File.ReadAllText(installedAssetJson)) ?? installedManifest;
                    }
                    catch
                    {
                        // keep candidate-derived fallback
                    }
                }

                session.Diff = AssetDiffEngine.CompareInstalledToStaging(
                    journal.asset_id,
                    session.PreviousEntry,
                    installedManifest,
                    installedMaterials,
                    journal.to_version_id,
                    download.Manifest,
                    download.Materials,
                    installedModel,
                    staging.ModelAssetPath);

                TransactionJournal.Advance(journal, TransactionPhases.Verified, "awaiting_acceptance");
                BridgeLog.Info(
                    $"Staged update {journal.asset_id}: {journal.from_version_id} → {journal.to_version_id} (tx={journal.transaction_id}). Awaiting explicit accept.");
                return session.Diff;
            }
            catch (Exception ex)
            {
                TransactionJournal.Fail(journal, ex);
                BridgeLog.Exception(ex, "SnapshotAndStage");
                throw;
            }
        }

        public async Task AcceptAsync(
            TransitionSession session,
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            if (session?.Journal == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (session.Journal.committed
                || session.Journal.phase == TransactionPhases.Committed)
            {
                return;
            }

            if (session.Journal.phase != TransactionPhases.Verified
                && session.Journal.phase != TransactionPhases.Regenerated
                && session.Journal.phase != TransactionPhases.Imported)
            {
                throw new InvalidOperationException(
                    $"Cannot accept transition in phase '{session.Journal.phase}'. Stage and verify first.");
            }

            var journal = session.Journal;
            var settings = UnslopProjectSettings.EnsureExists();
            try
            {
                status?.Report("Regenerating materials and wrapper…");
                var materialsDir = $"{ManagedPaths.InstalledAssetDir(journal.asset_id, session.CandidateManifest?.display_name)}/Materials";
                var generator = new MaterialGenerator(
                    MaterialGenerator.ResolveAdapter(BridgeServices.CreateClientContext().render_pipeline));
                var materials = generator.Generate(
                    session.CandidateMaterials ?? new MaterialsManifest(),
                    session.StagingAssetPath,
                    materialsDir);

                var displayName = session.CandidateManifest?.display_name;
                var installedRoot = ManagedPaths.EnsureFriendlyInstalledDir(journal.asset_id, displayName);
                ManagedPaths.EnsureDirectory(installedRoot);
                var modelFileName = Path.GetFileName(session.CandidateModelPath);
                var installedModelPath = $"{installedRoot}/{modelFileName}";
                CopyAssetPreservingMeta(session.CandidateModelPath, installedModelPath);
                AssetDatabase.ImportAsset(
                    installedModelPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                MeshImportDiagnostics.LogAssetMeshBounds("Accept: installed model", installedModelPath);

                var texturesSource = session.StagingAssetPath + "/textures";
                var texturesDest = installedRoot + "/Textures";
                if (Directory.Exists(ToFull(texturesSource)))
                {
                    CopyFolderContents(texturesSource, texturesDest);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                TransactionJournal.Advance(journal, TransactionPhases.Regenerated);

                var physicalSpecId = session.PreviousEntry?.physical_spec_id;
                var poses = SceneInstancePosePreserver.Capture(journal.asset_id);
                var wrapper = WrapperPrefabBuilder.Build(
                    journal.asset_id,
                    journal.to_version_id,
                    physicalSpecId,
                    File.Exists(ToFull(installedModelPath)) ? installedModelPath : session.CandidateModelPath,
                    session.CandidateManifest?.display_name);

                // GUID preservation check
                if (!string.IsNullOrEmpty(journal.wrapper_prefab_guid)
                    && !string.Equals(journal.wrapper_prefab_guid, wrapper.WrapperPrefabGuid, StringComparison.Ordinal))
                {
                    BridgeLog.Warn(
                        $"Wrapper GUID changed during accept: {journal.wrapper_prefab_guid} → {wrapper.WrapperPrefabGuid}");
                }

                SceneInstancePosePreserver.Restore(journal.asset_id, poses);
                WrapperPrefabBuilder.RenameSceneInstances(journal.asset_id, displayName);

                BridgeLog.Info($"Accept pipeline_origin={session.PipelineOrigin ?? "(none)"}");
                if (VisualCorrectionReset.IsCanonicalScaleBake(session.PipelineOrigin))
                {
                    status?.Report("canonical_scale_bake: resetting VisualCorrection to 1,1,1…");
                    VisualCorrectionReset.ApplyForCanonicalBake(journal.asset_id, wrapper.AssetPrefabPath);
                }

                var lockEntry = new LockAssetEntry
                {
                    installed_version_id = journal.to_version_id,
                    installed_version_number = session.CandidateManifest?.version_number
                                               ?? (session.PreviousEntry?.installed_version_number ?? 0) + 1,
                    physical_spec_id = physicalSpecId ?? string.Empty,
                    wrapper_prefab_guid = wrapper.WrapperPrefabGuid,
                    visual_prefab_guid = wrapper.VisualPrefabGuid,
                    source_fbx_guid = wrapper.SourceFbxGuid,
                    import_profile_hash = session.ImportProfileHash ?? journal.import_profile_hash,
                    manifest_sha256 = session.ManifestSha256 ?? journal.manifest_sha256,
                    pin = session.PreviousEntry?.pin,
                    material_bindings = materials.Bindings
                };

                var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
                LockFileService.UpsertAsset(lockFile, journal.asset_id, lockEntry);

                var visualCorrection = new[] { 1f, 1f, 1f };
                if (!VisualCorrectionReset.IsCanonicalScaleBake(session.PipelineOrigin)
                    && !string.IsNullOrEmpty(journal.local_metadata_json))
                {
                    try
                    {
                        var prevLocal = JsonConvert.DeserializeObject<AssetLocalMetadata>(journal.local_metadata_json);
                        if (prevLocal?.visual_correction != null && prevLocal.visual_correction.Length == 3)
                        {
                            visualCorrection = prevLocal.visual_correction;
                        }
                    }
                    catch
                    {
                        // keep default
                    }
                }

                LockFileService.SaveLocalMetadata(new AssetLocalMetadata
                {
                    schema_version = 1,
                    asset_id = journal.asset_id,
                    installed_version_id = journal.to_version_id,
                    physical_spec_id = physicalSpecId ?? string.Empty,
                    visual_correction = visualCorrection,
                    analysis_snapshot_hash = session.Diff != null
                        ? HashUtil.Sha256Utf8(JsonConvert.SerializeObject(session.Diff.Entries.Select(e => e.Path + e.Kind)))
                        : string.Empty,
                    last_transaction_id = journal.transaction_id,
                    accepted_at = DateTime.UtcNow.ToString("o")
                });

                status?.Report("Submitting install report…");
                await _api.UpsertInstallReportAsync(
                    settings.BoundProjectId,
                    journal.asset_id,
                    new InstallReportDto
                    {
                        transaction_id = journal.transaction_id,
                        operation = journal.operation,
                        previous_version_id = journal.from_version_id,
                        installed_version_id = journal.to_version_id,
                        physical_spec_id = physicalSpecId,
                        manifest_sha256 = lockEntry.manifest_sha256,
                        import_profile_hash = lockEntry.import_profile_hash,
                        lock_entry_hash = lockEntry.state_hash,
                        material_summary = new MaterialSummaryDto
                        {
                            managed = materials.ManagedCount,
                            local_override = materials.Bindings.Count(kv => kv.Value.mode == "local_override")
                        },
                        client = BridgeServices.CreateClientContext(),
                        committed_at = DateTime.UtcNow.ToString("o")
                    },
                    journal.transaction_id,
                    cancellationToken);

                TransactionJournal.Advance(journal, TransactionPhases.Committed);
                BridgeLog.Info($"Accepted transition {journal.transaction_id} for {journal.asset_id}.");
            }
            catch (Exception ex)
            {
                TransactionJournal.Fail(journal, ex);
                BridgeLog.Exception(ex, "Accept transition");
                throw;
            }
        }

        public Task DiscardAsync(TransitionSession session, IProgress<string> status = null)
        {
            if (session?.Journal == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            status?.Report("Discarding staged update…");
            RestoreSnapshot(session.Journal);
            CleanupStaging(session.Journal);
            TransactionJournal.Advance(session.Journal, TransactionPhases.Discarded);
            BridgeLog.Info($"Discarded transition {session.Journal.transaction_id}; install unchanged.");
            return Task.CompletedTask;
        }

        public async Task<int> RecoverAsync(IProgress<string> status = null)
        {
            ManagedPaths.EnsureOperationalDirectories();
            if (!Directory.Exists(ManagedPaths.TransactionsDir))
            {
                return 0;
            }

            var recovered = 0;
            foreach (var dir in Directory.GetDirectories(ManagedPaths.TransactionsDir))
            {
                var journalPath = Path.Combine(dir, "journal.json");
                if (!File.Exists(journalPath))
                {
                    continue;
                }

                TransactionJournalRecord journal;
                try
                {
                    journal = JsonConvert.DeserializeObject<TransactionJournalRecord>(File.ReadAllText(journalPath));
                }
                catch
                {
                    continue;
                }

                if (TransactionJournal.IsTerminal(journal))
                {
                    continue;
                }

                status?.Report($"Recovering transaction {journal.transaction_id}…");
                journal.phase = TransactionPhases.Recovering;
                TransactionJournal.Save(journal);
                try
                {
                    RestoreSnapshot(journal);
                    CleanupStaging(journal);
                    TransactionJournal.Advance(journal, TransactionPhases.Discarded, "recovered");
                    recovered++;
                    BridgeLog.Info($"Recovered incomplete journal {journal.transaction_id} by restoring snapshot.");
                }
                catch (Exception ex)
                {
                    TransactionJournal.Fail(journal, ex);
                    BridgeLog.Exception(ex, "Recover journal " + journal.transaction_id);
                }
            }

            await Task.Yield();
            return recovered;
        }

        static void EnsureUpdatesEnabled()
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_enabled")
                || !FeatureFlagService.IsEnabled("unity_bridge_updates_enabled"))
            {
                throw new InvalidOperationException("Updates are disabled by feature flags.");
            }
        }

        static void SnapshotInstalled(TransactionJournalRecord journal)
        {
            var installed = ManagedPaths.InstalledAssetDir(journal.asset_id);
            var installedFull = ToFull(installed);
            Directory.CreateDirectory(journal.snapshot_dir);
            if (!Directory.Exists(installedFull))
            {
                return;
            }

            CopyDirectory(installedFull, journal.snapshot_dir);
            var lockPath = ManagedPaths.LockFilePath;
            if (File.Exists(lockPath))
            {
                File.Copy(lockPath, Path.Combine(journal.snapshot_dir, "Unslop.lock.json"), true);
            }
        }

        static void RestoreSnapshot(TransactionJournalRecord journal)
        {
            if (string.IsNullOrEmpty(journal.snapshot_dir) || !Directory.Exists(journal.snapshot_dir))
            {
                return;
            }

            var installed = ManagedPaths.InstalledAssetDir(journal.asset_id);
            var installedFull = ToFull(installed);
            if (Directory.Exists(installedFull))
            {
                FileUtil.DeleteFileOrDirectory(installedFull);
                FileUtil.DeleteFileOrDirectory(installedFull + ".meta");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(installedFull) ?? ManagedPaths.ProjectRoot);
            CopyDirectory(journal.snapshot_dir, installedFull);

            // Remove lock snapshot copy from installed tree if present
            var nestedLock = Path.Combine(installedFull, "Unslop.lock.json");
            if (File.Exists(nestedLock))
            {
                var lockSnapshot = Path.Combine(journal.snapshot_dir, "Unslop.lock.json");
                if (File.Exists(lockSnapshot))
                {
                    File.Copy(lockSnapshot, ManagedPaths.LockFilePath, true);
                }

                File.Delete(nestedLock);
            }

            if (!string.IsNullOrEmpty(journal.lock_entry_json))
            {
                try
                {
                    var settings = UnslopProjectSettings.EnsureExists();
                    var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
                    var entry = JsonConvert.DeserializeObject<LockAssetEntry>(journal.lock_entry_json);
                    if (entry != null)
                    {
                        LockFileService.UpsertAsset(lockFile, journal.asset_id, entry);
                    }
                }
                catch (Exception ex)
                {
                    BridgeLog.Warn("Could not restore lock entry: " + BridgeLog.Redact(ex.Message));
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void CleanupStaging(TransactionJournalRecord journal)
        {
            if (string.IsNullOrEmpty(journal.staging_path))
            {
                return;
            }

            var full = ToFull(journal.staging_path);
            if (Directory.Exists(full))
            {
                FileUtil.DeleteFileOrDirectory(full);
                FileUtil.DeleteFileOrDirectory(full + ".meta");
            }
        }

        static MaterialsManifest LoadMaterialsJson(string installedRoot)
        {
            var path = Path.Combine(
                ManagedPaths.ProjectRoot,
                installedRoot.Replace('/', Path.DirectorySeparatorChar),
                "materials.json");
            if (!File.Exists(path))
            {
                return new MaterialsManifest();
            }

            try
            {
                return JsonConvert.DeserializeObject<MaterialsManifest>(File.ReadAllText(path))
                       ?? new MaterialsManifest();
            }
            catch
            {
                return new MaterialsManifest();
            }
        }

        static string FindInstalledModel(string installedRoot)
        {
            var preferred = $"{installedRoot}/model.fbx";
            if (File.Exists(ToFull(preferred)))
            {
                return preferred;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { installedRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        static string ToFull(string assetPath) =>
            Path.Combine(ManagedPaths.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        static void CopyAssetPreservingMeta(string sourceAssetPath, string destAssetPath)
        {
            var src = ToFull(sourceAssetPath);
            var dst = ToFull(destAssetPath);
            if (!File.Exists(src))
            {
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

        static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(source, dest));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = file.Replace(source, dest);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dest);
                File.Copy(file, target, true);
            }
        }
    }
}
