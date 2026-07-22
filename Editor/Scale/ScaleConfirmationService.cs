using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Importing;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Scale
{
    public enum ScaleConfirmationDerivedStatus
    {
        Confirmed,
        Unverified,
        Stale,
        NewVersionUnconfirmed
    }

    public sealed class ScaleConfirmationResult
    {
        public ScaleConfirmationDto Confirmation { get; set; }
        public ScaleConfirmationDerivedStatus DerivedStatus { get; set; }
        public string BadgeLabel { get; set; }
        public string Message { get; set; }
        public ScaleMeasurement Measurement { get; set; }
    }

    /// <summary>
    /// Confirm Scale in Unity — evidence payload + online badge; derives stale/unverified/new-version status.
    /// </summary>
    public sealed class ScaleConfirmationService
    {
        readonly IUnslopApiClient _api;
        readonly IScaleMeasurementService _measurement;

        public ScaleConfirmationService(IUnslopApiClient api = null, IScaleMeasurementService measurement = null)
        {
            _api = api ?? BridgeServices.CreateApiClient();
            _measurement = measurement ?? new ScaleMeasurementService();
        }

        public async Task<ScaleConfirmationResult> ConfirmAsync(
            GameObject wrapperInstance,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_scale_confirmation"))
            {
                throw new InvalidOperationException("unity_bridge_scale_confirmation is off.");
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
            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            lockFile.assets.TryGetValue(reference.AssetId, out var entry);

            var measured = _measurement.MeasureRendererBounds(wrapperInstance);
            var tolerance = _measurement.DefaultToleranceMetres;
            var request = new ScaleConfirmationCreateDto
            {
                asset_version_id = reference.InstalledVersionId,
                physical_spec_id = reference.PhysicalSpecId ?? entry?.physical_spec_id,
                engine = "unity",
                measured_dimensions_metres = measured.ToArray(),
                tolerance_metres = new[] { tolerance.x, tolerance.y, tolerance.z },
                unity_version = Application.unityVersion,
                bridge_version = PackageInfo.Version,
                render_pipeline = BridgeServices.CreateClientContext().render_pipeline,
                import_profile_hash = entry?.import_profile_hash ?? ImportProfile.ComputeImportProfileHash(),
                project_id = settings.BoundProjectId
            };

            var confirmation = await _api.SubmitScaleConfirmationAsync(
                reference.AssetId,
                request,
                Guid.NewGuid().ToString("N"),
                cancellationToken);

            var derived = await DeriveStatusAsync(reference, entry, cancellationToken);
            var badge = confirmation?.badge?.label
                        ?? (derived == ScaleConfirmationDerivedStatus.Confirmed ? "Unity scale confirmed" : derived.ToString());

            BridgeLog.Info($"Scale confirmation for {reference.AssetId}: {badge}");
            return new ScaleConfirmationResult
            {
                Confirmation = confirmation,
                DerivedStatus = derived,
                BadgeLabel = badge,
                Measurement = measured,
                Message = $"Confirmation status={confirmation?.status ?? "unknown"}; derived={derived}"
            };
        }

        public async Task<ScaleConfirmationDerivedStatus> DeriveStatusAsync(
            UnslopAssetReference reference,
            LockAssetEntry entry = null,
            CancellationToken cancellationToken = default)
        {
            if (reference == null || string.IsNullOrEmpty(reference.AssetId))
            {
                return ScaleConfirmationDerivedStatus.Unverified;
            }

            try
            {
                var asset = await _api.GetAssetAsync(reference.AssetId, cancellationToken);
                var page = await _api.ListScaleConfirmationsAsync(reference.AssetId, "unity", null, cancellationToken);
                var latest = page?.data?.FirstOrDefault();

                if (latest == null || !string.Equals(latest.status, "confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    return ScaleConfirmationDerivedStatus.Unverified;
                }

                var confirmedVersion = latest.badge?.asset_version_id;
                var confirmedSpec = latest.badge?.physical_spec_id;
                var installedVersion = reference.InstalledVersionId ?? entry?.installed_version_id;
                var installedSpec = reference.PhysicalSpecId ?? entry?.physical_spec_id;

                if (!string.IsNullOrEmpty(asset?.recommended_version_id)
                    && !string.Equals(asset.recommended_version_id, installedVersion, StringComparison.Ordinal)
                    && !string.Equals(confirmedVersion, asset.recommended_version_id, StringComparison.Ordinal))
                {
                    return ScaleConfirmationDerivedStatus.NewVersionUnconfirmed;
                }

                if (!string.Equals(confirmedVersion, installedVersion, StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(installedSpec)
                        && !string.Equals(confirmedSpec, installedSpec, StringComparison.Ordinal)))
                {
                    return ScaleConfirmationDerivedStatus.Stale;
                }

                return ScaleConfirmationDerivedStatus.Confirmed;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn("Scale status derivation failed: " + BridgeLog.Redact(ex.Message));
                return ScaleConfirmationDerivedStatus.Unverified;
            }
        }
    }
}
