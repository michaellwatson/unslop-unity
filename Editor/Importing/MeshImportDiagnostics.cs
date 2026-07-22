using System.IO;
using System.Text;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Importing
{
    /// <summary>
    /// Install/scale diagnostics: file hashes and mesh bounds so stale FBX imports are obvious.
    /// </summary>
    public static class MeshImportDiagnostics
    {
        public static string ShortHash(string sha256)
        {
            return HashUtil.ShortHash(sha256);
        }

        public static void LogFile(string label, string fullPath, string expectedSha256 = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                BridgeLog.Warn($"{label}: missing file {fullPath}");
                return;
            }

            var info = new FileInfo(fullPath);
            var actual = HashUtil.Sha256File(fullPath);
            var sb = new StringBuilder();
            sb.Append(label)
                .Append(": bytes=").Append(info.Length)
                .Append(" sha=").Append(ShortHash(actual));
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var match = HashUtil.EqualsHash(expectedSha256, actual);
                sb.Append(" expected=").Append(ShortHash(expectedSha256))
                    .Append(match ? " MATCH" : " MISMATCH");
                BridgeLog.Info(sb.ToString());
                if (!match)
                {
                    BridgeLog.Warn($"{label}: on-disk hash does not match manifest/grant.");
                }

                return;
            }

            BridgeLog.Info(sb.ToString());
        }

        public static void LogAssetMeshBounds(string label, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null)
            {
                // FBX may expose Mesh directly.
                var meshOnly = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                if (meshOnly != null)
                {
                    BridgeLog.Info($"{label}: mesh-only bounds size={Format(meshOnly.bounds.size)} path={assetPath}");
                    return;
                }

                BridgeLog.Warn($"{label}: could not load GameObject/Mesh at {assetPath}");
                return;
            }

            LogGameObjectMeshBounds(label, go, assetPath);
        }

        public static void LogGameObjectMeshBounds(string label, GameObject root, string pathHint = null)
        {
            if (root == null)
            {
                BridgeLog.Warn($"{label}: null GameObject");
                return;
            }

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var localSize = Vector3.zero;
            var worldSize = Vector3.zero;
            var meshCount = 0;
            var initialised = false;
            Bounds world = default;

            foreach (var filter in filters)
            {
                if (filter?.sharedMesh == null)
                {
                    continue;
                }

                meshCount++;
                localSize = Vector3.Max(localSize, filter.sharedMesh.bounds.size);
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!initialised)
                {
                    world = renderer.bounds;
                    initialised = true;
                }
                else
                {
                    world.Encapsulate(renderer.bounds);
                }
            }

            if (initialised)
            {
                worldSize = world.size;
            }

            BridgeLog.Info(
                $"{label}: meshes={meshCount} renderers={renderers.Length} " +
                $"meshLocalSize={Format(localSize)} worldAabbSize={Format(worldSize)} " +
                $"rootLocalScale={Format(root.transform.localScale)} rootLossyScale={Format(root.transform.lossyScale)}" +
                (string.IsNullOrEmpty(pathHint) ? string.Empty : $" path={pathHint}"));
        }

        static string Format(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    }
}
