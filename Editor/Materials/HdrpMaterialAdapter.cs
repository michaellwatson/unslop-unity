using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Materials
{
    /// <summary>
    /// Builds HDRP/Lit materials from engine-neutral metallic-roughness maps.
    /// Metallic, occlusion, and roughness are packed into an HDRP mask map
    /// (R metallic, G AO, B detail mask, A smoothness).
    /// </summary>
    public sealed class HdrpMaterialAdapter : IRenderPipelineMaterialAdapter
    {
        public const string LitShaderName = "HDRP/Lit";

        public string PipelineId => "hdrp";
        public string DerivedMapDirectory { get; set; }

        public bool IsAvailable => Shader.Find(LitShaderName) != null;

        public string UnavailableReason =>
            IsAvailable ? null : "HDRP/Lit shader not found. Install the High Definition RP package.";

        public Material CreateMaterial(MaterialDefinition definition, IReadOnlyDictionary<string, Texture2D> texturesByRole)
        {
            var shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(UnavailableReason);
            }

            var material = new Material(shader)
            {
                name = DisplayName(definition)
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

            var shader = Shader.Find(LitShaderName);
            if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            texturesByRole ??= new Dictionary<string, Texture2D>();
            definition ??= new MaterialDefinition();

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            if (TryGet(texturesByRole, out var baseColor, "base_color", "basecolor", "albedo", "diffuse"))
            {
                SetTexture(material, "_BaseColorMap", baseColor);
            }

            if (TryGet(texturesByRole, out var normal, "normal", "normal_map", "bump"))
            {
                SetTexture(material, "_NormalMap", normal);
                material.EnableKeyword("_NORMALMAP");
                material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
                SetFloat(material, "_NormalMapSpace", 0f);
                SetFloat(material, "_NormalScale", 1f);
            }

            TryGet(texturesByRole, out var metallic, "metallic", "metalness", "metallic_map");
            TryGet(texturesByRole, out var roughness, "roughness", "roughness_map");
            TryGet(texturesByRole, out var ao, "ao", "occlusion", "ambient_occlusion");

            if (metallic != null || roughness != null || ao != null)
            {
                var mask = WriteMaskMap(definition, metallic, roughness, ao);
                if (mask != null)
                {
                    SetTexture(material, "_MaskMap", mask);
                    material.EnableKeyword("_MASKMAP");
                    SetFloat(material, "_MetallicRemapMin", 0f);
                    SetFloat(material, "_MetallicRemapMax", 1f);
                    SetFloat(material, "_SmoothnessRemapMin", 0f);
                    SetFloat(material, "_SmoothnessRemapMax", 1f);
                    SetFloat(material, "_AORemapMin", 0f);
                    SetFloat(material, "_AORemapMax", 1f);
                    BridgeLog.Info($"Packed HDRP mask map for material {definition.material_id}");
                }
            }
            else
            {
                material.DisableKeyword("_MASKMAP");
                SetFloat(material, "_Metallic", 0f);
                SetFloat(material, "_Smoothness", 0.5f);
            }

            if (TryGet(texturesByRole, out var emissive, "emissive", "emission", "emissive_color"))
            {
                SetTexture(material, "_EmissiveColorMap", emissive);
                if (material.HasProperty("_EmissiveColor"))
                {
                    material.SetColor("_EmissiveColor", Color.white);
                }

                material.EnableKeyword("_EMISSIVE_COLOR_MAP");
                material.EnableKeyword("_EMISSIVE_MAPPING_BASE");
            }

            ValidateHdrpMaterial(material);
        }

        static void ValidateHdrpMaterial(Material material)
        {
            var type = Type.GetType(
                "UnityEngine.Rendering.HighDefinition.LitAPI, Unity.RenderPipelines.HighDefinition.Runtime");
            var method = type?.GetMethod(
                "ValidateMaterial",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(null, new object[] { material });
        }

        Texture2D WriteMaskMap(MaterialDefinition definition, Texture2D metallic, Texture2D roughness, Texture2D ao)
        {
            if (string.IsNullOrWhiteSpace(DerivedMapDirectory))
            {
                BridgeLog.Warn(
                    $"HDRP mask map skipped for {definition.material_id}: no derived-map directory.");
                return null;
            }

            var width = 0;
            var height = 0;
            Grow(metallic, ref width, ref height);
            Grow(roughness, ref width, ref height);
            Grow(ao, ref width, ref height);
            if (width < 1 || height < 1)
            {
                return null;
            }

            width = Mathf.Min(width, 4096);
            height = Mathf.Min(height, 4096);

            var metalPx = metallic != null ? ReadLinear(metallic, width, height) : null;
            var roughPx = roughness != null ? ReadLinear(roughness, width, height) : null;
            var aoPx = ao != null ? ReadLinear(ao, width, height) : null;
            var packed = new Color[width * height];
            for (var i = 0; i < packed.Length; i++)
            {
                var metal = metalPx != null ? metalPx[i].r : 0f;
                var occlusion = aoPx != null ? aoPx[i].r : 1f;
                var smoothness = roughPx != null ? 1f - roughPx[i].r : 0.5f;
                packed[i] = new Color(
                    Mathf.Clamp01(metal),
                    Mathf.Clamp01(occlusion),
                    1f,
                    Mathf.Clamp01(smoothness));
            }

            var mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = Sanitize(definition.material_id) + "_MaskMap"
            };
            mask.SetPixels(packed);
            mask.Apply(false, false);

            var assetPath = $"{DerivedMapDirectory.TrimEnd('/')}/{Sanitize(definition.material_id)}_MaskMap.png";
            ManagedPaths.EnsureDirectory(DerivedMapDirectory);
            var fullPath = Path.Combine(
                ManagedPaths.ProjectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(fullPath, mask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(mask);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        static void Grow(Texture2D texture, ref int width, ref int height)
        {
            if (texture == null)
            {
                return;
            }

            width = Mathf.Max(width, texture.width);
            height = Mathf.Max(height, texture.height);
        }

        static Color[] ReadLinear(Texture source, int width, int height)
        {
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false
            };
            var rt = RenderTexture.GetTemporary(descriptor);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false, false);
                var pixels = readable.GetPixels();
                UnityEngine.Object.DestroyImmediate(readable);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        static string DisplayName(MaterialDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition?.display_name))
            {
                return definition.display_name;
            }

            return string.IsNullOrWhiteSpace(definition?.material_id) ? "UnslopMaterial" : definition.material_id;
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "material";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
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
