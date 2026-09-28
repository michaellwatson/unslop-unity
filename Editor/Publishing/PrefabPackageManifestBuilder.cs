using System;
using System.Collections.Generic;
using System.Linq;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Manifests;

namespace Unslop.UnityBridge.Editor.Publishing
{
    /// <summary>
    /// Assembles neutral asset.json / materials.json payloads and file metadata
    /// for the Bridge publish two-pass flow (seed-command compatible).
    /// </summary>
    public static class PrefabPackageManifestBuilder
    {
        public const string PipelineOrigin = "unity_prefab_export";
        public const string MinimumBridgeVersion = "1.0.0";

        public static Dictionary<string, object> BuildAssetJson(
            string assetId,
            string assetVersionId,
            string displayName,
            UploadedFileDto model,
            IReadOnlyList<UploadedFileDto> files)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var fileRows = (files ?? Array.Empty<UploadedFileDto>())
                .Select(ToFileRow)
                .ToList();

            return new Dictionary<string, object>
            {
                ["schema_version"] = 1,
                ["asset_id"] = assetId,
                ["asset_version_id"] = assetVersionId,
                ["display_name"] = displayName ?? "Unslop Asset",
                ["content_kind"] = "static_mesh",
                ["minimum_bridge_version"] = MinimumBridgeVersion,
                ["model"] = new Dictionary<string, object>
                {
                    ["file_id"] = model.file_id,
                    ["relative_path"] = model.relative_path ?? "model.fbx",
                    ["format"] = "fbx",
                    ["source_up_axis"] = "Y",
                    ["source_forward_axis"] = "Z",
                    ["source_units"] = "metres"
                },
                ["compatibility"] = new Dictionary<string, object>
                {
                    ["classification"] = "compatible",
                    ["declared_changes"] = new List<string> { "geometry" },
                    ["hierarchy_compatible"] = true,
                    ["material_slots_compatible"] = true,
                    ["pipeline_origin"] = PipelineOrigin
                },
                ["files"] = fileRows
            };
        }

        public static Dictionary<string, object> PatchAssetJsonFileRow(
            Dictionary<string, object> assetJson,
            UploadedFileDto assetManifestFile)
        {
            if (assetJson == null)
            {
                throw new ArgumentNullException(nameof(assetJson));
            }

            if (assetManifestFile == null)
            {
                throw new ArgumentNullException(nameof(assetManifestFile));
            }

            if (!assetJson.TryGetValue("files", out var filesObj) || filesObj is not List<Dictionary<string, object>> files)
            {
                files = new List<Dictionary<string, object>>();
                assetJson["files"] = files;
            }

            var row = ToFileRow(assetManifestFile);
            var replaced = false;
            for (var i = 0; i < files.Count; i++)
            {
                if (string.Equals(
                        files[i].TryGetValue("relative_path", out var path) ? path as string : null,
                        "asset.json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    files[i] = row;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                files.Add(row);
            }

            return assetJson;
        }

        public static Dictionary<string, object> ToMaterialsDictionary(MaterialsManifest materials)
        {
            materials ??= new MaterialsManifest();
            return new Dictionary<string, object>
            {
                ["schema_version"] = materials.schema_version <= 0 ? 1 : materials.schema_version,
                ["materials"] = (materials.materials ?? new List<MaterialDefinition>())
                    .Select(m => new Dictionary<string, object>
                    {
                        ["material_id"] = m.material_id,
                        ["display_name"] = string.IsNullOrWhiteSpace(m.display_name) ? m.material_id : m.display_name,
                        ["model"] = string.IsNullOrWhiteSpace(m.model) ? "metallic_roughness" : m.model,
                        ["textures"] = m.textures ?? new Dictionary<string, string>()
                    })
                    .ToList(),
                ["slots"] = (materials.slots ?? new List<MaterialSlot>())
                    .Select(s => new Dictionary<string, object>
                    {
                        ["slot_id"] = s.slot_id,
                        ["display_name"] = string.IsNullOrWhiteSpace(s.display_name) ? s.slot_id : s.display_name,
                        ["material_id"] = s.material_id
                    })
                    .ToList()
            };
        }

        static Dictionary<string, object> ToFileRow(UploadedFileDto file)
        {
            return new Dictionary<string, object>
            {
                ["file_id"] = file.file_id,
                ["role"] = file.role,
                ["relative_path"] = file.relative_path,
                ["byte_length"] = file.byte_length,
                ["sha256"] = file.sha256
            };
        }
    }
}
