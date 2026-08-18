using System;
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

        public Task<CanonicalScaleResult> AcceptCurrentScaleAsync(
            GameObject wrapperInstance,
            string ifMatchEtag = null,
            CancellationToken cancellationToken = default)
        {
            return SetCurrentSizeAsCanonicalAsync(
                wrapperInstance,
                ifMatchEtag,
                "No resize needed — accepted current size as canonical from Unity",
                cancellationToken,
                acceptCurrent: true);
        }

        public async Task<CanonicalScaleResult> SetCurrentSizeAsCanonicalAsync(
            GameObject wrapperInstance,
            string ifMatchEtag = null,
            string note = null,
            CancellationToken cancellationToken = default,
            bool acceptCurrent = false)
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
            BridgeLog.Info(
                $"Scale measure asset={reference.AssetId} version={reference.InstalledVersionId} " +
                $"renderers={measured.RendererCount} size_m=({measured.SizeMetres.x:F3},{measured.SizeMetres.y:F3},{measured.SizeMetres.z:F3}) " +
                $"center=({measured.Centre.x:F3},{measured.Centre.y:F3},{measured.Centre.z:F3}) " +
                $"rootScale={wrapperInstance.transform.lossyScale} visualCorrection={currentVisual}");
            MeshImportDiagnostics.LogGameObjectMeshBounds("Scale measure hierarchy", wrapperInstance);

            var maxAxis = Mathf.Max(measured.SizeMetres.x, measured.SizeMetres.y, measured.SizeMetres.z);
            if (maxAxis > 80f)
            {
                throw new InvalidOperationException(
                    $"Measured size {measured.SizeMetres} m looks like a file-scale glitch (prefab not in a scene). " +
                    "Select the wrapper in the Hierarchy (scene instance), not the Project prefab, and try again.");
            }
            var localMeta = LockFileService.LoadLocalMetadata(reference.AssetId);

            // Record the correction currently on the wrapper, not a stale lock-file value.
            // Accept-current with identity VisualCorrection must send 1,1,1 so the server does not re-bake.
            var proposedSource = acceptCurrent && ScaleMeasurementService.ApproximatelyIdentityScale(currentVisual)
                ? Vector3.one
                : currentVisual;
            var proposedCorrection = new[] { proposedSource.x, proposedSource.y, proposedSource.z };

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

                var specId = revision?.ResolvedId;
                if (string.IsNullOrWhiteSpace(specId))
                {
                    // Fallback: re-read asset pointer if create response omitted the id field.
                    var refreshed = await _api.GetAssetAsync(reference.AssetId, cancellationToken);
                    specId = refreshed?.current_physical_spec_id;
                }

                if (string.IsNullOrWhiteSpace(specId))
                {
                    throw new InvalidOperationException(
                        "Canonical size was accepted but the API did not return a physical_spec_id. Refresh the asset and try Confirm Scale after the Physical Spec Id appears.");
                }

                ResetVisualCorrectionAfterCanonicalWrite(wrapperInstance, visual, currentVisual);

                if (localMeta != null)
                {
                    localMeta.physical_spec_id = specId;
                    localMeta.visual_correction = new[] { 1f, 1f, 1f };
                    LockFileService.SaveLocalMetadata(localMeta);
                }

                var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
                if (lockFile.assets.TryGetValue(reference.AssetId, out var entry))
                {
                    entry.physical_spec_id = specId;
                    LockFileService.UpsertAsset(lockFile, reference.AssetId, entry);
                }

                reference.Configure(
                    reference.AssetId,
                    reference.InstalledVersionId,
                    specId,
                    reference.WrapperPrefabGuid);
                EditorUtility.SetDirty(reference);
                PrefabUtility.RecordPrefabInstancePropertyModifications(reference);

                BridgeLog.Info(
                    $"Canonical scale written for {reference.AssetId} spec={specId} pending={revision?.artist_correction_pending}");

                return new CanonicalScaleResult
                {
                    Revision = revision,
                    MeasuredMetres = measured.SizeMetres,
                    AppliedVisualCorrection = Vector3.one,
                    ArtistCorrectionPending = revision != null && revision.artist_correction_pending,
                    Message = BuildSuccessMessage(specId, revision, acceptCurrent)
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

        public static bool ConfirmAcceptCurrentScale(GameObject wrapper)
        {
            if (wrapper == null)
            {
                EditorUtility.DisplayDialog("Unslop", "Select an Unslop wrapper in the Hierarchy.", "OK");
                return false;
            }

            ScaleMeasurement measured;
            try
            {
                measured = new ScaleMeasurementService().MeasureRendererBounds(wrapper);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Unslop", ex.Message, "OK");
                return false;
            }

            if (measured.RendererCount == 0)
            {
                EditorUtility.DisplayDialog("Unslop", "No renderers found to measure.", "OK");
                return false;
            }

            var visual = ScaleMeasurementService.FindVisualCorrection(wrapper);
            var currentVisual = visual != null ? visual.localScale : Vector3.one;
            var size = measured.SizeMetres;
            var vcLine = ScaleMeasurementService.ApproximatelyIdentityScale(currentVisual)
                ? "VisualCorrection is 1,1,1 (no resize)."
                : $"VisualCorrection is ({currentVisual.x:F3}, {currentVisual.y:F3}, {currentVisual.z:F3}). That scale is included in the measured size and will reset to 1,1,1 after write.";

            return EditorUtility.DisplayDialog(
                "Accept Current Scale",
                $"Measured size: {size.x:F3} × {size.y:F3} × {size.z:F3} m\n{vcLine}\n\nWrite this as the canonical size? No extra resize will be applied.",
                "Accept",
                "Cancel");
        }

        static string BuildSuccessMessage(string specId, PhysicalSpecRevisionDto revision, bool acceptCurrent)
        {
            var pending = revision != null && revision.artist_correction_pending;
            var head = acceptCurrent
                ? $"Current scale accepted (spec {ShortId(specId)})."
                : $"Canonical size set (spec {ShortId(specId)}).";
            return pending
                ? head + " Artist correction pending on server — you can still Confirm Scale in Unity."
                : head;
        }

        static void ResetVisualCorrectionAfterCanonicalWrite(
            GameObject wrapperInstance,
            Transform visual,
            Vector3 currentVisual)
        {
            if (visual == null || ScaleMeasurementService.ApproximatelyIdentityScale(currentVisual))
            {
                BridgeLog.Info("VisualCorrection already 1,1,1 — leaving transforms unchanged.");
                return;
            }

            // Transfer: after canonical write, visual correction resets to 1 so scale does not compound.
            // Prefab assets must be edited via LoadPrefabContents; mutating the Project asset in place
            // can wipe FBX file-scale and explode the nested model.
            if (EditorUtility.IsPersistent(wrapperInstance))
            {
                var path = AssetDatabase.GetAssetPath(wrapperInstance);
                VisualCorrectionReset.ResetOnPrefab(path);
                return;
            }

            VisualCorrectionReset.ResetInstanceKeepingWorldAnchor(wrapperInstance);
        }

        static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "—";
            }

            return id.Length <= 12 ? id : id.Substring(0, 8) + "…" + id.Substring(id.Length - 4);
        }
    }
}
