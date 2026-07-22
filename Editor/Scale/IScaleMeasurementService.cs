using UnityEngine;

namespace Unslop.UnityBridge.Editor.Scale
{
    public readonly struct ScaleMeasurement
    {
        public Vector3 BoundsMin { get; }
        public Vector3 BoundsMax { get; }
        public Vector3 SizeMetres { get; }
        public Vector3 Centre { get; }
        public int RendererCount { get; }

        public ScaleMeasurement(Vector3 boundsMin, Vector3 boundsMax, Vector3 sizeMetres, Vector3 centre, int rendererCount)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            SizeMetres = sizeMetres;
            Centre = centre;
            RendererCount = rendererCount;
        }

        public float[] ToArray() => new[] { SizeMetres.x, SizeMetres.y, SizeMetres.z };
    }

    public readonly struct ScaleCompareResult
    {
        public bool WithinTolerance { get; }
        public Vector3 DeltaMetres { get; }
        public Vector3 ToleranceMetres { get; }
        public ScaleMeasurement Measured { get; }
        public Vector3 ExpectedMetres { get; }

        public ScaleCompareResult(
            bool withinTolerance,
            Vector3 deltaMetres,
            Vector3 toleranceMetres,
            ScaleMeasurement measured,
            Vector3 expectedMetres)
        {
            WithinTolerance = withinTolerance;
            DeltaMetres = deltaMetres;
            ToleranceMetres = toleranceMetres;
            Measured = measured;
            ExpectedMetres = expectedMetres;
        }
    }

    /// <summary>
    /// Renderer-corner bounds measurement with tolerance compare.
    /// Scene scale (instance transform) is distinct from canonical physical dimensions.
    /// </summary>
    public interface IScaleMeasurementService
    {
        ScaleMeasurement MeasureRendererBounds(GameObject root, bool includeInactive = true);
        ScaleCompareResult CompareToCanonical(ScaleMeasurement measured, Vector3 canonicalMetres, Vector3? toleranceMetres = null);
        Vector3 DefaultToleranceMetres { get; }
    }
}
