using System.Collections.Generic;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Manifests;

namespace Unslop.UnityBridge.Editor.Publishing
{
    public sealed class PrefabPackageFile
    {
        public string RelativePath { get; set; }
        public string Role { get; set; }
        public string MediaType { get; set; }
        public string AbsolutePath { get; set; }
        public byte[] Contents { get; set; }
        public long ByteLength { get; set; }
        public string Sha256Hex { get; set; }
    }

    public sealed class PrefabPackageExportResult
    {
        public string StagingRoot { get; set; }
        public string DisplayName { get; set; }
        public string LinkedAssetId { get; set; }
        public string ModelAbsolutePath { get; set; }
        public MaterialsManifest Materials { get; set; }
        public List<PrefabPackageFile> Files { get; set; } = new List<PrefabPackageFile>();
    }

    public sealed class PrefabPublishRequest
    {
        public UnityEngine.GameObject Prefab { get; set; }
        public string DisplayName { get; set; }
        public string ProjectId { get; set; }
        /// <summary>When set, publish a new version of this asset; otherwise create a catalog asset.</summary>
        public string ExistingAssetId { get; set; }
        public bool Recommend { get; set; } = true;
    }

    public sealed class PrefabPublishResult
    {
        public string AssetId { get; set; }
        public string AssetVersionId { get; set; }
        public int VersionNumber { get; set; }
        public string DisplayName { get; set; }
        public AssetSummaryDto Asset { get; set; }
        public AssetVersionSummaryDto Version { get; set; }
    }
}
