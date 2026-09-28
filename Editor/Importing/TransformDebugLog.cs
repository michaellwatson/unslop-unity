using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unslop.UnityBridge.Editor.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Importing
{
    /// <summary>
    /// Verbose transform dumps used when Bridge debug mode is on — especially Meshy vs artist
    /// rotation / scale differences across version updates.
    /// </summary>
    public static class TransformDebugLog
    {
        const float Epsilon = 1e-4f;

        public static void LogHierarchy(string label, GameObject root, string pathHint = null)
        {
            if (!BridgeDebugMode.Enabled || root == null)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("TRANSFORM DUMP [").Append(label).Append(']');
            if (!string.IsNullOrEmpty(pathHint))
            {
                sb.Append(" path=").Append(pathHint);
            }

            sb.Append(" root=").Append(root.name);
            BridgeLog.Debug(sb.ToString());

            foreach (var node in Collect(root.transform))
            {
                BridgeLog.Debug(FormatNode(node));
            }
        }

        public static void LogAssetHierarchy(string label, string assetPath)
        {
            if (!BridgeDebugMode.Enabled || string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null)
            {
                BridgeLog.Debug($"{label}: no GameObject at {assetPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(go);
            try
            {
                LogHierarchy(label, instance, assetPath);
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        /// <summary>
        /// Side-by-side compare of two hierarchies (e.g. installed Meshy vs staged artist FBX).
        /// Emphasises local/world scale and rotation deltas.
        /// </summary>
        public static void CompareHierarchies(
            string leftLabel,
            GameObject left,
            string rightLabel,
            GameObject right)
        {
            if (!BridgeDebugMode.Enabled)
            {
                return;
            }

            if (left == null && right == null)
            {
                BridgeLog.Debug($"TRANSFORM COMPARE {leftLabel} vs {rightLabel}: both null");
                return;
            }

            BridgeLog.Debug($"TRANSFORM COMPARE [{leftLabel}] vs [{rightLabel}]");

            var leftMap = left == null
                ? new Dictionary<string, TransformSnapshot>(StringComparer.Ordinal)
                : Collect(left.transform).ToDictionary(n => n.Path, n => n, StringComparer.Ordinal);
            var rightMap = right == null
                ? new Dictionary<string, TransformSnapshot>(StringComparer.Ordinal)
                : Collect(right.transform).ToDictionary(n => n.Path, n => n, StringComparer.Ordinal);

            var allPaths = leftMap.Keys.Union(rightMap.Keys).OrderBy(p => p, StringComparer.Ordinal);
            var scaleDiffs = 0;
            var rotationDiffs = 0;
            var missing = 0;

            foreach (var path in allPaths)
            {
                leftMap.TryGetValue(path, out var a);
                rightMap.TryGetValue(path, out var b);
                if (a == null)
                {
                    missing++;
                    BridgeLog.Debug($"  + only in {rightLabel}: {path} {FormatCompact(b)}");
                    continue;
                }

                if (b == null)
                {
                    missing++;
                    BridgeLog.Debug($"  - only in {leftLabel}: {path} {FormatCompact(a)}");
                    continue;
                }

                var scaleDelta = b.LocalScale - a.LocalScale;
                var lossyDelta = b.LossyScale - a.LossyScale;
                var localEulerDelta = DeltaEuler(a.LocalEuler, b.LocalEuler);
                var worldEulerDelta = DeltaEuler(a.WorldEuler, b.WorldEuler);
                var scaleChanged = scaleDelta.sqrMagnitude > Epsilon * Epsilon
                                   || lossyDelta.sqrMagnitude > Epsilon * Epsilon;
                var rotationChanged = localEulerDelta.sqrMagnitude > Epsilon * Epsilon
                                      || worldEulerDelta.sqrMagnitude > Epsilon * Epsilon
                                      || Quaternion.Angle(a.LocalRotation, b.LocalRotation) > 0.01f
                                      || Quaternion.Angle(a.WorldRotation, b.WorldRotation) > 0.01f;

                if (!scaleChanged && !rotationChanged)
                {
                    continue;
                }

                if (scaleChanged)
                {
                    scaleDiffs++;
                }

                if (rotationChanged)
                {
                    rotationDiffs++;
                }

                BridgeLog.Debug(
                    $"  Δ {path}" +
                    (scaleChanged
                        ? $" scale {Fmt(a.LocalScale)}→{Fmt(b.LocalScale)} (Δ{Fmt(scaleDelta)})" +
                          $" lossy {Fmt(a.LossyScale)}→{Fmt(b.LossyScale)} (Δ{Fmt(lossyDelta)})"
                        : string.Empty) +
                    (rotationChanged
                        ? $" rotLocal {Fmt(a.LocalEuler)}→{Fmt(b.LocalEuler)} (Δ{Fmt(localEulerDelta)}°)" +
                          $" rotWorld {Fmt(a.WorldEuler)}→{Fmt(b.WorldEuler)} (Δ{Fmt(worldEulerDelta)}°)" +
                          $" angleLocal={Quaternion.Angle(a.LocalRotation, b.LocalRotation):F2}°" +
                          $" angleWorld={Quaternion.Angle(a.WorldRotation, b.WorldRotation):F2}°"
                        : string.Empty));
            }

            BridgeLog.Debug(
                $"TRANSFORM COMPARE summary: scaleDiffs={scaleDiffs} rotationDiffs={rotationDiffs} " +
                $"missingOrExtra={missing} nodesLeft={leftMap.Count} nodesRight={rightMap.Count}");
        }

        public static void CompareAssetHierarchies(
            string leftLabel,
            string leftAssetPath,
            string rightLabel,
            string rightAssetPath)
        {
            if (!BridgeDebugMode.Enabled)
            {
                return;
            }

            var leftGo = LoadInstance(leftAssetPath);
            var rightGo = LoadInstance(rightAssetPath);
            try
            {
                CompareHierarchies(
                    $"{leftLabel}:{leftAssetPath}",
                    leftGo,
                    $"{rightLabel}:{rightAssetPath}",
                    rightGo);
            }
            finally
            {
                if (leftGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(leftGo);
                }

                if (rightGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(rightGo);
                }
            }
        }

        static GameObject LoadInstance(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return prefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        static List<TransformSnapshot> Collect(Transform root)
        {
            var list = new List<TransformSnapshot>();
            CollectRecursive(root, root.name, list);
            return list;
        }

        static void CollectRecursive(Transform t, string path, List<TransformSnapshot> list)
        {
            list.Add(new TransformSnapshot
            {
                Path = path,
                LocalPosition = t.localPosition,
                LocalEuler = t.localEulerAngles,
                LocalRotation = t.localRotation,
                LocalScale = t.localScale,
                WorldPosition = t.position,
                WorldEuler = t.eulerAngles,
                WorldRotation = t.rotation,
                LossyScale = t.lossyScale
            });

            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                CollectRecursive(child, path + "/" + child.name, list);
            }
        }

        static string FormatNode(TransformSnapshot n) =>
            $"  {n.Path} localPos={Fmt(n.LocalPosition)} localEuler={Fmt(n.LocalEuler)} " +
            $"localScale={Fmt(n.LocalScale)} worldPos={Fmt(n.WorldPosition)} " +
            $"worldEuler={Fmt(n.WorldEuler)} lossyScale={Fmt(n.LossyScale)}";

        static string FormatCompact(TransformSnapshot n) =>
            $"localEuler={Fmt(n.LocalEuler)} localScale={Fmt(n.LocalScale)} lossy={Fmt(n.LossyScale)}";

        static Vector3 DeltaEuler(Vector3 a, Vector3 b) =>
            new Vector3(Mathf.DeltaAngle(a.x, b.x), Mathf.DeltaAngle(a.y, b.y), Mathf.DeltaAngle(a.z, b.z));

        static string Fmt(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4})", v.x, v.y, v.z);

        sealed class TransformSnapshot
        {
            public string Path;
            public Vector3 LocalPosition;
            public Vector3 LocalEuler;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public Vector3 WorldPosition;
            public Vector3 WorldEuler;
            public Quaternion WorldRotation;
            public Vector3 LossyScale;
        }
    }
}
