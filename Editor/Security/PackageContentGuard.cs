using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.Manifests;

namespace Unslop.UnityBridge.Editor.Security
{
    /// <summary>
    /// Path/type/size/hash checks used by download and staging.
    /// </summary>
    public static class PackageContentGuard
    {
        public const long MaxFileBytes = 512L * 1024L * 1024L;

        static readonly HashSet<string> AllowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "model", "texture", "preview", "material_manifest", "asset_manifest"
        };

        static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr", ".json", ".txt"
        };

        public static readonly string[] ForbiddenExtensions =
        {
            ".cs", ".dll", ".so", ".dylib", ".exe", ".bat", ".cmd", ".ps1", ".js", ".shader",
            ".compute", ".cginc", ".hlsl", ".asmdef"
        };

        public static void ValidateGrantAgainstManifest(AssetVersionDetailDto version, DownloadGrantDto grant)
        {
            if (version == null)
            {
                throw new InvalidOperationException("Version detail required.");
            }

            if (grant == null)
            {
                throw new InvalidOperationException("Download grant required.");
            }

            if (!string.IsNullOrEmpty(version.manifest_sha256)
                && !string.IsNullOrEmpty(grant.manifest_sha256)
                && !HashUtil.EqualsHash(version.manifest_sha256, grant.manifest_sha256))
            {
                throw new InvalidOperationException("Download grant manifest hash does not match version metadata.");
            }

            var manifestFiles = (version.files ?? version.manifest?.files) ?? new List<ManifestFileDto>();
            foreach (var file in grant.files ?? Enumerable.Empty<DownloadGrantFileDto>())
            {
                ValidateRelativePath(file.relative_path);
                EnsureSize(file.byte_length, file.relative_path);
                var match = manifestFiles.FirstOrDefault(f =>
                    f.file_id == file.file_id
                    || string.Equals(NormalizePath(f.relative_path), NormalizePath(file.relative_path), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    throw new InvalidOperationException($"Grant file {file.file_id} not present in version manifest.");
                }

                if (!HashUtil.EqualsHash(match.sha256, file.sha256))
                {
                    throw new InvalidOperationException($"Grant hash mismatch for {file.relative_path}.");
                }

                if (match.byte_length > 0 && match.byte_length != file.byte_length)
                {
                    throw new InvalidOperationException($"Grant size mismatch for {file.relative_path}.");
                }

                EnsureAllowedRole(match.role);
            }
        }

        public static void ValidateGrantAgainstManifest(DownloadGrantDto grant, AssetVersionManifest manifest)
        {
            if (grant == null)
            {
                throw new InvalidOperationException("Download grant required.");
            }

            if (manifest?.files == null)
            {
                throw new InvalidOperationException("Manifest files are required for grant validation.");
            }

            var byPath = manifest.files.ToDictionary(f => NormalizePath(f.relative_path), f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var file in grant.files ?? Enumerable.Empty<DownloadGrantFileDto>())
            {
                ValidateRelativePath(file.relative_path);
                EnsureSize(file.byte_length, file.relative_path);
                if (!byPath.TryGetValue(NormalizePath(file.relative_path), out var declared))
                {
                    throw new InvalidOperationException($"Grant file path not in manifest: {file.relative_path}");
                }

                EnsureAllowedRole(declared.role);
                if (!HashUtil.EqualsHash(declared.sha256, file.sha256))
                {
                    throw new InvalidOperationException($"Grant/manifest hash mismatch for {file.relative_path}");
                }

                if (declared.byte_length > 0 && declared.byte_length != file.byte_length)
                {
                    throw new InvalidOperationException($"Grant/manifest size mismatch for {file.relative_path}");
                }
            }
        }

        public static void ValidateDownloadedPackage(string downloadRoot, AssetVersionManifest manifest)
        {
            if (!Directory.Exists(downloadRoot))
            {
                throw new InvalidOperationException("Download root missing.");
            }

            foreach (var file in manifest.files ?? Enumerable.Empty<ManifestFile>())
            {
                ValidateRelativePath(file.relative_path);
                EnsureAllowedRole(file.role);
                EnsureSize(file.byte_length, file.relative_path);
                var full = Path.Combine(downloadRoot, file.relative_path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    throw new InvalidOperationException($"Missing downloaded file: {file.relative_path}");
                }

                var info = new FileInfo(full);
                if (file.byte_length > 0 && info.Length != file.byte_length)
                {
                    throw new InvalidOperationException(
                        $"Size mismatch for {file.relative_path}: expected {file.byte_length}, got {info.Length}");
                }

                EnsureHashMatches(file.sha256, full, file.relative_path);
            }
        }

        public static void ValidateRelativePath(string relativePath) => EnsureSafeRelativePath(relativePath);

        public static void EnsureSafeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || relativePath.Contains("..")
                || Path.IsPathRooted(relativePath)
                || relativePath.Contains(":")
                || relativePath.Contains('\0')
                || relativePath.StartsWith("/")
                || relativePath.StartsWith("\\"))
            {
                throw new InvalidOperationException($"Path traversal rejected: {relativePath}");
            }

            var ext = Path.GetExtension(relativePath);
            if (ForbiddenExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Forbidden content type rejected: {relativePath}");
            }

            if (!string.IsNullOrEmpty(ext) && !AllowedExtensions.Contains(ext))
            {
                throw new InvalidOperationException($"Unsupported content type rejected: {relativePath}");
            }
        }

        public static void EnsureAllowedRole(string role)
        {
            if (!AllowedRoles.Contains(role ?? string.Empty))
            {
                throw new InvalidOperationException($"Unsupported file role '{role}'.");
            }
        }

        public static void EnsureSize(long byteLength, string relativePath)
        {
            if (byteLength <= 0)
            {
                throw new InvalidOperationException($"Non-positive size for {relativePath}.");
            }

            if (byteLength > MaxFileBytes)
            {
                throw new InvalidOperationException($"File exceeds max size ({MaxFileBytes} bytes): {relativePath}");
            }
        }

        public static void EnsureHashMatches(string expected, string actualPath, string relativePath)
        {
            var actual = HashUtil.Sha256File(actualPath);
            if (!HashUtil.EqualsHash(expected, actual))
            {
                throw new InvalidOperationException(
                    $"SHA256 mismatch for {relativePath}: expected {HashUtil.Normalize(expected)}, got {HashUtil.Normalize(actual)}");
            }
        }

        public static string NormalizePath(string path) =>
            (path ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
    }
}
