using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>One topo line as the session needs it: its id and where it goes.</summary>
    public sealed record TopoLineCandidate(long LineId, IReadOnlyList<XyPoint> Polyline);

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

            // Document-wide, filtered by owner view: a view collector returns only what is visible,
            // and a topo line hidden in the view (Hide in View, a filter, its style turned off) is
            // still a topo line of the view — missing it would delete its height text.
            var lines = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(curveElement => curveElement.OwnerViewId == view.Id)
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

                candidates.Add(new TopoLineCandidate(line.Id.Value, polyline));
            }

            return candidates;
        }
    }
}
