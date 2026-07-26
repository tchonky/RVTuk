using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Presentation order for a level's views: alphabetical, so the pane's tree is scannable.
    ///
    /// Deliberately separate from the rule that picks a level's *reference* view — that one takes
    /// the first view by ascending ElementId, so it stays deterministic no matter how the views
    /// are named or renamed. Sorting happens after that choice is made, never before.
    /// </summary>
    public static class ScopeViewOrder
    {
        public static IReadOnlyList<ScopeViewInfo> Sort(IEnumerable<ScopeViewInfo> views) =>
            views.OrderBy(v => v.ViewName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
