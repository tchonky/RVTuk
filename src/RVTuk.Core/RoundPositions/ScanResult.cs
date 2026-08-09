using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RoundPositions
{
    /// <summary>One element the scan found off the grid. LevelElevationCm is null for things that
    /// belong to no level, such as grids and base points.</summary>
    public readonly record struct DirtyElement(
        long ElementId,
        FixTier Tier,
        string Category,
        string Level,
        double? LevelElevationCm,
        string Name);

    public record LevelNode(string Level, int Count, IReadOnlyList<DirtyElement> Elements);

    public record CategoryNode(FixTier Tier, string Category, int Count, IReadOnlyList<LevelNode> Levels);

    public record ScanResult(
        int Examined,
        int AlreadyClean,
        IReadOnlyList<DirtyElement> Dirty,
        IReadOnlyList<SkippedElement> Skipped)
    {
        /// <summary>Shown for grids, base points and anything else with no owning level.</summary>
        public const string NoLevelLabel = "(no level)";

        public IReadOnlyList<CategoryNode> GroupedByCategoryAndLevel() =>
            Dirty
                .GroupBy(d => (d.Tier, d.Category))
                .OrderBy(g => g.Key.Tier)
                .ThenBy(g => g.Key.Category, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategoryNode(
                    g.Key.Tier,
                    g.Key.Category,
                    g.Count(),
                    g.GroupBy(d => d.Level)
                        // No-level first, then bottom to top: an architect reads a building upwards,
                        // and level names sort alphabetically in the wrong order past Level 9.
                        .OrderBy(l => l.First().LevelElevationCm ?? double.NegativeInfinity)
                        .Select(l => new LevelNode(
                            string.IsNullOrEmpty(l.Key) ? NoLevelLabel : l.Key,
                            l.Count(),
                            l.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList()))
                        .ToList()))
                .ToList();

        /// <summary>Largest group first — on an inherited model the biggest pile is the story.</summary>
        public IReadOnlyList<SkipGroup> GroupedSkips() =>
            Skipped
                .GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new SkipGroup(g.Key, SkipReasonText.Describe(g.Key), g.ToList()))
                .ToList();
    }

    public class ScanResultBuilder
    {
        private readonly List<DirtyElement> _dirty = new List<DirtyElement>();
        private readonly List<SkippedElement> _skipped = new List<SkippedElement>();

        public int Examined { get; private set; }

        public int AlreadyClean { get; private set; }

        public void CountExamined() => Examined++;

        public void RecordAlreadyClean() => AlreadyClean++;

        public void RecordDirty(DirtyElement element) => _dirty.Add(element);

        public void RecordSkipped(long elementId, string category, SkipReason reason) =>
            _skipped.Add(new SkippedElement(elementId, category, reason));

        public ScanResult Build() =>
            new ScanResult(Examined, AlreadyClean, _dirty.ToList(), _skipped.ToList());
    }
}
