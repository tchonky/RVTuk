using System;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Whether an element's vertical extent reaches the plan view's cut plane — i.e. whether the
    /// view would draw it as cut.
    ///
    /// Only linked models need this. Host elements come from a view-scoped collector, which
    /// already returns just what the view shows; a linked document has no view of its own to
    /// scope against, so its elements are collected whole-document and filtered here. Without it
    /// every storey of the link would crowd into one plan, since the crossing test drops Z.
    /// </summary>
    public static class ElevationRange
    {
        public static bool CrossesCutPlane(double minZ, double maxZ, double cutZ, double tolerance)
        {
            // A transformed bounding box can hand back its corners either way round.
            var bottom = Math.Min(minZ, maxZ);
            var top = Math.Max(minZ, maxZ);

            return cutZ >= bottom - tolerance && cutZ <= top + tolerance;
        }
    }
}
