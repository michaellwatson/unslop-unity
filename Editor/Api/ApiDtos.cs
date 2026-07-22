using System;
using System.Collections.Generic;

namespace Unslop.UnityBridge.Editor.Api
{
    [Serializable]
    public sealed class CursorPage<T>
    {
        public List<T> data = new List<T>();
        public string next_cursor;
    }

    [Serializable]
    public sealed class ProjectDto
    {
        public string project_id;
        public string name;
        public string role;
        public AllowedOperationsDto allowed_operations;
        public CompatibilityPolicyDto compatibility_policy;
        public string created_at;
        public string updated_at;
    }

    [Serializable]
    public sealed class AllowedOperationsDto
    {
        public bool read;
        public bool install_report;
        public bool download;
        public bool physical_spec_write;
        public bool scale_confirm;
    }

    [Serializable]
    public sealed class CompatibilityPolicyDto
    {
        public List<string> engines = new List<string>();
    }

    [Serializable]
    public sealed class AssetSummaryDto
    {
        public string asset_id;
        public string display_name;
        public string lifecycle;
        public bool api_available;
        public string recommended_version_id;
        public string current_physical_spec_id;
        public string physical_spec_etag;
        public string kind;
        public string project_id;
    }

    [Serializable]
    public class AssetVersionSummaryDto
    {
        public string asset_version_id;
        public string asset_id;
        public int version_number;
        public string state;
        public string manifest_sha256;
        public string compatibility_class;
        public string minimum_bridge_version;
        public string pipeline_origin;
        public string published_at;
    }

    [Serializable]
    public sealed class AssetVersionDetailDto : AssetVersionSummaryDto
    {
        public AssetVersionManifestDto manifest;
        public MaterialsManifestDto materials;
        public CompatibilityDeclarationDto compatibility;
        public List<ManifestFileDto> files = new List<ManifestFileDto>();
    }

    [Serializable]
    public sealed class AssetVersionManifestDto
    {
        public int schema_version = 1;
        public string asset_id;
        public string asset_version_id;
        public string display_name;
        public string content_kind;
        public string minimum_bridge_version;
        public string physical_spec_id_at_publish;
        public ModelManifestDto model;
        public CompatibilityDeclarationDto compatibility;
        public List<ManifestFileDto> files = new List<ManifestFileDto>();
    }

    [Serializable]
    public sealed class ModelManifestDto
    {
        public string file_id;
        public string relative_path;
        public string format;
        public string source_up_axis;
        public string source_forward_axis;
        public string source_units;
        public string expected_root_name;
    }

    [Serializable]
    public sealed class CompatibilityDeclarationDto
    {
        public string classification;
        public List<string> declared_changes = new List<string>();
        public bool hierarchy_compatible = true;
        public bool material_slots_compatible = true;
        public string pipeline_origin;
        public string source_content_sha256;
    }

    [Serializable]
    public sealed class ManifestFileDto
    {
        public string file_id;
        public string role;
        public string relative_path;
        public string media_type;
        public long byte_length;
        public string sha256;
    }

    [Serializable]
    public sealed class MaterialsManifestDto
    {
        public int schema_version = 1;
        public List<MaterialDefinitionDto> materials = new List<MaterialDefinitionDto>();
        public List<MaterialSlotDto> slots = new List<MaterialSlotDto>();
    }

    [Serializable]
    public sealed class MaterialDefinitionDto
    {
        public string material_id;
        public string display_name;
        public string model;
        public Dictionary<string, string> textures = new Dictionary<string, string>();
    }

    [Serializable]
    public sealed class MaterialSlotDto
    {
        public string slot_id;
        public string display_name;
        public string material_id;
    }

    [Serializable]
    public sealed class ClientContextDto
    {
        public string engine = "unity";
        public string unity_version;
        public string bridge_version;
        public string render_pipeline = "urp";
        public List<int> manifest_schema_versions = new List<int> { 1 };
    }

    [Serializable]
    public sealed class AssetUpdateCheckRequestDto
    {
        public ClientContextDto client = new ClientContextDto();
        public List<AssetUpdateCheckItemDto> assets = new List<AssetUpdateCheckItemDto>();
    }

    [Serializable]
    public sealed class AssetUpdateCheckItemDto
    {
        public string asset_id;
        public string installed_version_id;
        public string physical_spec_id;
        public string import_profile_hash;
        public string state_hash;
        public bool pinned;
    }

    [Serializable]
    public sealed class AssetUpdateCheckResponseDto
    {
        public string checked_at;
        public List<AssetUpdateStatusDto> assets = new List<AssetUpdateStatusDto>();
    }

    [Serializable]
    public sealed class AssetUpdateStatusDto
    {
        public string asset_id;
        public string installed_version_id;
        public string recommended_version_id;
        public string update_status;
        public string recommended_version_state;
        public string current_physical_spec_id;
        public string confirmation_status;
        public string minimum_bridge_version;
    }

    [Serializable]
    public sealed class DownloadGrantRequestDto
    {
        public List<string> roles = new List<string>();
    }

    [Serializable]
    public sealed class DownloadGrantDto
    {
        public string asset_version_id;
        public string manifest_sha256;
        public string expires_at;
        public List<DownloadGrantFileDto> files = new List<DownloadGrantFileDto>();
    }

    [Serializable]
    public sealed class DownloadGrantFileDto
    {
        public string file_id;
        public string relative_path;
        public long byte_length;
        public string sha256;
        public string download_url;
        public bool supports_range;
    }

    [Serializable]
    public sealed class InstallReportDto
    {
        public string transaction_id;
        public string operation;
        public string previous_version_id;
        public string installed_version_id;
        public string physical_spec_id;
        public string manifest_sha256;
        public string import_profile_hash;
        public string lock_entry_hash;
        public MaterialSummaryDto material_summary = new MaterialSummaryDto();
        public ClientContextDto client = new ClientContextDto();
        public string committed_at;
    }

    [Serializable]
    public sealed class MaterialSummaryDto
    {
        public int managed;
        public int local_override;
        public int locally_modified_managed;
    }

    [Serializable]
    public sealed class PinRequestDto
    {
        public string version_id;
        public string reason;
        public string mode = "manual";
    }

    [Serializable]
    public sealed class CandidateDecisionDto
    {
        public string candidate_version_id;
        public string decision;
        public string reason;
    }

    [Serializable]
    public sealed class PhysicalSpecRevisionDto
    {
        public string physical_spec_id;
        public float[] dimensions_metres;
        public string up_axis;
        public string forward_axis;
        public string pivot_policy;
        public string etag;
        public bool artist_correction_pending;
    }

    [Serializable]
    public sealed class PhysicalSpecCreateDto
    {
        public float[] dimensions_metres;
        public string up_axis = "Y";
        public string forward_axis = "Z";
        public string pivot_policy = "bottom_centre";
        public PhysicalSpecOriginDto origin = new PhysicalSpecOriginDto();
        public PhysicalSpecMeasurementDto measurement = new PhysicalSpecMeasurementDto();
        public string note;
    }

    [Serializable]
    public sealed class PhysicalSpecOriginDto
    {
        public string type = "unity_canonical_correction";
        public string project_id;
        public string asset_version_id;
        public string unity_version;
        public string bridge_version;
    }

    [Serializable]
    public sealed class PhysicalSpecMeasurementDto
    {
        public float[] measured_dimensions_metres;
        public float[] source_dimensions_metres;
        public float[] proposed_visual_correction;
    }

    [Serializable]
    public sealed class ScaleConfirmationCreateDto
    {
        public string asset_version_id;
        public string physical_spec_id;
        public string engine = "unity";
        public float[] measured_dimensions_metres;
        public float[] tolerance_metres;
        public string unity_version;
        public string bridge_version;
        public string render_pipeline = "urp";
        public string import_profile_hash;
        public string project_id;
    }

    [Serializable]
    public sealed class ScaleConfirmationDto
    {
        public string confirmation_id;
        public string status;
        public ScaleBadgeDto badge;
    }

    [Serializable]
    public sealed class ScaleBadgeDto
    {
        public string engine;
        public string label;
        public string asset_version_id;
        public string physical_spec_id;
    }

    [Serializable]
    public sealed class ApiErrorDto
    {
        public string error;
        public string message;
        public string code;
    }
}
