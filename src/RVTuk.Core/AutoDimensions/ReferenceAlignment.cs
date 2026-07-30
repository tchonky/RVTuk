using System;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>Which way a candidate's dimension references face, relative to its own run.</summary>
    public enum ReferenceNormal
    {
        /// <summary>Normals across the element — a wall's two side faces (thickness).</summary>
        AcrossSegment,

        /// <summary>Normals along the element — an opening's Left/Right jambs (width).</summary>
        AlongSegment,
    }

    /// <summary>
    /// Whether a candidate's reference planes can be measured by a given dimension line.
    ///
    /// A dimension measures along its own line, so its references must not be planes parallel
    /// to that line — there is no distance between them to report, and Revit rejects the whole
    /// dimension rather than the offending reference. That is a hard geometric limit, and it
    /// falls differently on the two orientations:
    ///
    ///   • wall side faces  — normals across the wall, so the line must CROSS the wall
    ///   • opening jambs    — normals along the wall, so the line must RUN ALONG the wall
    ///
    /// Which means a line crossing a wall square on can dimension the wall but never the doors
    /// in it: their jambs lie parallel to the line.
    /// </summary>
    public static class ReferenceAlignment
    {
        /// <summary>
        /// Matches WallCrossingFinder's parallel tolerance, so the crossing rule and this one
        /// meet exactly: a wall it admits is one whose side faces are usable.
        /// </summary>
        public const double DefaultToleranceDegrees = 5.0;

        private const double MinimumSegmentLength = 1e-12;

        public static bool CanDimension(
            XyPoint lineStart,
            XyPoint lineEnd,
            WallCandidate segment,
            ReferenceNormal normal,
            double toleranceDegrees = DefaultToleranceDegrees)
        {
            var segmentX = segment.End.X - segment.Start.X;
            var segmentY = segment.End.Y - segment.Start.Y;
            if (Math.Abs(segmentX) < MinimumSegmentLength && Math.Abs(segmentY) < MinimumSegmentLength)
                return false;

            var lineAngle = Math.Atan2(lineEnd.Y - lineStart.Y, lineEnd.X - lineStart.X);
            var segmentAngle = Math.Atan2(segmentY, segmentX);

            // The normal is along the segment for jambs, and a quarter turn off it for side faces.
            var normalAngle = normal == ReferenceNormal.AlongSegment
                ? segmentAngle
                : segmentAngle + Math.PI / 2.0;

            // Usable while the normal is not perpendicular to the line — i.e. while the planes
            // are not parallel to it.
            return Angle2D.FromParallelDegrees(lineAngle, normalAngle) < 90.0 - toleranceDegrees;
        }
    }
}
