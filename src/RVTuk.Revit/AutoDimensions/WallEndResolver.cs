using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Turns a _DP-Dim Ref line's target end into a dimension reference on a wall face the string
    /// can actually measure.
    ///
    /// Revit's one requirement is that a reference's plane not lie parallel to the dimension line —
    /// the normal must run ALONG the string. Which of a wall's faces satisfy that depends on how
    /// the wall sits relative to the string, and the two cases are exact opposites:
    ///
    ///   • wall PARALLEL to the string — its END faces face along the wall, hence along the
    ///     string. Its side faces face across it, which is precisely why a parallel wall is
    ///     invisible to the automatic pass and why this tool exists.
    ///   • wall PERPENDICULAR to the string — its SIDE faces face across the wall, hence along
    ///     the string. Now the end faces are the unusable ones.
    ///
    /// So the test is on each face's own normal against the STRING, never against the wall.
    /// Testing against the wall found end faces and nothing else: a perpendicular wall was
    /// skipped in silence and the search fell through to whatever else lay within a foot, so the
    /// mark landed on a neighbouring wall's end rather than the face pointed at.
    ///
    /// Resolved view-independently so one answer serves every fanned-out view.
    /// </summary>
    public static class WallEndResolver
    {
        /// <summary>One candidate wall end: which collected wall, where, and how far off.</summary>
        private readonly record struct WallEnd(int Index, XyPoint Point, double Distance);

        /// <summary>
        /// Feet. Generous on purpose: the user may snap to the wall's face corner rather than its
        /// location-curve end, and the two differ by half the wall's thickness — 1 ft covers
        /// walls to 600 mm.
        /// </summary>
        public const double WallEndTolerance = 1.0;

        /// <summary>Degrees. The same parallel tolerance the crossing and alignment rules use.</summary>
        private const double NormalToleranceDegrees = WallCrossingFinder.ParallelToleranceDegrees;

        /// <summary>Feet. A face normal with more vertical than this is a top or bottom.</summary>
        private const double VerticalTolerance = 0.001;

        /// <summary>
        /// Feet. How far from the point pointed at, measured along the string, a face may sit.
        /// Cannot be zero: a join extends a wall's geometry past its location-curve end by the
        /// thickness of the wall it meets, and a face corner sits half a thickness off the
        /// location curve. It is what keeps a parallel wall's far end, and the jambs of any door
        /// in it, from standing in for the end that was pointed at.
        /// </summary>
        private const double FaceOffsetTolerance = 1.0;

        private const double MinimumLength = 1e-12;

        /// <summary>
        /// Null when there is no wall end within tolerance of the target point, or when none of
        /// the walls that own those ends offers a face whose normal runs along the string — a
        /// wall joined into another at that end has its end face clipped or consumed by Revit and
        /// may have no planar face there at all, and a wall lying at a diagonal to the string has
        /// no usable face in either family. Tries every candidate end, nearest first, rather than
        /// betting everything on the single nearest one.
        /// </summary>
        public static Reference? TryResolve(
            DimensionCandidateSet candidates,
            XyPoint targetEnd,
            XyPoint stringStart,
            XyPoint stringEnd)
        {
            var stringX = stringEnd.X - stringStart.X;
            var stringY = stringEnd.Y - stringStart.Y;
            var stringLength = Math.Sqrt(stringX * stringX + stringY * stringY);
            if (stringLength < MinimumLength) return null;

            var stringDirection = new XyPoint(stringX / stringLength, stringY / stringLength);

            // No cheap pre-gate on the wall's own orientation. The old one tested the location
            // curve with ReferenceNormal.AlongSegment, which is the parallel case only and threw
            // away every perpendicular wall before its geometry was ever read. The face normal
            // below is the real requirement, so it is the only test.
            foreach (var wallEnd in WallEndsNear(candidates, targetEnd))
            {
                var candidate = candidates.OccluderItems[wallEnd.Index];
                if (candidate.Wall == null) continue;

                var reference = TryFaceAlongString(
                    candidate.Wall, wallEnd.Point, stringDirection, candidate.Link);
                if (reference != null) return reference;
            }

            return null;
        }

        /// <summary>
        /// Every collected wall end within <see cref="WallEndTolerance"/> of the target point,
        /// nearest first.
        ///
        /// All of them, not just the nearest: the end a ref line points at is usually a corner,
        /// where joined walls' location curves meet at the same point, so the nearest end is as
        /// likely to belong to the neighbour as to the wall meant. Returning one candidate made
        /// that a coin flip.
        /// </summary>
        private static IReadOnlyList<WallEnd> WallEndsNear(
            DimensionCandidateSet candidates, XyPoint target)
        {
            var found = new List<WallEnd>();

            // OccluderItems is index-aligned with Occluders; clamp rather than trust it, so a
            // mismatch cannot throw deep inside a run.
            var count = Math.Min(candidates.Occluders.Count, candidates.OccluderItems.Count);

            for (int i = 0; i < count; i++)
            {
                var segment = candidates.Occluders[i];
                foreach (var end in new[] { segment.Start, segment.End })
                {
                    var dx = end.X - target.X;
                    var dy = end.Y - target.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > WallEndTolerance) continue;

                    found.Add(new WallEnd(i, end, distance));
                }
            }

            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return found;
        }

        /// <summary>
        /// The wall's vertical planar face whose normal runs along the string and whose plane sits
        /// nearest the point pointed at, measured along that same string.
        ///
        /// ComputeReferences is required or the face's Reference comes back null, and the options
        /// carry no View on purpose — a view-specific resolution would have to be redone for every
        /// fanned-out view, and could differ between them.
        /// </summary>
        private static Reference? TryFaceAlongString(
            Wall wall, XyPoint target, XyPoint stringDirection, RevitLinkInstance? link)
        {
            try
            {
                var localTarget = ToLocalPoint(target, link);
                var localDirection = ToLocalDirection(stringDirection, link);
                var stringAngle = Math.Atan2(localDirection.Y, localDirection.X);

                var options = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Medium,
                };

                Reference? best = null;
                var bestDistance = double.MaxValue;

                foreach (var geometryObject in wall.get_Geometry(options))
                {
                    if (geometryObject is not Solid solid) continue;

                    foreach (Face face in solid.Faces)
                    {
                        if (face is not PlanarFace planar) continue;
                        if (planar.Reference == null) continue;

                        // Vertical check FIRST: a top or bottom face has no horizontal normal, and
                        // Atan2(0, 0) is 0, which would read as perfectly aligned with anything.
                        var normal = planar.FaceNormal;
                        if (Math.Abs(normal.Z) > VerticalTolerance) continue;

                        // Against the STRING, not the wall — see the class summary. This one test
                        // picks end faces on a parallel wall and side faces on a perpendicular
                        // one, with neither being a special case, and rejects a wall lying at a
                        // diagonal, which has no face a string can honestly measure.
                        var normalAngle = Math.Atan2(normal.Y, normal.X);
                        if (Angle2D.FromParallelDegrees(stringAngle, normalAngle) > NormalToleranceDegrees)
                            continue;

                        // Distance along the string — the axis the dimension actually measures on.
                        // It is what tells a perpendicular wall's two side faces apart (they lie
                        // one wall thickness apart on this axis) and what keeps a parallel wall's
                        // far end and its doors' jambs from standing in for the end pointed at.
                        var origin = planar.Origin;
                        var distance = Math.Abs(
                            (origin.X - localTarget.X) * localDirection.X
                            + (origin.Y - localTarget.Y) * localDirection.Y);

                        if (distance > FaceOffsetTolerance) continue;
                        if (distance >= bestDistance) continue;

                        best = planar.Reference;
                        bestDistance = distance;
                    }
                }

                if (best == null) return null;

                // A reference resolved inside a linked document is meaningless to the host view
                // until it is re-expressed through the link instance that places it.
                return link == null ? best : best.CreateLinkReference(link);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The target point in the wall's own document. Occluder segments are in host
        /// coordinates, but a linked wall's geometry is in the link's.
        /// </summary>
        private static XyPoint ToLocalPoint(XyPoint host, RevitLinkInstance? link)
        {
            if (link == null) return host;

            try
            {
                var point = link.GetTotalTransform().Inverse.OfPoint(new XYZ(host.X, host.Y, 0));
                return new XyPoint(point.X, point.Y);
            }
            catch
            {
                return host;
            }
        }

        /// <summary>
        /// The string's direction in the wall's own document. A direction, so OfVector rather than
        /// OfPoint — it rotates with the link but must not be translated by it.
        /// </summary>
        private static XyPoint ToLocalDirection(XyPoint host, RevitLinkInstance? link)
        {
            if (link == null) return host;

            try
            {
                var vector = link.GetTotalTransform().Inverse.OfVector(new XYZ(host.X, host.Y, 0));
                return new XyPoint(vector.X, vector.Y);
            }
            catch
            {
                return host;
            }
        }
    }
}
