using System;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Where a vertical line meets a plane. A floor sloped by a slope arrow has no shape vertices
    /// to copy, yet its top is not flat. Its corners' heights are read off its top face this way
    /// and handed to the new element as ordinary corner offsets.
    /// </summary>
    public static class PlaneHeight
    {
        /// <param name="normal">Any length; only its direction matters.</param>
        /// <returns>The plane's Z above (x, y), or null for a vertical plane, which has none.</returns>
        public static double? At(XyzPoint origin, XyzPoint normal, double x, double y)
        {
            if (Math.Abs(normal.Z) < 1e-9) return null;
            return origin.Z - (normal.X * (x - origin.X) + normal.Y * (y - origin.Y)) / normal.Z;
        }
    }
}
