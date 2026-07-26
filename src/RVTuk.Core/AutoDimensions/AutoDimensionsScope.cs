using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>What the pane persisted on its last successful run; null when it never ran.</summary>
    public record ScopeSelection(int CategoryMask, IReadOnlyList<long> CheckedViewIds);

    /// <summary>Everything one discovery pass tells the pane about the open project.</summary>
    public record AutoDimensionsScope(IReadOnlyList<LevelScope> Levels, ScopeSelection? Selection);
}
