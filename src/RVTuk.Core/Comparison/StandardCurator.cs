using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    /// <summary>Curates the editable Standard: routes an "accept" to the right category merger,
    /// records provenance, and bumps the revision. Never touches a Revit model.</summary>
    public class StandardCurator
    {
        private readonly Dictionary<string, ICategoryMerger> _mergers =
            new Dictionary<string, ICategoryMerger>();

        public StandardCurator(IEnumerable<ICategoryMerger> mergers)
        {
            foreach (var m in mergers)
                _mergers[m.CategoryId] = m;
        }

        public MergeResult Accept(
            StandardSnapshot std, SnapshotMeta sourceMeta, CategorySnapshot source,
            string itemKey, DependencyClosure deps, bool replace = false)
        {
            if (!_mergers.TryGetValue(source.CategoryId, out var merger))
                return new MergeResult { Applied = false, Conflict = "no merger" };

            var result = merger.AcceptIntoStandard(std, source, itemKey, deps, replace);
            if (result.Applied)
            {
                RecordProvenance(std, itemKey, sourceMeta);
                std.Meta.Revision++;
            }
            return result;
        }

        private static void RecordProvenance(StandardSnapshot std, string itemKey, SnapshotMeta sourceMeta)
        {
            std.Provenance.RemoveAll(p => p.ItemKey == itemKey);
            std.Provenance.Add(new ItemProvenance
            {
                ItemKey = itemKey,
                SourceName = sourceMeta.SourceName,
                CapturedUtc = sourceMeta.CapturedUtc,
            });
        }
    }
}
