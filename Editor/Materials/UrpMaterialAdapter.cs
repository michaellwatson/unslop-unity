using System;
using System.Collections.Generic;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    public sealed class UrpMaterialAdapter : IRenderPipelineMaterialAdapter
    {
        public string PipelineId => "urp";

        public bool IsAvailable
        {
            get
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                return shader != null;
            }
        }

        public string UnavailableReason =>
            IsAvailable ? null : "URP Lit shader not found. Install Universal RP package.";

        public Material CreateMaterial(MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(UnavailableReason);
            }

            var material = new Material(shader)
            {
                name = string.IsNullOrWhiteSpace(definition?.display_name)
                    ? definition?.material_id ?? "UnslopMaterial"
                    : definition.display_name
            };
            ApplyTextures(material, definition, texturesByRole);
            return material;
        }

        public void ApplyTextures(Material material, MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            texturesByRole ??= new Dictionary<string, Texture2D>();
            definition ??= new MaterialDefinition();

            if (TryGet(texturesByRole, out var baseColor, "base_color", "basecolor", "albedo", "diffuse"))
            {
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", baseColor);
                }

                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", baseColor);
                }
            }

            if (TryGet(texturesByRole, out var normal, "normal", "normal_map", "bump"))
            {
                if (material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                    if (material.HasProperty("_BumpScale"))
                    {
                        material.SetFloat("_BumpScale", 1f);
                    }
                }
            }

            // Roughness → Smoothness (invert). Prefer metallic/gloss map alpha when present.
            if (TryGet(texturesByRole, out var roughness, "roughness", "roughness_map"))
            {
                if (material.HasProperty("_MetallicGlossMap"))
                {
                    material.SetTexture("_MetallicGlossMap", roughness);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (material.HasProperty("_Smoothness"))
                {
                    // When a roughness map drives the gloss channel, keep smoothness at 1 so the map dominates.
                    material.SetFloat("_Smoothness", 1f);
                }

                if (material.HasProperty("_SmoothnessTextureChannel"))
                {
                    material.SetFloat("_SmoothnessTextureChannel", 0f);
                }

                BridgeLog.Info($"Applied roughness→smoothness mapping for material {definition.material_id}");
            }
            else if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.5f);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            if (material.HasProperty("_WorkflowMode"))
            {
                material.SetFloat("_WorkflowMode", 1f); // Metallic
            }
        }

        static bool TryGet(IReadOnlyDictionary<string, Texture2D> map, out Texture2D texture, params string[] keys)
        {
            foreach (var key in keys)
            {
                foreach (var kv in map)
                {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                    {
                        texture = kv.Value;
                        return true;
                    }
                }
            }

            texture = null;
            return false;
        }
    }
}
