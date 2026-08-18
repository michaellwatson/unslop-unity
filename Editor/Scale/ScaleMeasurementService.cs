using System;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Importing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unslop.UnityBridge.Editor.Scale
{
    public sealed class ScaleMeasurementService : IScaleMeasurementService
    {
        public Vector3 DefaultToleranceMetres { get; } = new Vector3(0.005f, 0.005f, 0.005f);

        public ScaleMeasurement MeasureRendererBounds(GameObject root, bool includeInactive = true)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var scope = MeasurementSubject.Acquire(root);
            try
            {
                return MeasureLive(scope.Root, includeInactive);
            }
            finally
            {
                scope.Dispose();
            }
        }

        public ScaleMeasurement MeasureLive(GameObject root, bool includeInactive = true)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var search = FindNamedChild(root.transform, WrapperPrefabBuilder.ModelName)
                         ?? FindNamedChild(root.transform, WrapperPrefabBuilder.VisualCorrectionName)
                         ?? root.transform;

            var meshBounds = ComputeWorldMeshAabb(search, includeInactive, out var meshCount);
            if (meshCount == 0 || !meshBounds.HasValue)
            {
                return new ScaleMeasurement(Vector3.zero, Vector3.zero, Vector3.zero, root.transform.position, 0);
            }

            var size = Abs(meshBounds.Value.size);
            var rendererBounds = ComputeRendererAabb(search, includeInactive, out var rendererCount);
            if (rendererBounds.HasValue)
            {
                size = ChooseStableSize(size, Abs(rendererBounds.Value.size), root);
            }

            BridgeLog.Info(
                $"Scale AABB mesh=({meshBounds.Value.size.x:F3},{meshBounds.Value.size.y:F3},{meshBounds.Value.size.z:F3}) " +
                $"chosen=({size.x:F3},{size.y:F3},{size.z:F3}) renderers={rendererCount} " +
                $"rootLossy={root.transform.lossyScale} persistent={EditorUtility.IsPersistent(root)}");

            return new ScaleMeasurement(
                meshBounds.Value.min,
                meshBounds.Value.max,
                size,
                meshBounds.Value.center,
                Math.Max(meshCount, rendererCount));
        }

        public ScaleCompareResult CompareToCanonical(
            ScaleMeasurement measured,
            Vector3 canonicalMetres,
            Vector3? toleranceMetres = null)
        {
            var tolerance = toleranceMetres ?? DefaultToleranceMetres;
            var delta = measured.SizeMetres - canonicalMetres;
            var within = Mathf.Abs(delta.x) <= tolerance.x
                         && Mathf.Abs(delta.y) <= tolerance.y
                         && Mathf.Abs(delta.z) <= tolerance.z;
            return new ScaleCompareResult(within, delta, tolerance, measured, canonicalMetres);
        }

        public static Vector3 GetSceneScale(GameObject root) =>
            root == null ? Vector3.one : root.transform.lossyScale;

        public static Transform FindVisualCorrection(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            return FindNamedChild(root.transform, WrapperPrefabBuilder.VisualCorrectionName);
        }

        public static bool ApproximatelyIdentityScale(Vector3 scale) =>
            Mathf.Abs(scale.x - 1f) < 0.0001f
            && Mathf.Abs(scale.y - 1f) < 0.0001f
            && Mathf.Abs(scale.z - 1f) < 0.0001f;

        /// <summary>
        /// Prefab assets are not in a scene, so Renderer.bounds often double-applies FBX file scale
        /// (or ignores it). Instantiate a hidden identity copy for measurement.
        /// </summary>
        public sealed class MeasurementSubject : IDisposable
        {
            readonly Scene _preview;
            readonly bool _ownsPreview;

            public GameObject Root { get; }
            public bool IsTemporary { get; }

            MeasurementSubject(GameObject root, bool temporary, Scene preview, bool ownsPreview)
            {
                Root = root;
                IsTemporary = temporary;
                _preview = preview;
                _ownsPreview = ownsPreview;
            }

            public static MeasurementSubject Acquire(GameObject source)
            {
                if (source == null)
                {
                    throw new ArgumentNullException(nameof(source));
                }

                if (!IsPrefabAsset(source))
                {
                    return new MeasurementSubject(source, false, default, false);
                }

                var prefab = source;
                var path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(path))
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? source;
                }

                try
                {
                    var preview = EditorSceneManager.NewPreviewScene();
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    if (instance == null)
                    {
                        EditorSceneManager.ClosePreviewScene(preview);
                        return new MeasurementSubject(source, false, default, false);
                    }

                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    instance.SetActive(true);
                    return new MeasurementSubject(instance, true, preview, true);
                }
                catch (Exception ex)
                {
                    BridgeLog.Warn("Could not instantiate prefab for scale measure: " + BridgeLog.Redact(ex.Message));
                    return new MeasurementSubject(source, false, default, false);
                }
            }

            public void Dispose()
            {
                if (_ownsPreview && _preview.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(_preview);
                    return;
                }

                if (IsTemporary && Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }
            }

            static bool IsPrefabAsset(GameObject go)
            {
                if (go == null)
                {
                    return false;
                }

                if (EditorUtility.IsPersistent(go))
                {
                    return true;
                }

                var type = PrefabUtility.GetPrefabAssetType(go);
                return type != PrefabAssetType.NotAPrefab
                       && PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.NotAPrefab
                       && !go.scene.IsValid();
            }
        }

        static Vector3 ChooseStableSize(Vector3 meshAabb, Vector3 rendererAabb, GameObject root)
        {
            var meshMag = Mathf.Max(meshAabb.x, meshAabb.y, meshAabb.z);
            var rendererMag = Mathf.Max(rendererAabb.x, rendererAabb.y, rendererAabb.z);
            if (meshMag < 1e-5f)
            {
                return rendererAabb;
            }

            var ratio = rendererMag / meshMag;
            // Prefab-asset Renderer.bounds often applies FBX file scale twice (~100x).
            if (ratio > 50f || ratio < 1f / 50f)
            {
                BridgeLog.Warn(
                    $"Scale bounds mismatch on '{root.name}': renderer={rendererAabb} meshMatrix={meshAabb} " +
                    $"(ratio={ratio:F1}). Using mesh-matrix AABB so an un-resized prefab is not recorded as huge.");
                return meshAabb;
            }

            return meshAabb;
        }

        static Bounds? ComputeWorldMeshAabb(Transform searchRoot, bool includeInactive, out int meshCount)
        {
            meshCount = 0;
            Bounds? bounds = null;
            foreach (var filter in searchRoot.GetComponentsInChildren<MeshFilter>(includeInactive))
            {
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                meshCount++;
                EncapsulateMesh(ref bounds, filter.sharedMesh, filter.transform.localToWorldMatrix);
            }

            foreach (var skinned in searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive))
            {
                if (skinned == null || skinned.sharedMesh == null)
                {
                    continue;
                }

                meshCount++;
                EncapsulateMesh(ref bounds, skinned.sharedMesh, skinned.localToWorldMatrix);
            }

            return bounds;
        }

        static Bounds? ComputeRendererAabb(Transform searchRoot, bool includeInactive, out int rendererCount)
        {
            rendererCount = 0;
            Bounds? bounds = null;
            foreach (var renderer in searchRoot.GetComponentsInChildren<Renderer>(includeInactive))
            {
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                rendererCount++;
                if (bounds == null)
                {
                    bounds = renderer.bounds;
                }
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(renderer.bounds);
                    bounds = b;
                }
            }

            return bounds;
        }

        static void EncapsulateMesh(ref Bounds? bounds, Mesh mesh, Matrix4x4 localToWorld)
        {
            var local = mesh.bounds;
            var min = local.min;
            var max = local.max;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                var world = localToWorld.MultiplyPoint3x4(corner);
                if (bounds == null)
                {
                    bounds = new Bounds(world, Vector3.zero);
                }
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(world);
                    bounds = b;
                }
            }
        }

        static Vector3 Abs(Vector3 v) =>
            new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static Transform FindNamedChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamedChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
