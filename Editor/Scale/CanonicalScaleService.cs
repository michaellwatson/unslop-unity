using System;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Scale
{
    public sealed class CanonicalScaleResult
    {
        public PhysicalSpecRevisionDto Revision { get; set; }
        public Vector3 MeasuredMetres { get; set; }
        public Vector3 AppliedVisualCorrection { get; set; }
        public bool ArtistCorrectionPending { get; set; }
        public string Message { get; set; }
        public bool Conflict412 { get; set; }
    }

    /// <summary>
    /// Set Current Size as Canonical → physical-spec revision (If-Match ETag) with non-compounding visual correction transfer.
    /// </summary>
    public sealed class CanonicalScaleService
    {
        readonly IUnslopApiClient _api;
        readonly IScaleMeasurementService _measurement;

        public CanonicalScaleService(IUnslopApiClient api = null, IScaleMeasurementService measurement = null)
        {
            _api = api ?? BridgeServices.CreateApiClient();
            _measurement = measurement ?? new ScaleMeasurementService();
        }

        public async Task<CanonicalScaleResult> SetCurrentSizeAsCanonicalAsync(
            GameObject wrapperInstance,
            string ifMatchEtag = null,
            string note = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_canonical_scale_write"))
            {
                throw new InvalidOperationException("unity_bridge_canonical_scale_write is off.");
            }

            if (wrapperInstance == null)
            {
                throw new ArgumentNullException(nameof(wrapperInstance));
            }

            var reference = wrapperInstance.GetComponent<UnslopAssetReference>()
                            ?? wrapperInstance.GetComponentInChildren<UnslopAssetReference>();
            if (reference == null || string.IsNullOrEmpty(reference.AssetId))
            {
                throw new InvalidOperationException("Select an Unslop wrapper with UnslopAssetReference.");
            }

            var settings = UnslopProjectSettings.EnsureExists();
            var measured = _measurement.MeasureRendererBounds(wrapperInstance);
            if (measured.RendererCount == 0)
            {
                throw new InvalidOperationException("No renderers found to measure.");
            }

            var visual = ScaleMeasurementService.FindVisualCorrection(wrapperInstance);
            var currentVisual = visual != null ? visual.localScale : Vector3.one;
            var localMeta = LockFileService.LoadLocalMetadata(reference.AssetId);
            var previousCorrection = localMeta?.visual_correction != null && localMeta.visual_correction.Length == 3
                ? new Vector3(localMeta.visual_correction[0], localMeta.visual_correction[1], localMeta.visual_correction[2])
                : currentVisual;

            // Non-compounding: bake measured size as canonical; reset visual correction toward identity
            // while recording the transferred correction in the revision payload.
            var proposedCorrection = new[]
            {
                previousCorrection.x,
                previousCorrection.y,
                previousCorrection.z
            };

            var asset = await _api.GetAssetAsync(reference.AssetId, cancellationToken);
            var etag = ifMatchEtag ?? asset?.physical_spec_etag;

            var create = new PhysicalSpecCreateDto
            {
                dimensions_metres = measured.ToArray(),
                up_axis = "Y",
                forward_axis = "Z",
                pivot_policy = "bottom_centre",
                note = note ?? "Set current size as canonical from Unity",
                origin = new PhysicalSpecOriginDto
                {
                    type = "unity_canonical_correction",
                    project_id = settings.BoundProjectId,
                    asset_version_id = reference.InstalledVersionId,
                    unity_version = Application.unityVersion,
                    bridge_version = BridgePackageInfo.Version
                },
                measurement = new PhysicalSpecMeasurementDto
                {
                    measured_dimensions_metres = measured.ToArray(),
                    source_dimensions_metres = measured.ToArray(),
                    proposed_visual_correction = proposedCorrection
                }
            };

            try
            {
                var revision = await _api.CreatePhysicalSpecRevisionAsync(
                    reference.AssetId,
                    create,
                    etag,
                    Guid.NewGuid().ToString("N"),
                    cancellationToken);

                if (visual != null)
                {
                    // Transfer: after canonical write, visual correction resets to 1 so scale does not compound.
                    visual.localScale = Vector3.one;
                    EditorUtility.SetDirty(wrapperInstance);
                }

                if (localMeta != null)
                {
                    localMeta.physical_spec_id = revision.physical_spec_id;
                    localMeta.visual_correction = new[] { 1f, 1f, 1f };
                    LockFileService.SaveLocalMetadata(localMeta);
                }

                var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
                if (lockFile.assets.TryGetValue(reference.AssetId, out var entry))
                {
                    entry.physical_spec_id = revision.physical_spec_id;
                    LockFileService.UpsertAsset(lockFile, reference.AssetId, entry);
                }

                reference.Configure(
                    reference.AssetId,
                    reference.InstalledVersionId,
                    revision.physical_spec_id,
                    reference.WrapperPrefabGuid);
                EditorUtility.SetDirty(reference);

                BridgeLog.Info(
                    $"Canonical scale written for {reference.AssetId} spec={revision.physical_spec_id} pending={revision.artist_correction_pending}");

                return new CanonicalScaleResult
                {
                    Revision = revision,
                    MeasuredMetres = measured.SizeMetres,
                    AppliedVisualCorrection = Vector3.one,
                    ArtistCorrectionPending = revision.artist_correction_pending,
                    Message = revision.artist_correction_pending
                        ? "Canonical size set. Artist correction pending on server."
                        : "Canonical size set."
                };
            }
            catch (UnslopApiException ex) when (ex.IsPreconditionFailed)
            {
                BridgeLog.Warn($"Physical spec If-Match conflict (412) for {reference.AssetId}. correlation={ex.CorrelationId}");
                return new CanonicalScaleResult
                {
                    Conflict412 = true,
                    MeasuredMetres = measured.SizeMetres,
                    Message = "Physical spec changed remotely (HTTP 412). Refresh and retry with the latest ETag — local size was not overwritten."
                };
            }
        }
    }
}
