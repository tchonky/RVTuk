using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>One topo line as the session needs it: its id and where it goes.</summary>
    /// <param name="Polyline">Revit's tessellation, which the clipper and the sampler read.</param>
    /// <param name="CornerPolyline">The same line for the outline corner rule: an arc by the rule's
    /// own 1 mm chord count (<see cref="OutlineCornerHeights.ArcChordCount"/>), any other curve as
    /// <paramref name="Polyline"/>. Plan feet, like it.</param>
    public sealed record TopoLineCandidate(
        long LineId, IReadOnlyList<XyPoint> Polyline, IReadOnlyList<XyPoint> CornerPolyline);

    /// <summary>
    /// Reads the view's topo lines. Detail curves only: a model line given the _DP-Topo Line style
    /// would appear in every plan at once, which is exactly the confusion view-specific lines were
    /// chosen to avoid, so it is ignored rather than half-supported. Lines inside a group (a placed
    /// or pasted detail group) are skipped too: the session neither restyles nor labels them.
    ///
    /// The curve's Z is the view's sketch plane and is discarded — which is only sound in a plan
    /// view, and the session is what enforces that.
    /// </summary>
    public static class TopoLineCollector
    {
        public static IReadOnlyList<TopoLineCandidate> Collect(Document doc, View view)
        {
            var candidates = new List<TopoLineCandidate>();

            // Document-wide, narrowed by owner view: the collector's view constructor returns only
            // what is visible, and a topo line hidden in the view (Hide in View, a filter, its
            // style turned off) is still a topo line of the view — missing it would delete its
            // height text. ElementOwnerViewFilter keeps hidden elements.
            var lines = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WherePasses(new ElementOwnerViewFilter(view.Id))
                .Cast<CurveElement>()
                .Where(curveElement => curveElement.CurveElementType == CurveElementType.DetailCurve)
                .Where(curveElement => curveElement.GroupId == ElementId.InvalidElementId)
                .Where(TopoLineStyle.IsTopoLine);

            foreach (var line in lines)
            {
                var curve = line.GeometryCurve;
                if (curve == null) continue;

                var polyline = curve.Tessellate()
                    .Select(point => new XyPoint(point.X, point.Y))
                    .ToList();
                if (polyline.Count < 2) continue;

                candidates.Add(new TopoLineCandidate(line.Id.Value, polyline, CornerPolyline(curve) ?? polyline));
            }

            return candidates;
        }

        /// <summary>
        /// A bound arc in equal chords that stray at most 1 mm from it, or null for any other curve
        /// (whose Revit tessellation stands). The arc lies in the plan view's sketch plane, so its
        /// plan projection is the same arc.
        /// </summary>
        private static IReadOnlyList<XyPoint>? CornerPolyline(Curve curve)
        {
            if (curve is not Arc arc || !arc.IsBound || !(arc.Radius > 0)) return null;

            int n = OutlineCornerHeights.ArcChordCount(
                arc.Length / arc.Radius, arc.Radius * SurveyHeightText.MetresPerFoot);

            var points = new List<XyPoint>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                var point = arc.Evaluate((double)i / n, true);
                points.Add(new XyPoint(point.X, point.Y));
            }
            return points;
        }
    }
}
