using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// Outer loop first. <c>Floor.Create</c> and <c>Toposolid.Create</c> may sort their loops
    /// themselves; ordering them here costs nothing and removes the question. Shared because Room
    /// Floor and the Topo Tools converter both build slabs from loops.
    /// </summary>
    public static class LoopOrder
    {
        public static List<CurveLoop> OuterFirst(IEnumerable<CurveLoop> loops) =>
            loops.OrderByDescending(PlanExtentArea).ToList();

        /// <summary>Area of the loop's plan bounding box: enough to tell the outer loop from the
        /// holes, which it always encloses.</summary>
        private static double PlanExtentArea(CurveLoop loop)
        {
            var points = loop.SelectMany(c => c.Tessellate()).ToList();
            return (points.Max(p => p.X) - points.Min(p => p.X))
                 * (points.Max(p => p.Y) - points.Min(p => p.Y));
        }
    }
}
