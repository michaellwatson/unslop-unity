using System;
using Unslop.UnityBridge.Editor.Importing;
using UnityEngine;

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

            // Prefer Model subtree under VisualCorrection so UserContent does not inflate bounds.
            var model = FindNamedChild(root.transform, WrapperPrefabBuilder.ModelName)
                        ?? FindNamedChild(root.transform, WrapperPrefabBuilder.VisualCorrectionName)
                        ?? root.transform;

            var renderers = model.GetComponentsInChildren<Renderer>(includeInactive);
            if (renderers == null || renderers.Length == 0)
            {
                return new ScaleMeasurement(Vector3.zero, Vector3.zero, Vector3.zero, root.transform.position, 0);
            }

            var bounds = new Bounds();
            var initialised = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                // Renderer-corner bounds (world AABB of mesh corners via renderer.bounds).
                if (!initialised)
                {
                    bounds = renderer.bounds;
                    initialised = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!initialised)
            {
                return new ScaleMeasurement(Vector3.zero, Vector3.zero, Vector3.zero, root.transform.position, 0);
            }

            return new ScaleMeasurement(
                bounds.min,
                bounds.max,
                bounds.size,
                bounds.center,
                renderers.Length);
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

        /// <summary>
        /// Scene scale is the lossy scale of the instance root; canonical dimensions are physical metres.
        /// </summary>
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
