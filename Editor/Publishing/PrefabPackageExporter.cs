using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.Importing;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEditor;
using UnityEngine;
#if UNSLOP_HAS_FBX_EXPORTER
using UnityEditor.Formats.Fbx.Exporter;
#endif

namespace Unslop.UnityBridge.Editor.Publishing
{
    /// <summary>
    /// Builds a neutral Bridge package (FBX + textures + materials.json + preview) under Library/Unslop/Publish.
    /// </summary>
    public static class PrefabPackageExporter
    {
        static readonly string[] AllowedTextureExt =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".webp"
        };

        public static PrefabPackageExportResult Export(GameObject prefabOrInstance, string displayName = null)
        {
            if (prefabOrInstance == null)
            {
                throw new ArgumentNullException(nameof(prefabOrInstance));
            }

            var root = ResolveExportRoot(prefabOrInstance, out var linkedAssetId);
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
            {
                throw new InvalidOperationException(
                    "Skinned / animated meshes are not supported for Bridge publish (static mesh MVP only).");
            }

            if (root.GetComponentsInChildren<MeshRenderer>(true).Length == 0
                && root.GetComponentsInChildren<MeshFilter>(true).Length == 0)
            {
                throw new InvalidOperationException("Prefab has no mesh renderers to export.");
            }

            var name = string.IsNullOrWhiteSpace(displayName)
                ? (string.IsNullOrWhiteSpace(prefabOrInstance.name) ? "UnslopAsset" : prefabOrInstance.name)
                : displayName.Trim();

            var staging = Path.Combine(ManagedPaths.PublishDir, Guid.NewGuid().ToString("N"));
            ManagedPaths.EnsureDirectory(staging);
            ManagedPaths.EnsureDirectory(Path.Combine(staging, "textures"));

            var modelPath = Path.Combine(staging, "model.fbx");
            WriteModelFbx(root, modelPath);

            var materials = new MaterialsManifest { schema_version = 1 };
            var files = new List<PrefabPackageFile>
            {
                CreateFileEntry(modelPath, "model.fbx", "model", "application/octet-stream")
            };

            CollectMaterialsAndTextures(root, staging, materials, files);

            var materialsBytes = Encoding.UTF8.GetBytes(
                Newtonsoft.Json.JsonConvert.SerializeObject(
                    PrefabPackageManifestBuilder.ToMaterialsDictionary(materials),
                    Newtonsoft.Json.Formatting.Indented));
            var materialsPath = Path.Combine(staging, "materials.json");
            File.WriteAllBytes(materialsPath, materialsBytes);
            files.Add(CreateFileEntry(materialsPath, "materials.json", "material_manifest", "application/json", materialsBytes));

            var previewPath = Path.Combine(staging, "preview.png");
            WritePreviewPng(root, previewPath);
            if (File.Exists(previewPath))
            {
                files.Add(CreateFileEntry(previewPath, "preview.png", "preview", "image/png"));
            }

            BridgeLog.Info($"Prefab package staged at {staging} files={files.Count} display='{name}'");

            return new PrefabPackageExportResult
            {
                StagingRoot = staging,
                DisplayName = name,
                LinkedAssetId = linkedAssetId,
                ModelAbsolutePath = modelPath,
                Materials = materials,
                Files = files
            };
        }

        public static GameObject ResolveExportRoot(GameObject source, out string linkedAssetId)
        {
            linkedAssetId = null;
            if (source == null)
            {
                return null;
            }

            var reference = source.GetComponentInChildren<UnslopAssetReference>(true);
            if (reference != null)
            {
                linkedAssetId = reference.AssetId;
                var model = FindNamedChild(source.transform, WrapperPrefabBuilder.ModelName)
                            ?? FindNamedChild(source.transform, WrapperPrefabBuilder.VisualNestedName);
                if (model != null)
                {
                    return model.gameObject;
                }
            }

            return source;
        }

        static void WriteModelFbx(GameObject root, string absolutePath)
        {
            var existingFbx = TryFindSourceFbxPath(root);
            if (!string.IsNullOrEmpty(existingFbx) && File.Exists(existingFbx))
            {
                File.Copy(existingFbx, absolutePath, overwrite: true);
                BridgeLog.Info("Publish: copied existing FBX " + existingFbx);
                return;
            }

#if UNSLOP_HAS_FBX_EXPORTER
            var tempAssetPath = $"Assets/Unslop/__PublishTemp_{Guid.NewGuid():N}.fbx";
            try
            {
                ManagedPaths.EnsureDirectory("Assets/Unslop");
                var exported = ModelExporter.ExportObject(tempAssetPath, root);
                if (string.IsNullOrEmpty(exported) || !File.Exists(ManagedPaths.ToFull(exported)))
                {
                    throw new InvalidOperationException("FBX Exporter returned no file.");
                }

                File.Copy(ManagedPaths.ToFull(exported), absolutePath, overwrite: true);
                BridgeLog.Info("Publish: exported FBX via Unity FBX Exporter.");
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tempAssetPath) != null
                    || File.Exists(ManagedPaths.ToFull(tempAssetPath)))
                {
                    AssetDatabase.DeleteAsset(tempAssetPath);
                }
            }
#else
            throw new InvalidOperationException(
                "No source .fbx found on the prefab and Unity FBX Exporter is not installed. " +
                "Install Package Manager → com.unity.formats.fbx, or publish from a prefab whose mesh comes from an FBX asset.");
#endif
        }

        static string TryFindSourceFbxPath(GameObject root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                var meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (EndsWithFbx(meshPath))
                {
                    return ManagedPaths.ToFull(meshPath);
                }
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (source != null)
            {
                var path = AssetDatabase.GetAssetPath(source);
                if (EndsWithFbx(path))
                {
                    return ManagedPaths.ToFull(path);
                }
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                var corresponding = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (corresponding == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(corresponding);
                if (EndsWithFbx(path))
                {
                    return ManagedPaths.ToFull(path);
                }
            }

            return null;
        }

        static void CollectMaterialsAndTextures(
            GameObject root,
            string staging,
            MaterialsManifest materials,
            List<PrefabPackageFile> files)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var materialIndex = 0;
            var usedTextureRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var renderer in renderers)
            {
                var shared = renderer.sharedMaterials;
                if (shared == null)
                {
                    continue;
                }

                for (var slot = 0; slot < shared.Length; slot++)
                {
                    var mat = shared[slot];
                    if (mat == null)
                    {
                        continue;
                    }

                    materialIndex++;
                    var materialId = "mat_" + SanitizeToken(mat.name, "material") + "_" + materialIndex;
                    var slotId = "slot_" + SanitizeToken(renderer.name, "body") + "_" + slot;
                    var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    TryExportTexture(mat, "_BaseMap", "base_color", staging, files, usedTextureRelPaths, textures);
                    if (!textures.ContainsKey("base_color"))
                    {
                        TryExportTexture(mat, "_MainTex", "base_color", staging, files, usedTextureRelPaths, textures);
                    }

                    TryExportTexture(mat, "_BumpMap", "normal", staging, files, usedTextureRelPaths, textures);
                    TryExportTexture(mat, "_MetallicGlossMap", "roughness", staging, files, usedTextureRelPaths, textures);
                    TryExportTexture(mat, "_SpecGlossMap", "roughness", staging, files, usedTextureRelPaths, textures);

                    materials.materials.Add(new MaterialDefinition
                    {
                        material_id = materialId,
                        display_name = mat.name,
                        model = "metallic_roughness",
                        textures = textures
                    });
                    materials.slots.Add(new MaterialSlot
                    {
                        slot_id = slotId,
                        display_name = renderer.name + " [" + slot + "]",
                        material_id = materialId
                    });
                }
            }

            if (materials.materials.Count == 0)
            {
                materials.materials.Add(new MaterialDefinition
                {
                    material_id = "mat_default",
                    display_name = "Default",
                    model = "metallic_roughness",
                    textures = new Dictionary<string, string>()
                });
                materials.slots.Add(new MaterialSlot
                {
                    slot_id = "slot_body",
                    display_name = "Body",
                    material_id = "mat_default"
                });
            }
        }

        static void TryExportTexture(
            Material material,
            string property,
            string role,
            string staging,
            List<PrefabPackageFile> files,
            HashSet<string> usedRelPaths,
            Dictionary<string, string> textures)
        {
            if (material == null || !material.HasProperty(property))
            {
                return;
            }

            var texture = material.GetTexture(property) as Texture2D;
            if (texture == null)
            {
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(texture);
            byte[] bytes = null;
            string mediaType = "image/png";
            string fileName;

            if (!string.IsNullOrEmpty(assetPath))
            {
                var ext = Path.GetExtension(assetPath).ToLowerInvariant();
                if (AllowedTextureExt.Contains(ext))
                {
                    var full = ManagedPaths.ToFull(assetPath);
                    if (File.Exists(full))
                    {
                        bytes = File.ReadAllBytes(full);
                        mediaType = MediaTypeForExtension(ext);
                        fileName = SanitizeToken(Path.GetFileNameWithoutExtension(assetPath), role) + ext;
                    }
                }
            }

            if (bytes == null)
            {
                try
                {
                    bytes = EncodeTexturePng(texture);
                    fileName = SanitizeToken(texture.name, role) + ".png";
                    mediaType = "image/png";
                }
                catch (Exception ex)
                {
                    BridgeLog.Warn($"Publish: could not encode texture {texture.name}: {ex.Message}");
                    return;
                }
            }

            var relative = "textures/" + fileName;
            var unique = relative;
            var n = 1;
            while (usedRelPaths.Contains(unique))
            {
                unique = $"textures/{Path.GetFileNameWithoutExtension(fileName)}_{n}{Path.GetExtension(fileName)}";
                n++;
            }

            usedRelPaths.Add(unique);
            var abs = Path.Combine(staging, unique.Replace('/', Path.DirectorySeparatorChar));
            ManagedPaths.EnsureDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, bytes);
            files.Add(CreateFileEntry(abs, unique, "texture", mediaType, bytes));
            textures[role] = unique;
        }

        static byte[] EncodeTexturePng(Texture2D texture)
        {
            if (texture.isReadable)
            {
                return texture.EncodeToPNG();
            }

            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                readable.Apply();
                var png = readable.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(readable);
                return png;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static void WritePreviewPng(GameObject root, string absolutePath)
        {
            try
            {
                var utility = new PreviewRenderUtility();
                try
                {
                    utility.cameraFieldOfView = 30f;
                    var rect = new Rect(0, 0, 256, 256);
                    utility.BeginPreview(rect, GUIStyle.none);
                    var bounds = CalculateBounds(root);
                    var radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
                    utility.camera.transform.position = bounds.center + new Vector3(radius, radius * 0.6f, -radius);
                    utility.camera.transform.LookAt(bounds.center);
                    utility.lights[0].intensity = 1.2f;
                    utility.ambientColor = new Color(0.35f, 0.35f, 0.35f, 0f);

                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        Mesh mesh = null;
                        if (r is MeshRenderer)
                        {
                            mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                        }

                        if (mesh == null || r.sharedMaterial == null)
                        {
                            continue;
                        }

                        utility.DrawMesh(mesh, r.localToWorldMatrix, r.sharedMaterial, 0);
                    }

                    utility.Render(true);
                    var texture = utility.EndPreview() as Texture2D;
                    if (texture != null)
                    {
                        File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
                    }
                }
                finally
                {
                    utility.Cleanup();
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Warn("Publish preview capture failed: " + BridgeLog.Redact(ex.Message));
            }
        }

        static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        static PrefabPackageFile CreateFileEntry(
            string absolutePath,
            string relativePath,
            string role,
            string mediaType,
            byte[] contents = null)
        {
            contents ??= File.ReadAllBytes(absolutePath);
            return new PrefabPackageFile
            {
                AbsolutePath = absolutePath,
                RelativePath = relativePath.Replace('\\', '/'),
                Role = role,
                MediaType = mediaType,
                Contents = contents,
                ByteLength = contents.LongLength,
                Sha256Hex = HashUtil.Sha256Bytes(contents, prefixed: false)
            };
        }

        static Transform FindNamedChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            foreach (Transform child in root)
            {
                var found = FindNamedChild(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static bool EndsWithFbx(string path) =>
            !string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        static string MediaTypeForExtension(string ext) =>
            ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".tga" => "image/tga",
                ".tif" or ".tiff" => "image/tiff",
                ".exr" => "image/x-exr",
                ".webp" => "image/webp",
                _ => "image/png"
            };

        static string SanitizeToken(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var sb = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (c == '_' || c == '-')
                {
                    sb.Append('_');
                }
            }

            var result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? fallback : result;
        }
    }
}
