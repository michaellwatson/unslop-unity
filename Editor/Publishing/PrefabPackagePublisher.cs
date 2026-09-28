using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Manifests;

namespace Unslop.UnityBridge.Editor.Publishing
{
    /// <summary>
    /// Exports a Unity prefab to a Bridge package and publishes it as a catalog asset version.
    /// </summary>
    public sealed class PrefabPackagePublisher
    {
        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        readonly IUnslopApiClient _api;

        public PrefabPackagePublisher(IUnslopApiClient api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public async Task<PrefabPublishResult> PublishAsync(
            PrefabPublishRequest request,
            IProgress<string> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Prefab == null)
            {
                throw new ArgumentException("Prefab is required.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ProjectId))
            {
                throw new ArgumentException("ProjectId is required.", nameof(request));
            }

            progress?.Report("Exporting prefab package…");
            var package = PrefabPackageExporter.Export(request.Prefab, request.DisplayName);
            var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? package.DisplayName
                : request.DisplayName.Trim();

            string assetId = request.ExistingAssetId;
            AssetSummaryDto asset = null;
            if (string.IsNullOrWhiteSpace(assetId))
            {
                progress?.Report("Creating catalog asset…");
                asset = await _api.CreateProjectAssetAsync(
                    request.ProjectId,
                    new CreateAssetDto
                    {
                        display_name = displayName,
                        kind = "mesh",
                        api_available = true
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                assetId = asset.asset_id;
            }
            else
            {
                asset = await _api.GetAssetAsync(assetId, cancellationToken).ConfigureAwait(true);
            }

            progress?.Report("Creating draft version…");
            var draft = await _api.CreateDraftVersionAsync(assetId, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            var versionId = draft.asset_version_id;

            var uploaded = new List<UploadedFileDto>();
            UploadedFileDto modelUpload = null;

            foreach (var file in package.Files)
            {
                progress?.Report($"Uploading {file.RelativePath}…");
                var result = await _api.UploadVersionFileAsync(
                    assetId,
                    versionId,
                    file.RelativePath,
                    file.Role,
                    file.MediaType,
                    file.Contents,
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                uploaded.Add(result);
                if (string.Equals(file.Role, "model", StringComparison.OrdinalIgnoreCase))
                {
                    modelUpload = result;
                }
            }

            if (modelUpload == null)
            {
                throw new InvalidOperationException("Package is missing a model file upload.");
            }

            var materialsJson = PrefabPackageManifestBuilder.ToMaterialsDictionary(package.Materials);
            var assetJson = PrefabPackageManifestBuilder.BuildAssetJson(
                assetId,
                versionId,
                displayName,
                modelUpload,
                uploaded);

            progress?.Report("Uploading asset.json…");
            var assetBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(assetJson, JsonSettings));
            var assetUpload = await _api.UploadVersionFileAsync(
                assetId,
                versionId,
                "asset.json",
                "asset_manifest",
                "application/json",
                assetBytes,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            assetJson = PrefabPackageManifestBuilder.PatchAssetJsonFileRow(assetJson, assetUpload);
            assetBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(assetJson, JsonSettings));
            assetUpload = await _api.UploadVersionFileAsync(
                assetId,
                versionId,
                "asset.json",
                "asset_manifest",
                "application/json",
                assetBytes,
                cancellationToken: cancellationToken).ConfigureAwait(true);
            assetJson = PrefabPackageManifestBuilder.PatchAssetJsonFileRow(assetJson, assetUpload);

            var localManifest = JsonConvert.DeserializeObject<AssetVersionManifest>(
                JsonConvert.SerializeObject(assetJson, JsonSettings));
            var materialsManifest = package.Materials;
            var validator = new ManifestValidator();
            var report = validator.ValidateManifest(localManifest);
            var matReport = validator.ValidateMaterials(materialsManifest);
            foreach (var warning in report.Warnings.Concat(matReport.Warnings))
            {
                BridgeLog.Warn("Publish validation warning: " + warning);
            }

            if (!report.IsValid || !matReport.IsValid)
            {
                var errors = string.Join("; ", report.Errors.Concat(matReport.Errors));
                throw new InvalidOperationException("Local package validation failed: " + errors);
            }

            progress?.Report("Submitting for validation…");
            await _api.SubmitVersionAsync(
                assetId,
                versionId,
                assetJson,
                materialsJson,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            progress?.Report("Publishing version…");
            var published = await _api.PublishVersionAsync(
                assetId,
                versionId,
                request.Recommend,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            BridgeLog.Info(
                $"Published prefab as asset={assetId} version={published.asset_version_id} v{published.version_number}");

            return new PrefabPublishResult
            {
                AssetId = assetId,
                AssetVersionId = published.asset_version_id,
                VersionNumber = published.version_number,
                DisplayName = displayName,
                Asset = asset,
                Version = published
            };
        }
    }
}
