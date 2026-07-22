using System;
using System.Collections.Generic;

namespace Unslop.UnityBridge.Editor.Manifests
{
    [Serializable]
    public sealed class AssetVersionManifest
    {
        public int schema_version = 1;
        public string asset_id = string.Empty;
        public string asset_version_id = string.Empty;
        public int version_number;
        public string display_name = string.Empty;
        public string content_kind = string.Empty;
        public string minimum_bridge_version = string.Empty;
        public ModelManifest model = new ModelManifest();
        public CompatibilityDeclaration compatibility = new CompatibilityDeclaration();
        public List<ManifestFile> files = new List<ManifestFile>();
    }

    [Serializable]
    public sealed class ModelManifest
    {
        public string file_id = string.Empty;
        public string relative_path = string.Empty;
        public string format = string.Empty;
        public string source_up_axis = "Y";
        public string source_forward_axis = "Z";
        public string source_units = "metres";
        public string expected_root_name;
    }

    [Serializable]
    public sealed class CompatibilityDeclaration
    {
        public string classification = "compatible";
        public List<string> declared_changes = new List<string>();
        public bool hierarchy_compatible = true;
        public bool material_slots_compatible = true;
        public string pipeline_origin;
    }

    [Serializable]
    public sealed class ManifestFile
    {
        public string file_id = string.Empty;
        public string role = string.Empty;
        public string relative_path = string.Empty;
        public string media_type = string.Empty;
        public long byte_length;
        public string sha256 = string.Empty;
    }

    [Serializable]
    public sealed class MaterialsManifest
    {
        public int schema_version = 1;
        public List<MaterialDefinition> materials = new List<MaterialDefinition>();
        public List<MaterialSlot> slots = new List<MaterialSlot>();
    }

    [Serializable]
    public sealed class MaterialDefinition
    {
        public string material_id = string.Empty;
        public string display_name = string.Empty;
        public string model = "metallic_roughness";
        public Dictionary<string, string> textures = new Dictionary<string, string>();
    }

    [Serializable]
    public sealed class MaterialSlot
    {
        public string slot_id = string.Empty;
        public string display_name = string.Empty;
        public string material_id = string.Empty;
    }

    public sealed class ValidationReport
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public void Error(string message) => Errors.Add(message);
        public void Warn(string message) => Warnings.Add(message);
    }
}
