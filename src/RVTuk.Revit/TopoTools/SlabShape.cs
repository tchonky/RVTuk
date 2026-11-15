using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;
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
    /// Vertices are found by position, and a held <c>SlabShapeVertex</c> is checked with
    /// <c>IsValidObject</c> and looked up again if it has gone stale. Whether a vertex stays valid
    /// after its neighbours change is unverified, and a position cannot go stale.
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
        /// created from <paramref name="loops"/> and regenerated, so it is still flat.</summary>
        public static ReplayOutcome Replay(
            Document doc, Element target, SourceShape shape, IReadOnlyList<CurveLoop> loops)
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
            var plan = ShapeReplayPlanner.Plan(shape.Vertices, flat, Outline(loops));

            // Edge points first, while the whole surface is still flat at topZ, so every
            // DrawPoint location is on the top face. Raising anything before this would tilt an
            // edge, and the next point drawn on it would miss the face and come back null.
            int pointsLost = DrawEdgePoints(editor, plan.EdgePoints, topZ);

            // Interior points take their absolute Z directly, in one batch.
            pointsLost += AddPoints(editor, plan.Additions);

            // Then every raise, corners and edge points alike, relative to the flat top.
            var raises = plan.Offsets
                .Select(offset => (At: flat[offset.TargetIndex], offset.Offset))
                .Concat(plan.EdgePoints.Select(point => (At: point, Offset: point.Z - topZ)))
                .Where(raise => Math.Abs(raise.Offset) > ShapeReplayPlanner.ToleranceFeet)
                .ToList();
            pointsLost += Raise(editor, raises);

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
        /// Points on an edge, drawn onto the flat top one at a time. <c>AddPoints</c> refuses
        /// them, and so does 2025's <c>AddPoint</c>, which is why the deprecated
        /// <c>DrawPoint</c> is used. Each call regenerates, but only edge points pay that.
        /// Returns how many could not be drawn.
        /// </summary>
        private static int DrawEdgePoints(SlabShapeEditor editor, IReadOnlyList<XyzPoint> points, double topZ)
        {
            int lost = 0;
            foreach (var point in points)
            {
                try
                {
#pragma warning disable CS0618
                    if (editor.DrawPoint(new XYZ(point.X, point.Y, topZ)) == null) lost++;
#pragma warning restore CS0618
                }
                catch (Exception)
                {
                    lost++;
                }
            }
            return lost;
        }

        /// <summary>
        /// Interior points, all at once. They are strictly inside the outline, so the batch should
        /// hold; if Revit still refuses it, each point is tried on its own so one bad point does
        /// not cost the rest. Returns how many could not be added.
        /// </summary>
        private static int AddPoints(SlabShapeEditor editor, IReadOnlyList<XyzPoint> points)
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
                }
                catch (Exception)
                {
                    lost++;
                }
            }
            return lost;
        }

        /// <summary>
        /// Raises or lowers each vertex found at a position. The live vertices are read once and
        /// matched in memory, rather than walking the editor for every raise, which matters on a
        /// survey toposolid with thousands of points. A vertex that has gone stale since is looked
        /// up again. Returns how many could not be raised.
        /// </summary>
        private static int Raise(SlabShapeEditor editor, IReadOnlyList<(XyzPoint At, double Offset)> raises)
        {
            if (raises.Count == 0) return 0;

            var vertices = new List<SlabShapeVertex>();
            var positions = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            {
                vertices.Add(vertex);
                positions.Add(ToPoint(vertex.Position));
            }

            int lost = 0;
            foreach (var raise in raises)
            {
                int index = ShapeReplayPlanner.IndexOfXy(positions, raise.At);
                var vertex = index < 0 ? null : vertices[index];
                if (vertex != null && !vertex.IsValidObject) vertex = VertexAt(editor, raise.At);
                if (vertex == null)
                {
                    lost++;
                    continue;
                }

                try
                {
                    editor.ModifySubElement(vertex, raise.Offset);
                }
                catch (Exception)
                {
                    lost++;
                }
            }
            return lost;
        }

        /// <summary>The loops tessellated to plan points, the form the planner's edge test reads.</summary>
        private static IReadOnlyList<IReadOnlyList<XyPoint>> Outline(IReadOnlyList<CurveLoop> loops)
        {
            var outline = new List<IReadOnlyList<XyPoint>>();
            foreach (var loop in loops)
            {
                var points = new List<XyPoint>();
                foreach (Curve curve in loop)
                {
                    foreach (var point in curve.Tessellate()) points.Add(new XyPoint(point.X, point.Y));
                }
                outline.Add(points);
            }
            return outline;
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
