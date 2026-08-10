using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// One level of the project: every plan view that belongs to it, plus which of those views
    /// (if any) owns the _DP-Dim reference lines the others copy their dimensions from.
    /// A level with no reference view has nothing to fan out and is shown inert in the pane.
    /// </summary>
    public record LevelScope(
        long LevelId,
        string LevelName,
        long? ReferenceViewId,
        IReadOnlyList<ScopeViewInfo> Views)
    {
        public bool HasReferenceView => ReferenceViewId.HasValue;
    }
}
