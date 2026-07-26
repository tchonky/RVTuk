using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    public enum DimensionCandidateKind
    {
        Wall,
        Opening,
    }

    /// <summary>One element that may cross a reference line, tagged with how to dimension it.</summary>
    public sealed class DimensionCandidate
    {
        public DimensionCandidateKind Kind { get; set; }
        public Wall? Wall { get; set; }
        public FamilyInstance? Instance { get; set; }

        /// <summary>Null for host elements; the placing link instance for linked ones.</summary>
        public RevitLinkInstance? Link { get; set; }
    }

    /// <summary>
    /// Candidates and their 2D segments, index-aligned: WallCrossingFinder returns indices into
    /// <see cref="Segments"/>, which are indices into <see cref="Items"/>.
    /// </summary>
    public sealed class DimensionCandidateSet
    {
        public IReadOnlyList<DimensionCandidate> Items { get; set; } = new List<DimensionCandidate>();
        public IReadOnlyList<WallCandidate> Segments { get; set; } = new List<WallCandidate>();
    }

    /// <summary>
    /// Turns the checked categories' elements — as visible in one target view, in the host model
    /// and in every loaded Revit link — into the flat, category-tagged candidate list the
    /// (unchanged) Core crossing finder consumes, and resolves each matched candidate's two
    /// dimension references.
    /// </summary>
    public static class DimensionCandidateCollector
    {
        /// <summary>Feet. Generous enough to admit a wall whose top lands on the cut plane.</summary>
        private const double CutPlaneTolerance = 1e-6;

        public static DimensionCandidateSet Collect(Document doc, View view, DimensionCategories categories)
        {
            var items = new List<DimensionCandidate>();
            var segments = new List<WallCandidate>();

            CollectHost(doc, view, categories, items, segments);
            CollectLinks(doc, view, categories, items, segments);

            return new DimensionCandidateSet { Items = items, Segments = segments };
        }

        /// <summary>
        /// Appends the candidate's two references (a wall's side faces, an opening's Left/Right)
        /// to the array. Returns false — leaving the array untouched — when either side can't be
        /// resolved, so one unreadable element doesn't cost the whole line its dimension.
        /// </summary>
        public static bool TryAppendReferences(DimensionCandidate candidate, ReferenceArray target)
        {
            try
            {
                Reference first;
                Reference second;

                if (candidate.Kind == DimensionCandidateKind.Wall)
                {
                    if (candidate.Wall == null) return false;

                    var exterior = HostObjectUtils.GetSideFaces(candidate.Wall, ShellLayerType.Exterior);
                    var interior = HostObjectUtils.GetSideFaces(candidate.Wall, ShellLayerType.Interior);
                    if (exterior.Count == 0 || interior.Count == 0) return false;

                    first = exterior[0];
                    second = interior[0];
                }
                else
                {
                    if (candidate.Instance == null) return false;

                    var left = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Left);
                    var right = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Right);
                    if (left.Count == 0 || right.Count == 0) return false;

                    first = left[0];
                    second = right[0];
                }

                if (candidate.Link != null)
                {
                    // A reference resolved inside a linked document is meaningless to the host
                    // view until it is re-expressed through the link instance that places it.
                    first = first.CreateLinkReference(candidate.Link);
                    second = second.CreateLinkReference(candidate.Link);
                    if (first == null || second == null) return false;
                }

                target.Append(first);
                target.Append(second);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CollectHost(
            Document doc,
            View view,
            DimensionCategories categories,
            List<DimensionCandidate> items,
            List<WallCandidate> segments)
        {
            // View-scoped collector: what the view actually shows, so no elevation filtering is
            // needed here (cutZ null) — unlike links, which have no view of their own.
            if (categories.HasFlag(DimensionCategories.Walls))
            {
                AddWalls(
                    new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>(),
                    null, Transform.Identity, null, items, segments);
            }

            if (categories.HasFlag(DimensionCategories.Doors))
                AddOpenings(HostOpenings(doc, view, BuiltInCategory.OST_Doors), null, Transform.Identity, null, items, segments);

            if (categories.HasFlag(DimensionCategories.Windows))
                AddOpenings(HostOpenings(doc, view, BuiltInCategory.OST_Windows), null, Transform.Identity, null, items, segments);
        }

        /// <summary>
        /// Walls, doors and windows inside every loaded Revit link visible in the view.
        ///
        /// A linked document can't be collected view-scoped (the view belongs to the host), so
        /// everything is collected and filtered by the view's cut plane instead — otherwise every
        /// storey of the link would pile into one plan, the crossing test being 2D. A non-plan
        /// view has no cut plane, so links are skipped there.
        /// </summary>
        private static void CollectLinks(
            Document doc,
            View view,
            DimensionCategories categories,
            List<DimensionCandidate> items,
            List<WallCandidate> segments)
        {
            var cutZ = TryGetCutPlaneElevation(doc, view);
            if (cutZ == null) return;

            foreach (var link in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(RevitLinkInstance))
                         .Cast<RevitLinkInstance>())
            {
                try
                {
                    var linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue; // unloaded link

                    var transform = link.GetTotalTransform();

                    if (categories.HasFlag(DimensionCategories.Walls))
                    {
                        AddWalls(
                            new FilteredElementCollector(linkDoc).OfClass(typeof(Wall)).Cast<Wall>(),
                            link, transform, cutZ, items, segments);
                    }

                    if (categories.HasFlag(DimensionCategories.Doors))
                        AddOpenings(LinkOpenings(linkDoc, BuiltInCategory.OST_Doors), link, transform, cutZ, items, segments);

                    if (categories.HasFlag(DimensionCategories.Windows))
                        AddOpenings(LinkOpenings(linkDoc, BuiltInCategory.OST_Windows), link, transform, cutZ, items, segments);
                }
                catch
                {
                    // One broken or half-loaded link never costs the run its host dimensions.
                }
            }
        }

        private static IEnumerable<FamilyInstance> HostOpenings(Document doc, View view, BuiltInCategory category) =>
            new FilteredElementCollector(doc, view.Id)
                .OfCategory(category)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();

        private static IEnumerable<FamilyInstance> LinkOpenings(Document linkDoc, BuiltInCategory category) =>
            new FilteredElementCollector(linkDoc)
                .OfCategory(category)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();

        private static void AddWalls(
            IEnumerable<Wall> walls,
            RevitLinkInstance? link,
            Transform transform,
            double? cutZ,
            List<DimensionCandidate> items,
            List<WallCandidate> segments)
        {
            // Straight walls only: Core's finder is a 2D segment intersection, and Revit can't
            // linear-dimension a curved face against a straight line anyway.
            foreach (var wall in walls)
            {
                if ((wall.Location as LocationCurve)?.Curve is not Line centerline) continue;
                if (!ReachesCutPlane(wall, transform, cutZ)) continue;

                items.Add(new DimensionCandidate
                {
                    Kind = DimensionCandidateKind.Wall,
                    Wall = wall,
                    Link = link,
                });
                segments.Add(new WallCandidate(
                    ToXyPoint(transform.OfPoint(centerline.GetEndPoint(0))),
                    ToXyPoint(transform.OfPoint(centerline.GetEndPoint(1)))));
            }
        }

        private static void AddOpenings(
            IEnumerable<FamilyInstance> instances,
            RevitLinkInstance? link,
            Transform transform,
            double? cutZ,
            List<DimensionCandidate> items,
            List<WallCandidate> segments)
        {
            foreach (var instance in instances)
            {
                if (!ReachesCutPlane(instance, transform, cutZ)) continue;
                if (!TryBuildOpeningSegment(instance, transform, out var segment)) continue;

                items.Add(new DimensionCandidate
                {
                    Kind = DimensionCandidateKind.Opening,
                    Instance = instance,
                    Link = link,
                });
                segments.Add(segment);
            }
        }

        /// <summary>
        /// True when the view would draw this element as cut. Always true for host elements
        /// (cutZ null) — the view-scoped collector has already decided what is visible.
        /// </summary>
        private static bool ReachesCutPlane(Element element, Transform transform, double? cutZ)
        {
            if (cutZ == null) return true;

            var box = element.get_BoundingBox(null);
            if (box == null) return false;

            var boxTransform = box.Transform ?? Transform.Identity;
            var min = transform.OfPoint(boxTransform.OfPoint(box.Min));
            var max = transform.OfPoint(boxTransform.OfPoint(box.Max));

            return ElevationRange.CrossesCutPlane(min.Z, max.Z, cutZ.Value, CutPlaneTolerance);
        }

        /// <summary>
        /// The plan view's cut plane in host coordinates, or null when the view has none (so
        /// links are skipped). The cut plane can be measured from a different level than the
        /// view's own — hence the level lookup rather than a bare offset.
        /// </summary>
        private static double? TryGetCutPlaneElevation(Document doc, View view)
        {
            if (view is not ViewPlan plan) return null;

            var viewLevel = plan.GenLevel;
            if (viewLevel == null) return null;

            try
            {
                var range = plan.GetViewRange();
                // Unlimited/LevelAbove/LevelBelow resolve to no element — fall back to the view's
                // own level, which is what those offsets are measured against in practice.
                var cutLevel = doc.GetElement(range.GetLevelId(PlanViewPlane.CutPlane)) as Level;
                return (cutLevel?.Elevation ?? viewLevel.Elevation)
                    + range.GetOffset(PlanViewPlane.CutPlane);
            }
            catch
            {
                return viewLevel.Elevation;
            }
        }

        private static bool TryBuildOpeningSegment(
            FamilyInstance instance, Transform transform, out WallCandidate segment)
        {
            segment = default!;

            if (instance.Location is not LocationPoint location) return false;
            if (instance.Host is not Wall host) return false;
            if ((host.Location as LocationCurve)?.Curve is not Line hostLine) return false;

            var width = TryGetWidth(instance);
            if (width == null) return false;

            // Host wall's direction, not the instance's own facing, to sidestep flip quirks —
            // and through the link transform, so a rotated link lands square in host coordinates.
            var direction = transform.OfVector(hostLine.Direction);
            segment = OpeningSegment.FromCenter(
                ToXyPoint(transform.OfPoint(location.Point)),
                new XyPoint(direction.X, direction.Y),
                width.Value);
            return true;
        }

        /// <summary>
        /// Openings carry their width under several names depending on the family's lineage
        /// (built-in door/window width, the generic family width, or a plain shared "Width"),
        /// on the instance or the type. First positive value wins; null means "not an opening we
        /// can size", and the candidate is dropped.
        /// </summary>
        private static double? TryGetWidth(FamilyInstance instance)
        {
            var symbol = instance.Symbol;
            var parameters = new[]
            {
                instance.get_Parameter(BuiltInParameter.DOOR_WIDTH),
                instance.get_Parameter(BuiltInParameter.WINDOW_WIDTH),
                symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH),
                symbol?.get_Parameter(BuiltInParameter.WINDOW_WIDTH),
                symbol?.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM),
                instance.LookupParameter("Width"),
                symbol?.LookupParameter("Width"),
            };

            foreach (var parameter in parameters)
            {
                if (parameter == null || !parameter.HasValue) continue;
                if (parameter.StorageType != StorageType.Double) continue;

                var value = parameter.AsDouble();
                if (value > 0) return value;
            }
            return null;
        }

        private static XyPoint ToXyPoint(XYZ point) => new(point.X, point.Y);
    }
}
