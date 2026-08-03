using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>One topo line as the runner needs it: where it goes, and how high.</summary>
    public sealed record TopoLineCandidate(
        long LineId,
        double? ElevationFeet,
        IReadOnlyList<XyPoint> Polyline,
        double LengthFeet);

    /// <summary>
    /// Reads the view's topo lines. Detail curves only: a model line given the _DP-Topo Line style
    /// would appear in every plan at once, which is exactly the confusion view-specific lines were
    /// chosen to avoid, so it is ignored rather than half-supported.
    ///
    /// The curve's Z is the view's sketch plane and is discarded — which is only sound in a plan
    /// view, and the runner is what enforces that.
    /// </summary>
    public static class TopoLineCollector
    {
        public static IReadOnlyList<TopoLineCandidate> Collect(Document doc, View view)
        {
            var candidates = new List<TopoLineCandidate>();

            var lines = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(curveElement => curveElement.CurveElementType == CurveElementType.DetailCurve)
                .Where(TopoLineStyle.IsTopoLine);

            foreach (var line in lines)
            {
                var curve = line.GeometryCurve;
                if (curve == null) continue;

                var polyline = curve.Tessellate()
                    .Select(point => new XyPoint(point.X, point.Y))
                    .ToList();
                if (polyline.Count < 2) continue;

                double? elevation = TopoElevationStore.TryGet(line, out var feet)
                    ? feet
                    : (double?)null;

                candidates.Add(new TopoLineCandidate(line.Id.Value, elevation, polyline, curve.Length));
            }

            return candidates;
        }
    }
}
