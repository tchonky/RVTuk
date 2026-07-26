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
    /// Turns the checked categories' elements — as visible in one target view — into the flat,
    /// category-tagged candidate list the (unchanged) Core crossing finder consumes, and resolves
    /// each matched candidate's two dimension references.
    /// </summary>
    public static class DimensionCandidateCollector
    {
        public static DimensionCandidateSet Collect(Document doc, View view, DimensionCategories categories)
        {
            var items = new List<DimensionCandidate>();
            var segments = new List<WallCandidate>();

            if (categories.HasFlag(DimensionCategories.Walls))
            {
                // Straight walls only: Core's finder is a 2D segment intersection, and Revit can't
                // linear-dimension a curved face against a straight line anyway.
                foreach (var wall in new FilteredElementCollector(doc, view.Id)
                             .OfClass(typeof(Wall))
                             .Cast<Wall>())
                {
                    if ((wall.Location as LocationCurve)?.Curve is not Line centerline) continue;

                    items.Add(new DimensionCandidate { Kind = DimensionCandidateKind.Wall, Wall = wall });
                    segments.Add(new WallCandidate(
                        ToXyPoint(centerline.GetEndPoint(0)),
                        ToXyPoint(centerline.GetEndPoint(1))));
                }
            }

            if (categories.HasFlag(DimensionCategories.Doors))
                CollectOpenings(doc, view, BuiltInCategory.OST_Doors, items, segments);

            if (categories.HasFlag(DimensionCategories.Windows))
                CollectOpenings(doc, view, BuiltInCategory.OST_Windows, items, segments);

            return new DimensionCandidateSet { Items = items, Segments = segments };
        }

        /// <summary>
        /// Appends the candidate's two references (a wall's side faces, an opening's Left/Right)
        /// to the array. Returns false — leaving the array untouched — when either side can't be
        /// resolved, so one unreadable element doesn't cost the whole line its dimension.
        /// </summary>
        public static bool TryAppendReferences(DimensionCandidate candidate, ReferenceArray target)
        {
            if (candidate.Kind == DimensionCandidateKind.Wall)
            {
                if (candidate.Wall == null) return false;

                var exterior = HostObjectUtils.GetSideFaces(candidate.Wall, ShellLayerType.Exterior);
                var interior = HostObjectUtils.GetSideFaces(candidate.Wall, ShellLayerType.Interior);
                if (exterior.Count == 0 || interior.Count == 0) return false;

                target.Append(exterior[0]);
                target.Append(interior[0]);
                return true;
            }

            if (candidate.Instance == null) return false;

            var left = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Left);
            var right = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Right);
            if (left.Count == 0 || right.Count == 0) return false;

            target.Append(left[0]);
            target.Append(right[0]);
            return true;
        }

        private static void CollectOpenings(
            Document doc,
            View view,
            BuiltInCategory category,
            List<DimensionCandidate> items,
            List<WallCandidate> segments)
        {
            foreach (var instance in new FilteredElementCollector(doc, view.Id)
                         .OfCategory(category)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                if (!TryBuildOpeningSegment(instance, out var segment)) continue;

                items.Add(new DimensionCandidate
                {
                    Kind = DimensionCandidateKind.Opening,
                    Instance = instance,
                });
                segments.Add(segment);
            }
        }

        private static bool TryBuildOpeningSegment(FamilyInstance instance, out WallCandidate segment)
        {
            segment = default!;

            if (instance.Location is not LocationPoint location) return false;
            if (instance.Host is not Wall host) return false;
            if ((host.Location as LocationCurve)?.Curve is not Line hostLine) return false;

            var width = TryGetWidth(instance);
            if (width == null) return false;

            var direction = hostLine.Direction;
            segment = OpeningSegment.FromCenter(
                ToXyPoint(location.Point),
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
