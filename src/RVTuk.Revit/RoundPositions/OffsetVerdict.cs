using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    public enum OffsetOutcome
    {
        NeedsMove,
        AlreadyOnGrid,
        CannotFix,
    }

    /// <summary>
    /// What one element needs, as read from the model. The scan and the fix both go through this,
    /// so a fix can never act on a different answer than the one the tree showed.
    /// </summary>
    public readonly struct OffsetVerdict
    {
        private OffsetVerdict(OffsetOutcome outcome, XYZ? translation, SkipReason reason)
        {
            Outcome = outcome;
            Translation = translation;
            Reason = reason;
        }

        public OffsetOutcome Outcome { get; }

        /// <summary>Only meaningful when <see cref="Outcome"/> is <see cref="OffsetOutcome.NeedsMove"/>.</summary>
        public XYZ? Translation { get; }

        /// <summary>Only meaningful when <see cref="Outcome"/> is <see cref="OffsetOutcome.CannotFix"/>.</summary>
        public SkipReason Reason { get; }

        public static OffsetVerdict Move(XYZ translation) =>
            new OffsetVerdict(OffsetOutcome.NeedsMove, translation, default);

        public static OffsetVerdict Clean() =>
            new OffsetVerdict(OffsetOutcome.AlreadyOnGrid, null, default);

        public static OffsetVerdict Cannot(SkipReason reason) =>
            new OffsetVerdict(OffsetOutcome.CannotFix, null, reason);
    }
}
