using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The one element a session writes to — a toposolid or a floor — with its plan outline read
    /// once, since every topo line is clipped against it.
    /// </summary>
    public sealed class SlabTarget
    {
        private SlabTarget(
            Element element, IReadOnlyList<IReadOnlyList<XyPoint>> loops, IReadOnlyList<XyPoint> corners)
        {
            Element = element;
            Loops = loops;
            Corners = corners;
        }

        /// <summary>A <see cref="Toposolid"/> or a <see cref="Floor"/>.</summary>
        public Element Element { get; }

        /// <summary>The outline, outer loop and holes, as tessellated plan points.</summary>
        public IReadOnlyList<IReadOnlyList<XyPoint>> Loops { get; }

        /// <summary>
        /// The outline's and holes' real corners: each sketch curve's start point, as
        /// SlabShape reads a sloped floor's corners. Not the tessellation, whose inner points on an
        /// arc are no vertices of the element.
        /// </summary>
        public IReadOnlyList<XyPoint> Corners { get; }

        public long Id => Element.Id.Value;

        /// <summary>"Toposolid 123" / "Floor 123", as the converter names elements.</summary>
        public string Label => $"{(Element is Floor ? "Floor" : "Toposolid")} {Id}";

        /// <returns>Null when the element is neither a toposolid nor a floor, or has no readable outline.</returns>
        public static SlabTarget? From(Document doc, Element element)
        {
            ElementId sketchId;
            if (element is Toposolid toposolid) sketchId = toposolid.SketchId;
            else if (element is Floor floor) sketchId = floor.SketchId;
            else return null;

            var corners = new List<XyPoint>();
            var loops = ReadLoops(doc, sketchId, corners);
            return loops.Count == 0 ? null : new SlabTarget(element, loops, corners);
        }

        /// <summary>
        /// The footprint, loop by loop. Each curve is tessellated separately, so consecutive curves
        /// repeat their shared endpoint — harmless, and
        /// <see cref="RVTuk.Core.TopoTools.PolygonContainment"/> is written to tolerate exactly that.
        /// </summary>
        private static IReadOnlyList<IReadOnlyList<XyPoint>> ReadLoops(
            Document doc, ElementId sketchId, List<XyPoint> corners)
        {
            var loops = new List<IReadOnlyList<XyPoint>>();

            if (doc.GetElement(sketchId) is not Sketch sketch) return loops;

            foreach (CurveArray loop in sketch.Profile)
            {
                var points = new List<XyPoint>();
                var starts = new List<XyPoint>();
                foreach (Curve curve in loop)
                {
                    var start = curve.GetEndPoint(0);
                    starts.Add(new XyPoint(start.X, start.Y));
                    foreach (var point in curve.Tessellate())
                    {
                        points.Add(new XyPoint(point.X, point.Y));
                    }
                }
                if (points.Count < 3) continue;
                loops.Add(points);
                corners.AddRange(starts);
            }

            return loops;
        }
    }
}
