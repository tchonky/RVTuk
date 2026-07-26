using System;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>Undirected 2D angle helpers shared by the crossing and alignment rules.</summary>
    public static class Angle2D
    {
        /// <summary>0 = the two directions are parallel/collinear, 90 = perpendicular.</summary>
        public static double FromParallelDegrees(double angleA, double angleB)
        {
            var diffDegrees = Math.Abs(angleA - angleB) * 180.0 / Math.PI;
            diffDegrees %= 180.0;
            if (diffDegrees > 90.0) diffDegrees = 180.0 - diffDegrees;
            return diffDegrees;
        }
    }
}
