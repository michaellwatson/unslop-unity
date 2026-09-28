using System.Collections.Generic;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    public interface IRenderPipelineMaterialAdapter
    {
        string PipelineId { get; }
        bool IsAvailable { get; }
        string UnavailableReason { get; }

        /// <summary>
        /// Asset folder for derived maps (HDRP mask maps). Unused by adapters that assign source textures directly.
        /// </summary>
        string DerivedMapDirectory { get; set; }

        Material CreateMaterial(MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole);
        void ApplyTextures(Material material, MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole);
    }
}
