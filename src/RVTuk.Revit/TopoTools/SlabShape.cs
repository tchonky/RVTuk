using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>A floor's or toposolid's top surface as positions, read before the element is deleted.</summary>
    public sealed record SourceShape(IReadOnlyList<XyzPoint> Vertices, IReadOnlyList<CreaseEnds> Creases)
    {
        public static readonly SourceShape Flat =
            new SourceShape(Array.Empty<XyzPoint>(), Array.Empty<CreaseEnds>());
    }

    public sealed record ReplayOutcome(int PointsLost, int CreasesLost);

    /// <summary>
    /// Reads a slab's shape and replays it onto a freshly created flat one with the same outline.
    /// <see cref="ShapeReplayPlanner"/> decides what to do; this class only talks to
    /// <c>SlabShapeEditor</c>.
    ///
    /// Vertices are looked up by position every time they are needed, never held across edits.
    /// Whether a <c>SlabShapeVertex</c> stays valid after its neighbours change is unverified, and
    /// a position cannot go stale.
    /// </summary>
    public static class SlabShape
    {
        public static SlabShapeEditor? EditorOf(Element element) => element switch
        {
            Floor floor => floor.GetSlabShapeEditor(),
            Toposolid toposolid => toposolid.GetSlabShapeEditor(),
            _ => null,
        };

        public static SourceShape Read(Element source, IReadOnlyList<CurveLoop> loops)
        {
            var editor = EditorOf(source);
            if (editor != null && editor.IsEnabled)
            {
                var vertices = Positions(editor);
                if (vertices.Count > 0)
                {
                    var creases = new List<CreaseEnds>();
                    foreach (SlabShapeCrease crease in editor.SlabShapeCreases)
                    {
                        if (crease.CreaseType != SlabShapeCreaseType.UserDrawn) continue;
                        var curve = crease.Curve;
                        creases.Add(new CreaseEnds(ToPoint(curve.GetEndPoint(0)), ToPoint(curve.GetEndPoint(1))));
                    }
                    return new SourceShape(vertices, creases);
                }
            }

            // No shape points, yet a floor with a slope arrow is not flat: read its corners off
            // the top face instead.
            if (source is Floor floor)
                return new SourceShape(SlopedCorners(floor, loops), Array.Empty<CreaseEnds>());

            return SourceShape.Flat;
        }

        /// <summary>Caller must be inside a transaction; <paramref name="target"/> must be freshly
        /// created and regenerated, so it is still flat.</summary>
        public static ReplayOutcome Replay(Document doc, Element target, SourceShape shape)
        {
            if (shape.Vertices.Count == 0) return new ReplayOutcome(0, 0);

            var editor = EditorOf(target)
                ?? throw new InvalidOperationException("the new element cannot take shape points");
            if (!editor.IsEnabled) editor.Enable();
            doc.Regenerate();

            var flat = Positions(editor);
            if (flat.Count == 0) return new ReplayOutcome(shape.Vertices.Count, shape.Creases.Count);

            // Fresh and flat: every vertex sits at the top, which is where ModifySubElement
            // offsets and DrawPoint locations are measured from.
            double topZ = flat[0].Z;
            var plan = ShapeReplayPlanner.Plan(shape.Vertices, flat);

            // Points first, while the surface is still flat, so DrawPoint's fallback location at
            // topZ really is on the top face.
            int pointsLost = AddPoints(editor, plan.Additions, topZ);

            foreach (var offset in plan.Offsets)
            {
                var vertex = VertexAt(editor, flat[offset.TargetIndex]);
                if (vertex == null)
                {
                    pointsLost++;
                    continue;
                }

                try
                {
                    editor.ModifySubElement(vertex, offset.Offset);
                }
                catch (Exception)
                {
                    pointsLost++;
                }
            }

            int creasesLost = 0;
            if (shape.Creases.Count > 0)
            {
                doc.Regenerate();
                var live = Positions(editor);
                var creasePlan = ShapeReplayPlanner.PlanCreases(live, shape.Creases);
                creasesLost = creasePlan.Dropped;

                foreach (var pair in creasePlan.Pairs)
                {
                    var start = VertexAt(editor, live[pair.StartIndex]);
                    var end = VertexAt(editor, live[pair.EndIndex]);
                    if (start == null || end == null)
                    {
                        creasesLost++;
                        continue;
                    }

                    try
                    {
#if REVIT2025
                        editor.AddSplitLine(start, end);
#else
                        editor.DrawSplitLine(start, end);
#endif
                    }
                    catch (Exception)
                    {
                        creasesLost++;
                    }
                }
            }

            return new ReplayOutcome(pointsLost, creasesLost);
        }

        /// <summary>
        /// All at once when Revit takes them. <c>AddPoints</c> wants points strictly inside the
        /// boundary, so a single point on an edge fails the whole batch. Then each point is tried
        /// on its own, falling back to <c>DrawPoint</c> on the flat top plus an offset. Returns how
        /// many could not be placed at all.
        /// </summary>
        private static int AddPoints(SlabShapeEditor editor, IReadOnlyList<XyzPoint> points, double topZ)
        {
            if (points.Count == 0) return 0;

            try
            {
                editor.AddPoints(points.Select(ToRevit).ToList());
                return 0;
            }
            catch (Exception)
            {
                // Retry one by one below.
            }

            int lost = 0;
            foreach (var point in points)
            {
                try
                {
                    editor.AddPoints(new List<XYZ> { ToRevit(point) });
                    continue;
                }
                catch (Exception)
                {
                    // Try drawing it instead.
                }

                try
                {
                    // Deprecated in 2025, but its replacement AddPoint wants points strictly inside
                    // the boundary, the very restriction this fallback exists to get around.
#pragma warning disable CS0618
                    var vertex = editor.DrawPoint(new XYZ(point.X, point.Y, topZ));
#pragma warning restore CS0618
                    if (vertex == null)
                    {
                        lost++;
                        continue;
                    }
                    editor.ModifySubElement(vertex, point.Z - topZ);
                }
                catch (Exception)
                {
                    lost++;
                }
            }
            return lost;
        }

        private static IReadOnlyList<XyzPoint> SlopedCorners(Floor floor, IReadOnlyList<CurveLoop> loops)
        {
            PlanarFace? top = null;
            try
            {
                foreach (var reference in HostObjectUtils.GetTopFaces(floor))
                {
                    if (floor.GetGeometryObjectFromReference(reference) is PlanarFace face)
                    {
                        top = face;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                return Array.Empty<XyzPoint>();
            }

            // Flat, or no planar top to read: nothing to replay.
            if (top == null || Math.Abs(top.FaceNormal.Z) > 1 - 1e-9) return Array.Empty<XyzPoint>();

            var origin = ToPoint(top.Origin);
            var normal = ToPoint(top.FaceNormal);
            var corners = new List<XyzPoint>();
            foreach (var loop in loops)
            {
                foreach (Curve curve in loop)
                {
                    var start = curve.GetEndPoint(0);
                    var z = PlaneHeight.At(origin, normal, start.X, start.Y);
                    if (z != null) corners.Add(new XyzPoint(start.X, start.Y, z.Value));
                }
            }
            return corners;
        }

        private static List<XyzPoint> Positions(SlabShapeEditor editor)
        {
            var positions = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices) positions.Add(ToPoint(vertex.Position));
            return positions;
        }

        private static SlabShapeVertex? VertexAt(SlabShapeEditor editor, XyzPoint at)
        {
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            {
                var position = vertex.Position;
                if (Math.Abs(position.X - at.X) <= ShapeReplayPlanner.ToleranceFeet &&
                    Math.Abs(position.Y - at.Y) <= ShapeReplayPlanner.ToleranceFeet)
                {
                    return vertex;
                }
            }
            return null;
        }

        internal static XyzPoint ToPoint(XYZ point) => new XyzPoint(point.X, point.Y, point.Z);

        private static XYZ ToRevit(XyzPoint point) => new XYZ(point.X, point.Y, point.Z);
    }
}
