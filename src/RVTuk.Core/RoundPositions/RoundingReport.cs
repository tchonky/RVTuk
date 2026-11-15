using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RoundPositions
{
    public readonly record struct SkippedElement(long ElementId, string Category, SkipReason Reason);

    /// <summary>A dimension unlocked or an alignment constraint deleted to let a move through.</summary>
    public readonly record struct ReleasedConstraint(long ElementId, string Category, string Description);

    public record SkipGroup(SkipReason Reason, string Description, IReadOnlyList<SkippedElement> Elements);

    public record RoundingReport(
        int Examined,
        int Moved,
        int AlreadyClean,
        IReadOnlyList<SkippedElement> Skipped,
        IReadOnlyList<ReleasedConstraint> ReleasedConstraints)
    {
        /// <summary>How long the fix took. The pane shows it because the first version of this
        /// tool was abandoned mid-run for being slow with nothing on screen to say so.</summary>
        public long ElapsedMilliseconds { get; init; }

        /// <summary>Largest group first — on an inherited model the biggest pile is the story.</summary>
        public IReadOnlyList<SkipGroup> GroupedByReason() =>
            Skipped
                .GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new SkipGroup(g.Key, SkipReasonText.Describe(g.Key), g.ToList()))
                .ToList();
    }

    public class RoundingReportBuilder
    {
        private readonly List<SkippedElement> _skipped = new List<SkippedElement>();
        private readonly List<ReleasedConstraint> _released = new List<ReleasedConstraint>();

        public int Examined { get; private set; }
        public int Moved { get; private set; }
        public int AlreadyClean { get; private set; }

        public void CountExamined() => Examined++;

        public void RecordMoved() => Moved++;

        public void RecordAlreadyClean() => AlreadyClean++;

        public void RecordSkipped(long elementId, string category, SkipReason reason) =>
            _skipped.Add(new SkippedElement(elementId, category, reason));

        public void RecordReleasedConstraint(long elementId, string category, string description) =>
            _released.Add(new ReleasedConstraint(elementId, category, description));

        public RoundingReport Build() =>
            new RoundingReport(Examined, Moved, AlreadyClean, _skipped.ToList(), _released.ToList());
    }
}
