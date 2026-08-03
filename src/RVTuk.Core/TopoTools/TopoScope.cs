using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>Everything one discovery pass tells the pane.</summary>
    public sealed record TopoScope(
        string ViewName,
        IReadOnlyList<TopoLineInfo> Lines,
        int ToposolidCount,
        string Message)
    {
        public static TopoScope Unavailable(string viewName, string message) =>
            new TopoScope(viewName, Array.Empty<TopoLineInfo>(), 0, message);
    }
}
