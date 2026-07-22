using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Unslop.UnityBridge.Editor.Manifests
{
    public interface IAssetPackageValidator
    {
        ValidationReport ValidateManifest(AssetVersionManifest manifest);
        ValidationReport ValidateMaterials(MaterialsManifest materials);
    }

    public sealed class ManifestValidator : IAssetPackageValidator
    {
        public static readonly HashSet<string> AllowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "model", "texture", "preview", "material_manifest", "asset_manifest"
        };

        static readonly Regex Sha256Hex = new Regex("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

        static readonly string[] ForbiddenExtensions =
        {
            ".cs", ".dll", ".so", ".dylib", ".exe", ".bat", ".cmd", ".ps1", ".js", ".shader", ".compute", ".cginc", ".hlsl", ".asmdef"
        };

        public ValidationReport ValidateManifest(AssetVersionManifest manifest)
        {
            var report = new ValidationReport();
            if (manifest == null)
            {
                report.Error("Manifest is null.");
                return report;
            }

            if (manifest.schema_version < 1)
            {
                report.Error("schema_version must be >= 1.");
            }

            Require(report, manifest.asset_id, "asset_id");
            Require(report, manifest.asset_version_id, "asset_version_id");
            Require(report, manifest.display_name, "display_name");
            Require(report, manifest.content_kind, "content_kind");
            Require(report, manifest.minimum_bridge_version, "minimum_bridge_version");

            if (manifest.model == null)
            {
                report.Error("model block is required.");
            }
            else
            {
                Require(report, manifest.model.file_id, "model.file_id");
                Require(report, manifest.model.relative_path, "model.relative_path");
                if (!string.Equals(manifest.model.format, "fbx", StringComparison.OrdinalIgnoreCase)
                    && !EndsWith(manifest.model.relative_path, ".fbx"))
                {
                    report.Error("MVP requires an FBX model.");
                }
            }

            if (manifest.files == null || manifest.files.Count == 0)
            {
                report.Error("files[] must not be empty.");
                return report;
            }

            var modelFiles = manifest.files.Where(f => string.Equals(f.role, "model", StringComparison.OrdinalIgnoreCase)).ToList();
            if (modelFiles.Count != 1)
            {
                report.Error("Exactly one file with role=model is required for the static-mesh MVP.");
            }

            foreach (var file in manifest.files)
            {
                if (file == null)
                {
                    report.Error("Null file entry.");
                    continue;
                }

                Require(report, file.file_id, "file.file_id");
                Require(report, file.role, "file.role");
                Require(report, file.relative_path, "file.relative_path");
                Require(report, file.sha256, "file.sha256");

                if (!AllowedRoles.Contains(file.role ?? string.Empty))
                {
                    report.Error($"Unsupported file role '{file.role}'.");
                }

                if (file.byte_length <= 0)
                {
                    report.Error($"file {file.file_id} must declare positive byte_length.");
                }

                if (ContainsTraversal(file.relative_path))
                {
                    report.Error($"Path traversal rejected: {file.relative_path}");
                }

                var ext = Path.GetExtension(file.relative_path ?? string.Empty).ToLowerInvariant();
                if (ForbiddenExtensions.Contains(ext))
                {
                    report.Error($"Forbidden content type rejected: {file.relative_path}");
                }
            }

            return report;
        }

        public ValidationReport ValidateMaterials(MaterialsManifest materials)
        {
            var report = new ValidationReport();
            if (materials == null)
            {
                report.Error("materials manifest is null.");
                return report;
            }

            if (materials.materials == null || materials.materials.Count == 0)
            {
                report.Warn("No materials defined.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mat in materials.materials ?? Enumerable.Empty<MaterialDefinition>())
            {
                if (string.IsNullOrWhiteSpace(mat.material_id) || !ids.Add(mat.material_id))
                {
                    report.Error($"Duplicate or empty material_id: {mat?.material_id}");
                }
            }

            foreach (var slot in materials.slots ?? Enumerable.Empty<MaterialSlot>())
            {
                if (string.IsNullOrWhiteSpace(slot.slot_id))
                {
                    report.Error("slot_id is required.");
                }

                if (!ids.Contains(slot.material_id))
                {
                    report.Error($"slot {slot.slot_id} references unknown material_id {slot.material_id}");
                }
            }

            return report;
        }

        public static string ComputeSha256Hex(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string ComputeSha256Hex(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string ComputeSha256Hex(string text, Encoding encoding = null)
        {
            encoding ??= Encoding.UTF8;
            return ComputeSha256Hex(encoding.GetBytes(text ?? string.Empty));
        }

        public static string FormatSha256Uri(string hexOrUri)
        {
            if (string.IsNullOrWhiteSpace(hexOrUri))
            {
                throw new ArgumentException("Hash value is required.", nameof(hexOrUri));
            }

            var value = hexOrUri.Trim();
            if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring("sha256:".Length);
            }

            if (!Sha256Hex.IsMatch(value))
            {
                throw new ArgumentException("Value is not a valid SHA-256 hex digest.", nameof(hexOrUri));
            }

            return "sha256:" + value.ToLowerInvariant();
        }

        public static bool IsValidSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var v = value.Trim();
            if (v.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                v = v.Substring("sha256:".Length);
            }

            return Sha256Hex.IsMatch(v);
        }

        static void Require(ValidationReport report, string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                report.Error($"{field} is required.");
            }
        }

        static bool EndsWith(string path, string suffix)
            => !string.IsNullOrEmpty(path) && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        static bool ContainsTraversal(string path)
            => string.IsNullOrEmpty(path)
               || path.Contains("..")
               || Path.IsPathRooted(path)
               || path.Contains(":")
               || path.StartsWith("/")
               || path.StartsWith("\\");
    }
}
