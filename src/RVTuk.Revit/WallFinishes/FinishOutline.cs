using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.WallFinishes
{
    /// <summary>One edge of the walls' exposed outline, with the finish of the face it lies on.</summary>
    public sealed class FinishEdge
    {
        public Curve Edge = null!;
        public ElementId MaterialId = ElementId.InvalidElementId;

        /// <summary>
        /// Part of the loop, so the offset stays closed, but never drawn: a jamb, in the fallback
        /// where the openings could not be removed and the line breaks at each one instead.
        /// </summary>
        public bool Skip;
    }

    /// <summary>A closed loop of the outline, and the merged section's face it bounds.</summary>
    public sealed class FinishLoop
    {
        /// <summary>A free solid's face, not the document's, so it outlives the rolled-back read.</summary>
        public PlanarFace Face = null!;
        public List<FinishEdge> Edges = new List<FinishEdge>();
    }

    public sealed class FinishOutlineResult
    {
        public List<FinishLoop> Loops = new List<FinishLoop>();
        public int BooleanFailures;
        public int EdgesWithoutMaterial;
        public bool UsedFallback;
    }

    /// <summary>
    /// Reads the exposed outline of the walls a plan view cuts: every wall's section at the cut
    /// height, merged into one shape, so faces where walls stand against each other — joined or
    /// not — are inside it and drop out. The doors, windows and openings are deleted first in a
    /// transaction that is always rolled back, so the outline runs straight across them.
    /// </summary>
    public static class FinishOutline
    {
        private const double Mm = 1 / 304.8;
        private const double Tolerance = Mm;

        /// <summary>Call outside any open transaction. Leaves the document as it found it.</summary>
        public static FinishOutlineResult Read(Document doc, ViewPlan view)
        {
            var walls = CollectWalls(doc, view);
            if (walls.Count == 0) return new FinishOutlineResult();

            var inserts = new HashSet<ElementId>(
                walls.SelectMany(w => w.FindInserts(true, false, true, true)));

            using (var tx = new Transaction(doc, "Wall Finishes — read"))
            {
                try
                {
                    tx.Start();
                    if (inserts.Count > 0) doc.Delete(inserts.ToList());
                    doc.Regenerate();
                    return ReadWalls(doc, view, walls, inserts, dropInsertFaces: false);
                }
                catch (Exception) when (inserts.Count > 0)
                {
                    // Borrowed by someone else, or anything else the deletion threw: read the
                    // walls as they are and break the line at each opening (spec step 2).
                }
                finally
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                }
            }

            var result = ReadWalls(doc, view, walls, inserts, dropInsertFaces: true);
            result.UsedFallback = true;
            return result;
        }

        private static List<Wall> CollectWalls(Document doc, ViewPlan view)
        {
            var walls = new Dictionary<ElementId, Wall>();
            foreach (var wall in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>())
            {
                var members = wall.IsStackedWall
                    ? wall.GetStackedWallMemberIds().Select(doc.GetElement).OfType<Wall>()
                    : new[] { wall };
                foreach (var member in members)
                {
                    if (member.WallType.Kind == WallKind.Basic) walls[member.Id] = member;
                }
            }
            return walls.Values.ToList();
        }

        private static FinishOutlineResult ReadWalls(
            Document doc, ViewPlan view, List<Wall> walls, HashSet<ElementId> inserts, bool dropInsertFaces)
        {
            var result = new FinishOutlineResult();
            double z = CutHeight(doc, view);
            var box = ThinBox(walls, z);

            var faces = new List<WallFaces>();
            Solid? merged = null;
            var separate = new List<Solid>();

            foreach (var wall in walls)
            {
                var solids = Solids(wall);
                faces.Add(new WallFaces(wall, solids));

                bool failed = false;
                foreach (var solid in solids)
                {
                    Solid section;
                    try
                    {
                        section = BooleanOperationsUtils.ExecuteBooleanOperation(solid, box, BooleanOperationsType.Intersect);
                    }
                    catch (Exception)
                    {
                        failed = true;
                        continue;
                    }
                    if (section == null || section.Volume <= 0) continue; // not cut: a low wall

                    try
                    {
                        merged = merged == null
                            ? section
                            : BooleanOperationsUtils.ExecuteBooleanOperation(merged, section, BooleanOperationsType.Union);
                    }
                    catch (Exception)
                    {
                        // ponytail: a section the union refuses is drawn from its own outline alone,
                        // so it may still get lines where it touches another wall.
                        failed = true;
                        separate.Add(section);
                    }
                }
                if (failed) result.BooleanFailures++;
            }

            foreach (var solid in separate.Prepend(merged).OfType<Solid>())
            {
                foreach (var face in solid.Faces.OfType<PlanarFace>().Where(f => f.FaceNormal.Z < -0.99))
                {
                    var lift = Transform.CreateTranslation(new XYZ(0, 0, z - face.Origin.Z));
                    foreach (var curveLoop in face.GetEdgesAsCurveLoops())
                    {
                        var loop = new FinishLoop { Face = face };
                        foreach (var curve in curveLoop)
                        {
                            loop.Edges.Add(Classify(doc, curve.CreateTransformed(lift), faces, inserts, dropInsertFaces));
                        }
                        result.EdgesWithoutMaterial += FillFreeEnds(loop);
                        result.Loops.Add(loop);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The view's cut height in the model's internal coordinates: the cut plane's level plus
        /// its offset. <c>ProjectElevation</c>, not <c>Elevation</c>, since the latter is measured
        /// from the project's chosen elevation base.
        /// </summary>
        private static double CutHeight(Document doc, ViewPlan view)
        {
            var range = view.GetViewRange();
            var level = doc.GetElement(range.GetLevelId(PlanViewPlane.CutPlane)) as Level ?? view.GenLevel;
            return level.ProjectElevation + range.GetOffset(PlanViewPlane.CutPlane);
        }

        private static Solid ThinBox(List<Wall> walls, double z)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var bb in walls.Select(w => w.get_BoundingBox(null)).Where(b => b != null))
            {
                minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y);
                maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y);
            }
            double pad = 1 / 0.3048, bottom = z - Mm / 2;
            var a = new XYZ(minX - pad, minY - pad, bottom);
            var b = new XYZ(maxX + pad, minY - pad, bottom);
            var c = new XYZ(maxX + pad, maxY + pad, bottom);
            var d = new XYZ(minX - pad, maxY + pad, bottom);
            var rectangle = CurveLoop.Create(new List<Curve>
            {
                Line.CreateBound(a, b), Line.CreateBound(b, c), Line.CreateBound(c, d), Line.CreateBound(d, a),
            });
            return GeometryCreationUtilities.CreateExtrusionGeometry(new[] { rectangle }, XYZ.BasisZ, Mm);
        }

        private static List<Solid> Solids(Wall wall)
        {
            var geometry = wall.get_Geometry(new Options { ComputeReferences = true });
            return geometry == null
                ? new List<Solid>()
                : geometry.OfType<Solid>().Where(s => s.Volume > 0).ToList();
        }

        private static FinishEdge Classify(
            Document doc, Curve edge, List<WallFaces> walls, HashSet<ElementId> inserts, bool dropInsertFaces)
        {
            var mid = edge.Evaluate(0.5, true);
            var result = new FinishEdge { Edge = edge };

            foreach (var candidate in walls.Where(w => w.Near(mid)))
            {
                var hit = candidate.FaceAt(mid, out var layerIndex);
                if (hit == null) continue;

                if (dropInsertFaces && candidate.Wall.GetGeneratingElementIds(hit).Any(inserts.Contains))
                {
                    result.Skip = true;
                }
                else if (doc.IsPainted(candidate.Wall.Id, hit))
                {
                    result.MaterialId = doc.GetPaintedMaterial(candidate.Wall.Id, hit);
                }
                else if (layerIndex != null)
                {
                    result.MaterialId = LayerMaterial(doc, candidate.Wall, layerIndex.Value);
                }
                // A free end (neither side face) with no paint: FillFreeEnds gives it its neighbour's.
                return result;
            }
            return result;
        }

        private static ElementId LayerMaterial(Document doc, Wall wall, int which)
        {
            var structure = wall.WallType.GetCompoundStructure();
            var id = ElementId.InvalidElementId;
            if (structure != null && structure.LayerCount > 0)
            {
                id = structure.GetMaterialId(which < 0 ? structure.LayerCount - 1 : which);
            }
            if (id == ElementId.InvalidElementId)
            {
                id = Category.GetCategory(doc, BuiltInCategory.OST_Walls)?.Material?.Id ?? ElementId.InvalidElementId;
            }
            return id;
        }

        /// <summary>
        /// A free end takes the finish of the side the line turns from: the previous drawn edge on
        /// the loop, wrapping round. Returns how many edges are still left without a material.
        /// </summary>
        private static int FillFreeEnds(FinishLoop loop)
        {
            var edges = loop.Edges;
            int missing = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Skip || edges[i].MaterialId != ElementId.InvalidElementId) continue;
                for (int back = 1; back < edges.Count; back++)
                {
                    var previous = edges[(i - back + edges.Count) % edges.Count];
                    if (previous.Skip) continue;
                    if (previous.MaterialId != ElementId.InvalidElementId)
                    {
                        edges[i].MaterialId = previous.MaterialId;
                        break;
                    }
                }
                if (edges[i].MaterialId == ElementId.InvalidElementId) missing++;
            }
            return missing;
        }

        /// <summary>A wall's faces, sorted the way the material rule needs them.</summary>
        private sealed class WallFaces
        {
            public readonly Wall Wall;
            private readonly BoundingBoxXYZ? _box;
            private readonly List<Face> _exterior;
            private readonly List<Face> _interior;
            private readonly List<Face> _all;

            public WallFaces(Wall wall, List<Solid> solids)
            {
                Wall = wall;
                _box = wall.get_BoundingBox(null);
                _exterior = SideFaces(wall, ShellLayerType.Exterior);
                _interior = SideFaces(wall, ShellLayerType.Interior);
                _all = solids.SelectMany(s => s.Faces.Cast<Face>()).ToList();
            }

            public bool Near(XYZ p) =>
                _box != null
                && p.X >= _box.Min.X - Tolerance && p.X <= _box.Max.X + Tolerance
                && p.Y >= _box.Min.Y - Tolerance && p.Y <= _box.Max.Y + Tolerance;

            /// <summary>
            /// The face <paramref name="p"/> lies on. <paramref name="layerIndex"/>: 0 for the
            /// exterior side, −1 for the interior side (the last layer), null for any other face.
            /// </summary>
            public Face? FaceAt(XYZ p, out int? layerIndex)
            {
                layerIndex = 0;
                var hit = _exterior.FirstOrDefault(f => On(f, p));
                if (hit != null) return hit;
                layerIndex = -1;
                hit = _interior.FirstOrDefault(f => On(f, p));
                if (hit != null) return hit;
                layerIndex = null;
                return _all.FirstOrDefault(f => On(f, p) && !(f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 0.99));
            }

            private static bool On(Face face, XYZ p)
            {
                var projection = face.Project(p);
                return projection != null && projection.Distance < Tolerance;
            }

            private static List<Face> SideFaces(Wall wall, ShellLayerType side)
            {
                try
                {
                    return HostObjectUtils.GetSideFaces(wall, side)
                        .Select(r => wall.GetGeometryObjectFromReference(r) as Face)
                        .OfType<Face>()
                        .ToList();
                }
                catch (Exception)
                {
                    return new List<Face>();
                }
            }
        }
    }
}
