using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>What the pane persisted on its last successful run; null when it never ran.</summary>
    public record ScopeSelection(
        int CategoryMask,
        IReadOnlyList<long> CheckedViewIds,
        int OpeningReachMillimetres);

    /// <summary>Everything one discovery pass tells the pane about the open project.</summary>
    public record AutoDimensionsScope(IReadOnlyList<LevelScope> Levels, ScopeSelection? Selection);

    /// <summary>What a project with nothing persisted yet starts with.</summary>
    public static class ScopeDefaults
    {
        /// <summary>
        /// How far from a reference line an opening's wall may sit and still be dimensioned by
        /// it. Drafting convention, not geometry — exterior dimension strings often sit further
        /// out than this, which is exactly why the pane lets the user change it.
        /// </summary>
        public const int OpeningReachMillimetres = 1000;
    }
}
