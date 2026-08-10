using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;

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

        /// <summary>
        /// How each candidate earns its place on a line — walls by being crossed, openings by
        /// lying alongside. Index-aligned with the other two.
        /// </summary>
        public IReadOnlyList<CandidateMatch> MatchModes { get; set; } = new List<CandidateMatch>();

        /// <summary>
        /// Every straight wall the view cuts, host and linked, in host coordinates — whether or
        /// not walls are being dimensioned. An opening is only dimensioned by a line with nothing
        /// parallel between them, and unticking Walls means "do not dimension walls", not
        /// "pretend walls are not there".
        /// </summary>
        public IReadOnlyList<WallCandidate> Occluders { get; set; } = new List<WallCandidate>();

        /// <summary>
        /// Every collected wall as an element, index-aligned with <see cref="Occluders"/>. A ref
        /// line must resolve a wall end whether or not Walls is ticked — the same reasoning that
        /// already makes every wall an occluder.
        /// </summary>
        public IReadOnlyList<DimensionCandidate> OccluderItems { get; set; } =
            new List<DimensionCandidate>();

        /// <summary>
        /// Elements the view draws but does not cut — walls below the cut plane, shown in
        /// projection. Reported in the run summary because their absence is a deliberate
        /// decision the user may need to see.
        /// </summary>
        public int ExcludedNotCut { get; set; }

        /// <summary>
        /// The view's cut plane, in host coordinates; null in a non-plan view. Kept so face
        /// resolution can pick the face that actually straddles it — a wall joined to the floor
        /// below and the roof above reports its side as several stacked faces, and the lowest
        /// one is no more "the" face than any other.
        /// </summary>
        public double? CutPlaneElevation { get; set; }
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
            // The cut plane governs BOTH host and linked elements. A plan view draws far more
            // than it cuts — everything down to its view depth appears in projection — so a
            // view-scoped collector alone hands back the storey below's walls, which then cross
            // the reference line just as convincingly as this storey's (the test is 2D).
            var cutZ = TryGetCutPlaneElevation(doc, view);
            var accumulated = new Accumulator();

            CollectHost(doc, view, categories, cutZ, accumulated);
            CollectLinks(doc, view, categories, cutZ, accumulated);

            return new DimensionCandidateSet
            {
                Items = accumulated.Items,
                Segments = accumulated.Segments,
                MatchModes = accumulated.MatchModes,
                Occluders = accumulated.Occluders,
                OccluderItems = accumulated.OccluderItems,
                ExcludedNotCut = accumulated.ExcludedNotCut,
                CutPlaneElevation = cutZ,
            };
        }

        /// <summary>Collects the index-aligned lists plus the exclusion tally as we go.</summary>
        private sealed class Accumulator
        {
            public readonly List<DimensionCandidate> Items = new List<DimensionCandidate>();
            public readonly List<WallCandidate> Segments = new List<WallCandidate>();
            public readonly List<CandidateMatch> MatchModes = new List<CandidateMatch>();
            public readonly List<WallCandidate> Occluders = new List<WallCandidate>();
            public readonly List<DimensionCandidate> OccluderItems = new List<DimensionCandidate>();
            public int ExcludedNotCut;

            public void Add(DimensionCandidate candidate, WallCandidate segment, CandidateMatch match)
            {
                Items.Add(candidate);
                Segments.Add(segment);
                MatchModes.Add(match);
            }
        }

        /// <summary>
        /// The candidate's two references (a wall's side faces, an opening's Left/Right), or null
        /// when either side can't be resolved — so one unreadable element doesn't cost the whole
        /// line its dimension. Returned rather than appended, because the runner interleaves them
        /// with ref line marks by position along the line before building the array.
        /// </summary>
        public static List<Reference>? TryResolveReferences(
            DimensionCandidateSet candidates,
            int index,
            XyPoint lineStart,
            XyPoint lineEnd)
        {
            try
            {
                var candidate = candidates.Items[index];
                var segment = candidates.Segments[index];

                Reference? first;
                Reference? second;

                if (candidate.Kind == DimensionCandidateKind.Wall)
                {
                    if (candidate.Wall == null) return null;
                    if (!ReferenceAlignment.CanDimension(
                            lineStart, lineEnd, segment, ReferenceNormal.AcrossSegment))
                        return null;

                    var cutZ = LocalCutPlane(candidates.CutPlaneElevation, candidate.Link);
                    first = PickSideFace(candidate.Wall, ShellLayerType.Exterior, cutZ);
                    second = PickSideFace(candidate.Wall, ShellLayerType.Interior, cutZ);
                    if (first == null || second == null) return null;
                }
                else
                {
                    if (candidate.Instance == null) return null;

                    // Jamb planes face along the host wall, so a line crossing that wall lies
                    // parallel to them. Revit rejects the whole dimension when handed one, which
                    // is why a line through a doorway used to produce nothing at all.
                    if (!ReferenceAlignment.CanDimension(
                            lineStart, lineEnd, segment, ReferenceNormal.AlongSegment))
                        return null;

                    // Left and Right ONLY — never CenterLeftRight, Front/Back or Strong/Weak.
                    // Exactly two references, both belonging to this instance, is what makes the
                    // segment a jamb-to-jamb measure of one opening; that is the shape Revit's
                    // "Show Opening Height" recognises, and a stray centre reference would split
                    // it into two meaningless halves. These are the reference planes' "Is
                    // Reference" property inside the family, not their names.
                    var left = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Left);
                    var right = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Right);
                    if (left.Count == 0 || right.Count == 0) return null;

                    // Expected to be one apiece; a family exposing several (nested families being
                    // the likely source) is served by the first, which at least stays stable.
                    first = left[0];
                    second = right[0];
                }

                if (candidate.Link != null)
                {
                    // A reference resolved inside a linked document is meaningless to the host
                    // view until it is re-expressed through the link instance that places it.
                    first = first.CreateLinkReference(candidate.Link);
                    second = second.CreateLinkReference(candidate.Link);
                    if (first == null || second == null) return null;
                }

                return new List<Reference> { first, second };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The wall's side face at the cut plane.
        ///
        /// A wall joined to the floor below and the roof above reports its side as several
        /// stacked faces, one per join region. Taking the first was arbitrary and frequently
        /// picked one wholly below or above the cut plane — a reference that reads, correctly,
        /// as a mark on the floor or the roof rather than on the wall. Falls back to the first
        /// face when none straddles the plane, so a wall is never lost to this.
        /// </summary>
        private static Reference? PickSideFace(Wall wall, ShellLayerType side, double? cutZ)
        {
            var faces = HostObjectUtils.GetSideFaces(wall, side);
            if (faces.Count == 0) return null;
            if (faces.Count == 1 || cutZ == null) return faces[0];

            foreach (var reference in faces)
            {
                if (SpansCutPlane(wall, reference, cutZ.Value)) return reference;
            }
            return faces[0];
        }

        private static bool SpansCutPlane(Wall wall, Reference reference, double cutZ)
        {
            try
            {
                if (wall.GetGeometryObjectFromReference(reference) is not Face face) return false;

                var uv = face.GetBoundingBox();
                var corners = new[]
                {
                    face.Evaluate(uv.Min),
                    face.Evaluate(new UV(uv.Min.U, uv.Max.V)),
                    face.Evaluate(new UV(uv.Max.U, uv.Min.V)),
                    face.Evaluate(uv.Max),
                };

                var minZ = corners.Min(p => p.Z);
                var maxZ = corners.Max(p => p.Z);
                return ElevationRange.CrossesCutPlane(minZ, maxZ, cutZ, CutPlaneTolerance);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The cut plane expressed in the element's own document. Links are placed with a
        /// rotation about Z at most, so the elevation shifts by the transform's origin alone.
        /// </summary>
        private static double? LocalCutPlane(double? cutZ, RevitLinkInstance? link)
        {
            if (cutZ == null) return null;
            if (link == null) return cutZ;

            try
            {
                return cutZ.Value - link.GetTotalTransform().Origin.Z;
            }
            catch
            {
                return cutZ;
            }
        }

        private static void CollectHost(
            Document doc,
            View view,
            DimensionCategories categories,
            double? cutZ,
            Accumulator accumulated)
        {
            // The view-scoped collector answers "what does this view draw", which includes
            // everything in projection below the cut plane. cutZ narrows that to what it cuts.
            AddWalls(
                new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>(),
                null, Transform.Identity, cutZ,
                categories.HasFlag(DimensionCategories.Walls), accumulated);

            if (categories.HasFlag(DimensionCategories.Doors))
                AddOpenings(HostOpenings(doc, view, BuiltInCategory.OST_Doors), null, Transform.Identity, cutZ, accumulated);

            if (categories.HasFlag(DimensionCategories.Windows))
                AddOpenings(HostOpenings(doc, view, BuiltInCategory.OST_Windows), null, Transform.Identity, cutZ, accumulated);
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
            double? cutZ,
            Accumulator accumulated)
        {
            // Without a cut plane there is no way to tell which storey of the link belongs here,
            // so links are skipped entirely rather than guessed at.
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

                    AddWalls(
                        new FilteredElementCollector(linkDoc).OfClass(typeof(Wall)).Cast<Wall>(),
                        link, transform, cutZ,
                        categories.HasFlag(DimensionCategories.Walls), accumulated);

                    if (categories.HasFlag(DimensionCategories.Doors))
                        AddOpenings(LinkOpenings(linkDoc, BuiltInCategory.OST_Doors), link, transform, cutZ, accumulated);

                    if (categories.HasFlag(DimensionCategories.Windows))
                        AddOpenings(LinkOpenings(linkDoc, BuiltInCategory.OST_Windows), link, transform, cutZ, accumulated);
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
            bool dimensionThem,
            Accumulator accumulated)
        {
            // Straight walls only: Core's finder is a 2D segment intersection, and Revit can't
            // linear-dimension a curved face against a straight line anyway.
            foreach (var wall in walls)
            {
                if ((wall.Location as LocationCurve)?.Curve is not Line centerline) continue;
                if (!ReachesCutPlane(wall, transform, cutZ))
                {
                    // Only counted when walls were asked for: the tally reports what the user
                    // wanted and did not get, and a wall collected purely to occlude was never
                    // wanted. Counting them would bury the real exclusions under hundreds.
                    if (dimensionThem) accumulated.ExcludedNotCut++;
                    continue;
                }

                var segment = new WallCandidate(
                    ToXyPoint(transform.OfPoint(centerline.GetEndPoint(0))),
                    ToXyPoint(transform.OfPoint(centerline.GetEndPoint(1))));

                // One instance in both lists: every wall stands in the way of the openings behind
                // it, dimensioned or not, and a ref line may point at any of their ends.
                var item = new DimensionCandidate
                {
                    Kind = DimensionCandidateKind.Wall,
                    Wall = wall,
                    Link = link,
                };

                accumulated.Occluders.Add(segment);
                accumulated.OccluderItems.Add(item);
                if (!dimensionThem) continue;

                accumulated.Add(item, segment, CandidateMatch.Crossing);
            }
        }

        private static void AddOpenings(
            IEnumerable<FamilyInstance> instances,
            RevitLinkInstance? link,
            Transform transform,
            double? cutZ,
            Accumulator accumulated)
        {
            foreach (var instance in instances)
            {
                if (!ReachesCutPlane(instance, transform, cutZ))
                {
                    accumulated.ExcludedNotCut++;
                    continue;
                }
                if (!TryBuildOpeningSegment(instance, transform, out var segment)) continue;

                accumulated.Add(
                    new DimensionCandidate
                    {
                        Kind = DimensionCandidateKind.Opening,
                        Instance = instance,
                        Link = link,
                    },
                    segment,
                    CandidateMatch.Alongside);
            }
        }

        /// <summary>
        /// True when the view cuts this element — not merely draws it. A plan view draws
        /// everything down to its view depth in projection, and those elements cross a reference
        /// line exactly as convincingly as cut ones, the crossing test being 2D.
        ///
        /// Permissive on the unknown: an element with no bounding box is kept, because a wall
        /// wrongly dropped is a silent missing dimension, which is the harder failure to spot.
        /// </summary>
        private static bool ReachesCutPlane(Element element, Transform transform, double? cutZ)
        {
            if (cutZ == null) return true; // non-plan view: no cut plane to judge against

            var box = element.get_BoundingBox(null);
            if (box == null) return true;

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
