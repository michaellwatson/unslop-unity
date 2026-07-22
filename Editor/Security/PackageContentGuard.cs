using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Manifests;

namespace Unslop.UnityBridge.Editor.Security
{
    public static class PackageContentGuard
    {
        static readonly HashSet<string> AllowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "model", "texture", "preview", "material_manifest", "asset_manifest"
        };

        static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".json", ".txt"
        };

        static readonly string[] ForbiddenExtensions =
        {
            ".cs", ".dll", ".so", ".dylib", ".exe", ".bat", ".cmd", ".ps1", ".js", ".shader", ".compute", ".cginc", ".hlsl", ".asmdef"
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
                && !string.Equals(Normalize(version.manifest_sha256), Normalize(grant.manifest_sha256), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Download grant manifest hash does not match version metadata.");
            }

            var manifestFiles = (version.files ?? version.manifest?.files) ?? new List<ManifestFileDto>();
            foreach (var file in grant.files ?? Enumerable.Empty<DownloadGrantFileDto>())
            {
                ValidateRelativePath(file.relative_path);
                var match = manifestFiles.FirstOrDefault(f => f.file_id == file.file_id);
                if (match == null)
                {
                    throw new InvalidOperationException($"Grant file {file.file_id} not present in version manifest.");
                }

                if (!string.Equals(Normalize(match.sha256), Normalize(file.sha256), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Grant hash mismatch for {file.relative_path}.");
                }

                if (match.byte_length != file.byte_length)
                {
                    throw new InvalidOperationException($"Grant size mismatch for {file.relative_path}.");
                }

                if (!AllowedRoles.Contains(match.role ?? string.Empty))
                {
                    throw new InvalidOperationException($"Forbidden role in grant: {match.role}");
                }
            }
        }

        public static void ValidateRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || relativePath.Contains("..")
                || Path.IsPathRooted(relativePath)
                || relativePath.Contains(":")
                || relativePath.StartsWith("/")
                || relativePath.StartsWith("\\"))
            {
                throw new InvalidOperationException($"Rejected path: {relativePath}");
            }

            var ext = Path.GetExtension(relativePath);
            if (ForbiddenExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Forbidden content rejected: {relativePath}");
            }

            if (!AllowedExtensions.Contains(ext))
            {
                throw new InvalidOperationException($"Unsupported extension rejected: {relativePath}");
            }
        }

        static string Normalize(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return string.Empty;
            }

            return hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? hash.Substring("sha256:".Length).ToLowerInvariant()
                : hash.ToLowerInvariant();
        }
    }
}
