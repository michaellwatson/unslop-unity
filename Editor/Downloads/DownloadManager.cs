using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using Unslop.UnityBridge.Editor.Security;
using UnityEngine.Networking;

namespace Unslop.UnityBridge.Editor.Downloads
{
    public sealed class DownloadResult
    {
        public string VersionId { get; set; }
        public string DownloadRoot { get; set; }
        public string ManifestSha256 { get; set; }
        public AssetVersionManifest Manifest { get; set; }
        public MaterialsManifest Materials { get; set; }
        public IReadOnlyList<string> Files { get; set; }
    }

    public sealed class DownloadManager
    {
        readonly IUnslopApiClient _api;

        public DownloadManager(IUnslopApiClient api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public async Task<DownloadResult> DownloadVersionAsync(
            string assetId,
            string versionId,
            AssetVersionDetailDto detail = null,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_enabled"))
            {
                throw new InvalidOperationException("unity_bridge_enabled feature flag is off.");
            }

            if (string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException("versionId is required.", nameof(versionId));
            }

            detail ??= await _api.GetAssetVersionAsync(assetId, versionId, cancellationToken);
            var manifest = ToManifest(detail);
            var materials = ToMaterials(detail);

            var validator = new ManifestValidator();
            var report = validator.ValidateManifest(manifest);
            if (!report.IsValid)
            {
                throw new InvalidOperationException("Manifest validation failed: " + string.Join("; ", report.Errors));
            }

            var grant = await _api.CreateDownloadGrantAsync(
                versionId,
                new DownloadGrantRequestDto(),
                Guid.NewGuid().ToString("N"),
                cancellationToken);

            PackageContentGuard.ValidateGrantAgainstManifest(grant, manifest);
            if (!string.IsNullOrEmpty(grant.manifest_sha256)
                && !string.IsNullOrEmpty(detail.manifest_sha256)
                && !HashUtil.EqualsSha256(grant.manifest_sha256, detail.manifest_sha256))
            {
                throw new PackageGuardViolationException("Download grant manifest_sha256 does not match version metadata.");
            }

            ManagedPaths.EnsureOperationalDirectories();
            var root = ManagedPaths.DownloadVersionDir(versionId);
            Directory.CreateDirectory(root);

            var files = grant.files ?? new List<DownloadGrantFileDto>();
            var completed = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var declared = manifest.files.FirstOrDefault(f =>
                    string.Equals(
                        PackageContentGuard.NormalizePath(f.relative_path),
                        PackageContentGuard.NormalizePath(file.relative_path),
                        StringComparison.OrdinalIgnoreCase));
                if (declared == null)
                {
                    throw new PackageGuardViolationException($"Grant path missing from manifest: {file.relative_path}");
                }

                PackageContentGuard.EnsureAllowedRole(declared.role);
                PackageContentGuard.EnsureSafeRelativePath(file.relative_path);

                var dest = Path.Combine(root, file.relative_path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? root);
                await DownloadFileAsync(file, dest, cancellationToken);
                PackageContentGuard.EnsureHashMatches(file.sha256, dest, file.relative_path);
                completed++;
                progress?.Report(completed / (float)Math.Max(1, files.Count));
            }

            PackageContentGuard.ValidateDownloadedPackage(root, manifest);
            BridgeLog.Info($"Downloaded {files.Count} file(s) for version {versionId} into Library/Unslop/Downloads.");

            return new DownloadResult
            {
                VersionId = versionId,
                DownloadRoot = root,
                ManifestSha256 = HashUtil.PrefixSha256(grant.manifest_sha256 ?? detail.manifest_sha256),
                Manifest = manifest,
                Materials = materials,
                Files = files.Select(f => f.relative_path).ToList()
            };
        }

        async Task DownloadFileAsync(DownloadGrantFileDto file, string destPath, CancellationToken cancellationToken)
        {
            long existing = 0;
            if (File.Exists(destPath))
            {
                existing = new FileInfo(destPath).Length;
                if (existing == file.byte_length)
                {
                    if (HashUtil.EqualsSha256(file.sha256, HashUtil.Sha256PrefixedFile(destPath)))
                    {
                        BridgeLog.Info($"Skip complete file {file.relative_path}");
                        return;
                    }

                    File.Delete(destPath);
                    existing = 0;
                }
                else if (existing > file.byte_length || !file.supports_range)
                {
                    File.Delete(destPath);
                    existing = 0;
                }
            }

            var url = file.download_url;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException($"Missing download_url for {file.relative_path}");
            }

            using var request = UnityWebRequest.Get(url);
            if (existing > 0 && file.supports_range)
            {
                request.SetRequestHeader("Range", $"bytes={existing}-");
                BridgeLog.Info($"Resuming {file.relative_path} from byte {existing}");
            }

            var tempPath = destPath + ".partial";
            if (existing > 0 && File.Exists(destPath))
            {
                File.Copy(destPath, tempPath, true);
            }
            else if (File.Exists(tempPath) && existing == 0)
            {
                File.Delete(tempPath);
            }

            request.downloadHandler = new DownloadHandlerFile(tempPath, existing > 0)
            {
                removeFileOnAbort = true
            };

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success
                && !(existing > 0 && request.responseCode == 206))
            {
                throw new InvalidOperationException(
                    $"Download failed for {file.relative_path}: HTTP {request.responseCode} {BridgeLog.Redact(request.error)}");
            }

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(tempPath, destPath);

            var length = new FileInfo(destPath).Length;
            if (length != file.byte_length)
            {
                throw new PackageGuardViolationException(
                    $"Downloaded size mismatch for {file.relative_path}: expected {file.byte_length}, got {length}");
            }
        }

        public static AssetVersionManifest ToManifest(AssetVersionDetailDto detail)
        {
            if (detail == null)
            {
                throw new ArgumentNullException(nameof(detail));
            }

            if (detail.manifest != null)
            {
                return new AssetVersionManifest
                {
                    schema_version = detail.manifest.schema_version,
                    asset_id = detail.manifest.asset_id ?? detail.asset_id,
                    asset_version_id = detail.manifest.asset_version_id ?? detail.asset_version_id,
                    version_number = detail.version_number,
                    display_name = detail.manifest.display_name,
                    content_kind = detail.manifest.content_kind,
                    minimum_bridge_version = detail.manifest.minimum_bridge_version ?? detail.minimum_bridge_version,
                    model = detail.manifest.model == null
                        ? new ModelManifest()
                        : new ModelManifest
                        {
                            file_id = detail.manifest.model.file_id,
                            relative_path = detail.manifest.model.relative_path,
                            format = detail.manifest.model.format,
                            source_up_axis = detail.manifest.model.source_up_axis,
                            source_forward_axis = detail.manifest.model.source_forward_axis,
                            source_units = detail.manifest.model.source_units,
                            expected_root_name = detail.manifest.model.expected_root_name
                        },
                    compatibility = MapCompatibility(detail.manifest.compatibility ?? detail.compatibility),
                    files = (detail.manifest.files ?? detail.files ?? new List<ManifestFileDto>())
                        .Select(MapFile)
                        .ToList()
                };
            }

            return new AssetVersionManifest
            {
                schema_version = 1,
                asset_id = detail.asset_id,
                asset_version_id = detail.asset_version_id,
                version_number = detail.version_number,
                display_name = detail.asset_version_id,
                content_kind = "static_mesh",
                minimum_bridge_version = detail.minimum_bridge_version,
                compatibility = MapCompatibility(detail.compatibility),
                files = (detail.files ?? new List<ManifestFileDto>()).Select(MapFile).ToList()
            };
        }

        public static MaterialsManifest ToMaterials(AssetVersionDetailDto detail)
        {
            if (detail?.materials == null)
            {
                return new MaterialsManifest();
            }

            return new MaterialsManifest
            {
                schema_version = detail.materials.schema_version,
                materials = (detail.materials.materials ?? new List<MaterialDefinitionDto>())
                    .Select(m => new MaterialDefinition
                    {
                        material_id = m.material_id,
                        display_name = m.display_name,
                        model = m.model,
                        textures = m.textures ?? new Dictionary<string, string>()
                    })
                    .ToList(),
                slots = (detail.materials.slots ?? new List<MaterialSlotDto>())
                    .Select(s => new MaterialSlot
                    {
                        slot_id = s.slot_id,
                        display_name = s.display_name,
                        material_id = s.material_id
                    })
                    .ToList()
            };
        }

        static CompatibilityDeclaration MapCompatibility(CompatibilityDeclarationDto dto)
        {
            if (dto == null)
            {
                return new CompatibilityDeclaration();
            }

            return new CompatibilityDeclaration
            {
                classification = dto.classification ?? "compatible",
                declared_changes = dto.declared_changes ?? new List<string>(),
                hierarchy_compatible = dto.hierarchy_compatible,
                material_slots_compatible = dto.material_slots_compatible
            };
        }

        static ManifestFile MapFile(ManifestFileDto f) => new ManifestFile
        {
            file_id = f.file_id,
            role = f.role,
            relative_path = f.relative_path,
            media_type = f.media_type,
            byte_length = f.byte_length,
            sha256 = f.sha256
        };
    }
}
