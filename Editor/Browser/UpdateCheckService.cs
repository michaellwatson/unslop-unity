using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Importing;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;

namespace Unslop.UnityBridge.Editor.Browser
{
    public sealed class UpdateCheckResult
    {
        public AssetUpdateCheckResponseDto Response { get; set; }
        public IReadOnlyList<UpdateCandidate> Candidates { get; set; } = Array.Empty<UpdateCandidate>();
        public string CheckedAt { get; set; }
    }

    public sealed class UpdateCandidate
    {
        public string AssetId { get; set; }
        public string InstalledVersionId { get; set; }
        public string RecommendedVersionId { get; set; }
        public string UpdateStatus { get; set; }
        public string ConfirmationStatus { get; set; }
        public bool IsPinned { get; set; }
        public bool HasUpdate =>
            !string.IsNullOrEmpty(RecommendedVersionId)
            && !string.Equals(InstalledVersionId, RecommendedVersionId, StringComparison.Ordinal)
            && !string.Equals(UpdateStatus, "up_to_date", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class StagedCandidateResult
    {
        public string AssetId { get; set; }
        public string VersionId { get; set; }
        public string StagingAssetPath { get; set; }
        public string ImportProfileHash { get; set; }
        public string ModelAssetPath { get; set; }
        public string ManifestSha256 { get; set; }
    }

    /// <summary>
    /// Batch asset-update-checks against the lock file and optional candidate staging.
    /// </summary>
    public sealed class UpdateCheckService
    {
        readonly IUnslopApiClient _api;

        public UpdateCheckService(IUnslopApiClient api = null)
        {
            _api = api ?? BridgeServices.CreateApiClient();
        }

        public async Task<UpdateCheckResult> CheckInstalledAsync(
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_enabled")
                || !FeatureFlagService.IsEnabled("unity_bridge_updates_enabled"))
            {
                throw new InvalidOperationException("Update checks are disabled by feature flags.");
            }

            var settings = UnslopProjectSettings.EnsureExists();
            if (string.IsNullOrWhiteSpace(settings.BoundProjectId))
            {
                throw new InvalidOperationException("Bind a project before checking updates.");
            }

            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            var request = new AssetUpdateCheckRequestDto
            {
                client = BridgeServices.CreateClientContext(),
                assets = lockFile.assets.Select(kv => new AssetUpdateCheckItemDto
                {
                    asset_id = kv.Key,
                    installed_version_id = kv.Value.installed_version_id,
                    physical_spec_id = kv.Value.physical_spec_id,
                    import_profile_hash = kv.Value.import_profile_hash,
                    state_hash = kv.Value.state_hash,
                    pinned = kv.Value.pin != null && !string.IsNullOrEmpty(kv.Value.pin.version_id)
                }).ToList()
            };

            if (request.assets.Count == 0)
            {
                status?.Report("No installed assets in lock file.");
                return new UpdateCheckResult
                {
                    CheckedAt = DateTime.UtcNow.ToString("o"),
                    Response = new AssetUpdateCheckResponseDto { checked_at = DateTime.UtcNow.ToString("o") },
                    Candidates = Array.Empty<UpdateCandidate>()
                };
            }

            status?.Report($"Checking updates for {request.assets.Count} asset(s)…");
            var response = await _api.AssetUpdateChecksAsync(
                settings.BoundProjectId,
                request,
                Guid.NewGuid().ToString("N"),
                cancellationToken);

            var pinLookup = lockFile.assets.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.pin != null && !string.IsNullOrEmpty(kv.Value.pin.version_id),
                StringComparer.Ordinal);

            var candidates = (response?.assets ?? new List<AssetUpdateStatusDto>())
                .Select(a => new UpdateCandidate
                {
                    AssetId = a.asset_id,
                    InstalledVersionId = a.installed_version_id,
                    RecommendedVersionId = a.recommended_version_id,
                    UpdateStatus = a.update_status,
                    ConfirmationStatus = a.confirmation_status,
                    IsPinned = pinLookup.TryGetValue(a.asset_id, out var pinned) && pinned
                })
                .ToList();

            BridgeLog.Info($"Update check complete: {candidates.Count(c => c.HasUpdate)} update(s) available.");
            return new UpdateCheckResult
            {
                Response = response,
                Candidates = candidates,
                CheckedAt = response?.checked_at ?? DateTime.UtcNow.ToString("o")
            };
        }

        /// <summary>
        /// Resolve a candidate version, download, and materialise into staging (no accept).
        /// </summary>
        public async Task<StagedCandidateResult> StageCandidateAsync(
            string assetId,
            string versionId,
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_updates_enabled"))
            {
                throw new InvalidOperationException("unity_bridge_updates_enabled is off.");
            }

            status?.Report("Fetching candidate version…");
            var detail = await _api.GetAssetVersionAsync(assetId, versionId, cancellationToken);
            var downloader = new DownloadManager(_api);
            status?.Report("Downloading candidate…");
            var download = await downloader.DownloadVersionAsync(assetId, versionId, detail, null, cancellationToken);
            status?.Report("Materialising staging…");
            var staging = StagingMaterialiser.Materialise(download.DownloadRoot, download.Manifest, assetId, versionId);

            return new StagedCandidateResult
            {
                AssetId = assetId,
                VersionId = versionId,
                StagingAssetPath = staging.StagingAssetPath,
                ImportProfileHash = staging.ImportProfileHash,
                ModelAssetPath = staging.ModelAssetPath,
                ManifestSha256 = download.ManifestSha256
            };
        }
    }
}
