using System;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Builds the 2D candidate segment for an opening (door/window). A wall contributes its own
    /// centreline; an opening has none, so it contributes a segment as wide as the opening,
    /// centred on its location point and running along its host wall's direction — deliberately
    /// the host's direction, not the instance's own facing, to sidestep flip/orientation quirks.
    /// </summary>
    public static class OpeningSegment
    {
        private const double MinimumDirectionLength = 1e-12;

        public static WallCandidate FromCenter(XyPoint center, XyPoint direction, double width)
        {
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < MinimumDirectionLength) return new WallCandidate(center, center);

            var halfX = direction.X / length * width / 2.0;
            var halfY = direction.Y / length * width / 2.0;

            return new WallCandidate(
                new XyPoint(center.X - halfX, center.Y - halfY),
                new XyPoint(center.X + halfX, center.Y + halfY));
        }
    }
}
