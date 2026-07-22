using System;
using System.Collections.Generic;

namespace Unslop.UnityBridge.Editor.Locking
{
    [Serializable]
    public sealed class UnslopLockFile
    {
        public int schema_version = 1;
        public string project_id = string.Empty;
        public string environment = "production";
        public string generated_by = string.Empty;
        public Dictionary<string, LockAssetEntry> assets = new Dictionary<string, LockAssetEntry>();
    }

    [Serializable]
    public sealed class LockAssetEntry
    {
        public string installed_version_id = string.Empty;
        public int installed_version_number;
        public string physical_spec_id = string.Empty;
        public string wrapper_prefab_guid = string.Empty;
        public string visual_prefab_guid = string.Empty;
        public string source_fbx_guid = string.Empty;
        public string import_profile_hash = string.Empty;
        public string manifest_sha256 = string.Empty;
        public string state_hash = string.Empty;
        public LockPin pin;
        public Dictionary<string, LockMaterialBinding> material_bindings = new Dictionary<string, LockMaterialBinding>();
    }

    [Serializable]
    public sealed class LockPin
    {
        public string mode = "manual";
        public string reason;
        public string version_id;
    }

    [Serializable]
    public sealed class LockMaterialBinding
    {
        public string mode = "managed";
        public string material_guid = string.Empty;
        public string managed_state_hash;
    }

    [Serializable]
    public sealed class AssetLocalMetadata
    {
        public int schema_version = 1;
        public string asset_id = string.Empty;
        public string installed_version_id = string.Empty;
        public string physical_spec_id = string.Empty;
        public float[] visual_correction = { 1f, 1f, 1f };
        public string analysis_snapshot_hash = string.Empty;
        public string last_transaction_id = string.Empty;
        public string accepted_at = string.Empty;
    }
}
