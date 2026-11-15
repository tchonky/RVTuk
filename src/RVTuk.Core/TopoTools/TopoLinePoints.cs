using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>The points a topo line contributes to a target: those inside it, and those on its edge.</summary>
    public sealed record TopoLinePoints(IReadOnlyList<XyPoint> Interior, IReadOnlyList<XyPoint> Edge)
    {
        public bool IsEmpty => Interior.Count == 0 && Edge.Count == 0;
    }
}
