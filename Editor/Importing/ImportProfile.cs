using System;
using System.Collections.Generic;
using System.Text;
using Unslop.UnityBridge.Editor.Downloads;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Importing
{
    /// <summary>
    /// Deterministic ModelImporter / TextureImporter settings and import_profile_hash.
    /// </summary>
    public static class ImportProfile
    {
        public const string ProfileId = "unslop.static_mesh.v1";

        public static string ComputeImportProfileHash()
        {
            var payload = BuildCanonicalPayload();
            return HashUtil.PrefixSha256(HashUtil.Sha256HexUtf8(payload));
        }

        public static void ApplyModelImporter(ModelImporter importer)
        {
            if (importer == null)
            {
                throw new ArgumentNullException(nameof(importer));
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.generateSecondaryUV = false;
            importer.addCollider = false;
            importer.keepQuads = false;
            importer.indexFormat = ModelImporterIndexFormat.Auto;
            importer.normalImportMode = ModelImporterNormalMode.Import;
            importer.normalSmoothingSource = ModelImporterNormalSmoothingSource.PreferSmoothingGroups;
            importer.tangentImportMode = ModelImporterTangents.CalculateMikk;
        }

        public static void ApplyTextureImporter(TextureImporter importer, string relativePath)
        {
            if (importer == null)
            {
                throw new ArgumentNullException(nameof(importer));
            }

            var isNormal = LooksLikeNormalMap(relativePath);
            var isRoughness = LooksLikeRoughness(relativePath);
            var isPreview = LooksLikePreview(relativePath);

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal && !isRoughness;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = !isPreview;
            importer.streamingMipmaps = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = isPreview ? 1 : 4;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = isPreview ? 1024 : 4096;
        }

        public static bool LooksLikeNormalMap(string relativePath)
        {
            var name = (relativePath ?? string.Empty).ToLowerInvariant();
            return name.Contains("normal") || name.Contains("_n.") || name.EndsWith("_n.png");
        }

        public static bool LooksLikeRoughness(string relativePath)
        {
            var name = (relativePath ?? string.Empty).ToLowerInvariant();
            return name.Contains("rough") || name.Contains("roughness");
        }

        public static bool LooksLikePreview(string relativePath)
        {
            var name = (relativePath ?? string.Empty).ToLowerInvariant();
            return name.Contains("preview");
        }

        static string BuildCanonicalPayload()
        {
            var lines = new List<string>
            {
                "profile_id=" + ProfileId,
                "model.globalScale=1",
                "model.useFileScale=true",
                "model.materialImportMode=None",
                "model.animationType=None",
                "model.normalImportMode=Import",
                "model.tangentImportMode=CalculateMikk",
                "model.meshCompression=Off",
                "texture.normal.sRGB=false",
                "texture.roughness.sRGB=false",
                "texture.color.sRGB=true",
                "texture.mipmapEnabled=true",
                "texture.maxTextureSize=4096",
                "texture.compression=Compressed"
            };
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }
    }
}
