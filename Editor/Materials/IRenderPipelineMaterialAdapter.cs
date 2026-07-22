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
        Material CreateMaterial(MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole);
        void ApplyTextures(Material material, MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole);
    }
}
