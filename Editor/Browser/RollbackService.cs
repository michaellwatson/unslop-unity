using System;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;
using Unslop.UnityBridge.Editor.Transactions;

namespace Unslop.UnityBridge.Editor.Browser
{
    public sealed class RollbackResult
    {
        public TransitionSession Session { get; set; }
        public string Message { get; set; }
        public bool WithdrawalWarning { get; set; }
    }

    /// <summary>
    /// Version history rollback via the same staged transition pipeline; pins and candidate decisions.
    /// Project rollback does not change the global recommended version.
    /// </summary>
    public sealed class RollbackService
    {
        readonly IUnslopApiClient _api;
        readonly AssetTransitionCoordinator _coordinator;

        public RollbackService(IUnslopApiClient api = null, AssetTransitionCoordinator coordinator = null)
        {
            _api = api ?? BridgeServices.CreateApiClient();
            _coordinator = coordinator ?? new AssetTransitionCoordinator(_api);
        }

        public async Task<RollbackResult> StageRollbackAsync(
            string assetId,
            string historicalVersionId,
            IProgress<string> status = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_rollback"))
            {
                throw new InvalidOperationException("unity_bridge_rollback is off.");
            }

            var detail = await _api.GetAssetVersionAsync(assetId, historicalVersionId, cancellationToken);
            var withdrawal = string.Equals(detail.state, "withdrawn", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(detail.state, "yanked", StringComparison.OrdinalIgnoreCase);

            if (withdrawal)
            {
                BridgeLog.Warn($"Staging rollback to withdrawn version {historicalVersionId} for {assetId}.");
            }

            status?.Report("Preparing rollback transition…");
            var session = await _coordinator.PrepareUpdateAsync(
                assetId,
                historicalVersionId,
                "rollback",
                status,
                cancellationToken);
            await _coordinator.SnapshotAndStageAsync(session, status, cancellationToken);

            return new RollbackResult
            {
                Session = session,
                WithdrawalWarning = withdrawal,
                Message = withdrawal
                    ? $"Staged rollback to withdrawn version {historicalVersionId}. Review carefully before accept. Global recommended version is unchanged."
                    : $"Staged rollback to {historicalVersionId}. Accept explicitly to apply. Global recommended version is unchanged."
            };
        }

        public Task AcceptRollbackAsync(TransitionSession session, IProgress<string> status = null, CancellationToken cancellationToken = default)
            => _coordinator.AcceptAsync(session, status, cancellationToken);

        public Task DiscardRollbackAsync(TransitionSession session, IProgress<string> status = null)
            => _coordinator.DiscardAsync(session, status);

        public async Task PinAsync(
            string assetId,
            string versionId,
            string reason = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_rollback"))
            {
                throw new InvalidOperationException("unity_bridge_rollback is off.");
            }

            var settings = UnslopProjectSettings.EnsureExists();
            await _api.PinAssetAsync(
                settings.BoundProjectId,
                assetId,
                new PinRequestDto
                {
                    version_id = versionId,
                    reason = reason ?? "Pinned from Unity Bridge",
                    mode = "manual"
                },
                Guid.NewGuid().ToString("N"),
                cancellationToken);

            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            if (lockFile.assets.TryGetValue(assetId, out var entry))
            {
                entry.pin = new LockPin
                {
                    mode = "manual",
                    reason = reason ?? "Pinned from Unity Bridge",
                    version_id = versionId
                };
                LockFileService.UpsertAsset(lockFile, assetId, entry);
            }

            BridgeLog.Info($"Pinned {assetId} to {versionId} (local + online).");
        }

        public async Task UnpinAsync(string assetId, CancellationToken cancellationToken = default)
        {
            var settings = UnslopProjectSettings.EnsureExists();
            await _api.UnpinAssetAsync(settings.BoundProjectId, assetId, cancellationToken);

            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            if (lockFile.assets.TryGetValue(assetId, out var entry))
            {
                entry.pin = null;
                LockFileService.UpsertAsset(lockFile, assetId, entry);
            }

            BridgeLog.Info($"Unpinned {assetId}.");
        }

        public async Task RecordCandidateDecisionAsync(
            string assetId,
            string candidateVersionId,
            string decision,
            string reason = null,
            CancellationToken cancellationToken = default)
        {
            var settings = UnslopProjectSettings.EnsureExists();
            await _api.RecordCandidateDecisionAsync(
                settings.BoundProjectId,
                assetId,
                new CandidateDecisionDto
                {
                    candidate_version_id = candidateVersionId,
                    decision = decision,
                    reason = reason
                },
                Guid.NewGuid().ToString("N"),
                cancellationToken);

            BridgeLog.Info($"Candidate decision for {assetId}: {decision} → {candidateVersionId}");
        }
    }
}
