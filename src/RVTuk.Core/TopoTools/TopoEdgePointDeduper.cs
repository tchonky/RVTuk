using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// One run's edge points with every plan position kept once. Chained lines meeting on the
    /// outline each produce the same edge point; placing it twice would ledger two records for one
    /// vertex, and the second would read as "moved by hand" on every later run. Positions match by
    /// XY within <see cref="ShapeReplayPlanner.ToleranceFeet"/>, as the ledger and replay do; the
    /// first line in the given order keeps the point, and a later duplicate (in the same line or
    /// another) is dropped.
    /// </summary>
    public static class TopoEdgePointDeduper
    {
        public static Dictionary<long, List<XyzPoint>> Dedupe(
            IEnumerable<KeyValuePair<long, IReadOnlyList<XyzPoint>>> edgePointsByLine)
        {
            var kept = new List<XyzPoint>();
            var result = new Dictionary<long, List<XyzPoint>>();
            foreach (var entry in edgePointsByLine)
            {
                var points = new List<XyzPoint>();
                foreach (var point in entry.Value)
                {
                    if (ShapeReplayPlanner.IndexOfXy(kept, point) >= 0) continue;
                    kept.Add(point);
                    points.Add(point);
                }
                result[entry.Key] = points;
            }
            return result;
        }
    }
}
