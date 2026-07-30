using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// "The toposolid below (or above)", decided per point: the one whose footprint encloses it
    /// and whose vertical extent is nearest it. Ties go to the lower element id, so a re-run never
    /// shuffles a point between two overlapping toposolids.
    /// </summary>
    public static class ToposolidRouter
    {
        public static ToposolidTarget? Route(
            IReadOnlyList<ToposolidTarget> targets, XyPoint point, double z)
        {
            ToposolidTarget? best = null;
            double bestDistance = 0;

            foreach (var target in targets)
            {
                if (!PolygonContainment.Contains(target.Loops, point)) continue;

                double distance =
                    z < target.MinZ ? target.MinZ - z :
                    z > target.MaxZ ? z - target.MaxZ :
                    0;

                if (best == null ||
                    distance < bestDistance ||
                    (distance == bestDistance && target.Id < best.Id))
                {
                    best = target;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
