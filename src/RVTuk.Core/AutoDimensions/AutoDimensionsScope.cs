using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>What the pane persisted on its last successful run; null when it never ran.</summary>
    public record ScopeSelection(
        int CategoryMask,
        IReadOnlyList<long> CheckedViewIds,
        long DimensionTypeId);

    /// <summary>
    /// One of the project's dimension types, as the pane's dropdown sees it. Which type a run
    /// uses is the user's choice because it carries their office's appearance — and because
    /// Revit's "Show Opening Height", which prints a door's height under its width, lives on the
    /// type rather than on the dimension.
    /// </summary>
    public record DimensionTypeInfo(long Id, string Name);

    /// <summary>Everything one discovery pass tells the pane about the open project.</summary>
    public record AutoDimensionsScope(
        IReadOnlyList<LevelScope> Levels,
        ScopeSelection? Selection,
        IReadOnlyList<DimensionTypeInfo> DimensionTypes);
}
