using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>Everything one discovery pass tells the pane.</summary>
    public sealed record TopoScope(
        bool IsProjectSetUp,
        string ViewName,
        IReadOnlyList<TopoLineInfo> Lines,
        int ToposolidCount,
        string Message)
    {
        public static TopoScope NotSetUp(string viewName) =>
            new TopoScope(false, viewName, Array.Empty<TopoLineInfo>(), 0,
                "This project has no Topo_Line style yet.");

        public static TopoScope Unavailable(string viewName, string message) =>
            new TopoScope(true, viewName, Array.Empty<TopoLineInfo>(), 0, message);
    }
}
