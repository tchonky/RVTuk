using System.Collections.Generic;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    /// <summary>Category-agnostic orchestration: for each category present on both sides,
    /// dispatch to the registered comparer and aggregate into a ComparisonResult.</summary>
    public class ComparisonEngine
    {
        private readonly CategoryRegistry _registry;

        public ComparisonEngine(CategoryRegistry registry)
        {
            _registry = registry;
        }

        public ComparisonResult Compare(
            SnapshotMeta metaA, SnapshotMeta metaB,
            IEnumerable<CategorySnapshot> snapsA, IEnumerable<CategorySnapshot> snapsB)
        {
            var byCatA = Index(snapsA);
            var byCatB = Index(snapsB);

            var result = new ComparisonResult { SideA = metaA, SideB = metaB };

            // Union of category ids present on either side.
            var categoryIds = new HashSet<string>(byCatA.Keys);
            categoryIds.UnionWith(byCatB.Keys);

            foreach (var categoryId in categoryIds)
            {
                if (!_registry.TryGet(categoryId, out var comparer))
                    continue; // category not supported in this build — skip silently

                byCatA.TryGetValue(categoryId, out var a);
                byCatB.TryGetValue(categoryId, out var b);
                if (a == null || b == null)
                    continue; // need both sides to diff

                result.Categories.Add(comparer.Compare(a, b));
            }

            return result;
        }

        private static Dictionary<string, CategorySnapshot> Index(IEnumerable<CategorySnapshot> snaps)
        {
            var map = new Dictionary<string, CategorySnapshot>();
            foreach (var s in snaps)
                map[s.CategoryId] = s;
            return map;
        }
    }
}
