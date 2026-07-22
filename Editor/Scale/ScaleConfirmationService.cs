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
using UnityEditor;
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
            var localMeta = LockFileService.LoadLocalMetadata(reference.AssetId);

            var versionId = FirstNonEmpty(
                reference.InstalledVersionId,
                entry?.installed_version_id);
            var physicalSpecId = FirstNonEmpty(
                reference.PhysicalSpecId,
                entry?.physical_spec_id,
                localMeta?.physical_spec_id);

            if (string.IsNullOrWhiteSpace(physicalSpecId))
            {
                // After Set Canonical Scale the asset pointer should hold the new spec.
                try
                {
                    var asset = await _api.GetAssetAsync(reference.AssetId, cancellationToken);
                    physicalSpecId = asset?.current_physical_spec_id;
                    if (string.IsNullOrWhiteSpace(physicalSpecId))
                    {
                        var revisions = await _api.ListPhysicalSpecRevisionsAsync(
                            reference.AssetId, null, cancellationToken);
                        physicalSpecId = revisions?.data?.FirstOrDefault()?.ResolvedId;
                    }
                }
                catch (Exception ex)
                {
                    BridgeLog.Warn("Could not resolve physical_spec_id before confirm: " + BridgeLog.Redact(ex.Message));
                }
            }

            if (string.IsNullOrWhiteSpace(physicalSpecId))
            {
                throw new InvalidOperationException(
                    "Confirm Scale needs a physical_spec_id. Run Set Canonical Scale first, then Confirm Scale once Physical Spec Id is filled in on UnslopAssetReference.");
            }

            if (string.IsNullOrWhiteSpace(versionId))
            {
                throw new InvalidOperationException("Confirm Scale needs an installed version id on the wrapper.");
            }

            // Persist resolved id onto the wrapper so the Inspector stops showing empty.
            if (!string.Equals(reference.PhysicalSpecId, physicalSpecId, StringComparison.Ordinal))
            {
                reference.Configure(
                    reference.AssetId,
                    versionId,
                    physicalSpecId,
                    reference.WrapperPrefabGuid);
                EditorUtility.SetDirty(reference);
                PrefabUtility.RecordPrefabInstancePropertyModifications(reference);
            }

            var measured = _measurement.MeasureRendererBounds(wrapperInstance);
            var tolerance = _measurement.DefaultToleranceMetres;
            BridgeLog.Info(
                $"Scale confirm asset={reference.AssetId} version={versionId} spec={physicalSpecId} " +
                $"size_m=({measured.SizeMetres.x:F3},{measured.SizeMetres.y:F3},{measured.SizeMetres.z:F3}) " +
                $"tolerance=({tolerance.x:F3},{tolerance.y:F3},{tolerance.z:F3}) " +
                $"rootScale={wrapperInstance.transform.lossyScale}");
            MeshImportDiagnostics.LogGameObjectMeshBounds("Scale confirm hierarchy", wrapperInstance);
            var client = BridgeServices.CreateClientContext();
            var request = new ScaleConfirmationCreateDto
            {
                asset_version_id = versionId,
                physical_spec_id = physicalSpecId,
                engine = "unity",
                measured_dimensions_metres = measured.ToArray(),
                tolerance_metres = new[] { tolerance.x, tolerance.y, tolerance.z },
                engine_version = Application.unityVersion,
                unity_version = Application.unityVersion,
                bridge_version = BridgePackageInfo.Version,
                render_backend = client.render_pipeline,
                render_pipeline = client.render_pipeline,
                import_profile_hash = entry?.import_profile_hash ?? ImportProfile.ComputeImportProfileHash(),
                project_id = settings.BoundProjectId,
                client = client
            };

            ScaleConfirmationDto confirmation;
            try
            {
                confirmation = await _api.SubmitScaleConfirmationAsync(
                    reference.AssetId,
                    request,
                    Guid.NewGuid().ToString("N"),
                    cancellationToken);
            }
            catch (UnslopApiException ex) when (ex.StatusCode == 404)
            {
                throw new InvalidOperationException(
                    "Scale confirmation endpoint returned 404. Usually the physical_spec_id is missing or unknown to the server. " +
                    $"Tried spec={physicalSpecId}, version={versionId}. Re-run Set Canonical Scale, then Confirm Scale again. " +
                    $"API detail: {ex.Message}",
                    ex);
            }

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
