using System;
using System.Collections.Generic;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    /// <summary>
    /// HDRP adapter placeholder for a future pipeline. MVP imports textures only and reports unavailable.
    /// </summary>
    public sealed class HdrpMaterialAdapterPlaceholder : IRenderPipelineMaterialAdapter
    {
        public string PipelineId => "hdrp";
        public bool IsAvailable => false;
        public string UnavailableReason => "HDRP material adapter is not implemented in MVP; textures imported, auto material unavailable.";

        public Material CreateMaterial(MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole)
        {
            throw new NotSupportedException(UnavailableReason);
        }

        public void ApplyTextures(Material material, MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole)
        {
            throw new NotSupportedException(UnavailableReason);
        }
    }
}
