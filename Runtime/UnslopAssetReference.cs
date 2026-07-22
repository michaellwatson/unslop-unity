using UnityEngine;

namespace Unslop.UnityBridge
{
    /// <summary>
    /// Stable runtime identity attached to managed Unslop wrapper prefabs.
    /// Network and installation logic remain Editor-only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnslopAssetReference : MonoBehaviour
    {
        [SerializeField] string assetId = string.Empty;
        [SerializeField] string installedVersionId = string.Empty;
        [SerializeField] string physicalSpecId = string.Empty;
        [SerializeField] string wrapperPrefabGuid = string.Empty;

        public string AssetId => assetId;
        public string InstalledVersionId => installedVersionId;
        public string PhysicalSpecId => physicalSpecId;
        public string WrapperPrefabGuid => wrapperPrefabGuid;

        public void Configure(string assetIdValue, string versionId, string physicalSpecIdValue, string wrapperGuid)
        {
            assetId = assetIdValue ?? string.Empty;
            installedVersionId = versionId ?? string.Empty;
            physicalSpecId = physicalSpecIdValue ?? string.Empty;
            wrapperPrefabGuid = wrapperGuid ?? string.Empty;
        }
    }
}
