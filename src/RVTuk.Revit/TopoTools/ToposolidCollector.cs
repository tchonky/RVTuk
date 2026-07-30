using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The toposolids a run may write to: those visible in the view, so phase, design option and
    /// worksets already decide what "the toposolid below" means without this tool re-implementing
    /// any of it.
    /// </summary>
    public static class ToposolidCollector
    {
        public static IReadOnlyList<ToposolidTarget> Collect(Document doc, View view)
        {
            var targets = new List<ToposolidTarget>();

            var solids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Toposolid)
                .WhereElementIsNotElementType()
                .OfType<Toposolid>();

            foreach (var solid in solids)
            {
                // A subdivision carries a shape of its own; its points belong to the host, and
                // writing to both would have them fight each other.
                if (solid.HostTopoId != null && solid.HostTopoId != ElementId.InvalidElementId) continue;

                var loops = ReadLoops(doc, solid);
                if (loops.Count == 0) continue;

                var box = solid.get_BoundingBox(null);
                if (box == null) continue;

                targets.Add(new ToposolidTarget(solid, loops, box.Min.Z, box.Max.Z));
            }

            return targets;
        }

        /// <summary>
        /// The footprint, loop by loop. Each curve is tessellated separately, so consecutive curves
        /// repeat their shared endpoint — harmless, and
        /// <see cref="RVTuk.Core.TopoTools.PolygonContainment"/> is written to tolerate exactly that.
        /// </summary>
        private static IReadOnlyList<IReadOnlyList<XyPoint>> ReadLoops(Document doc, Toposolid solid)
        {
            var loops = new List<IReadOnlyList<XyPoint>>();

            if (doc.GetElement(solid.SketchId) is not Sketch sketch) return loops;

            foreach (CurveArray loop in sketch.Profile)
            {
                var points = new List<XyPoint>();
                foreach (Curve curve in loop)
                {
                    foreach (var point in curve.Tessellate())
                    {
                        points.Add(new XyPoint(point.X, point.Y));
                    }
                }
                if (points.Count >= 3) loops.Add(points);
            }

            return loops;
        }
    }
}
