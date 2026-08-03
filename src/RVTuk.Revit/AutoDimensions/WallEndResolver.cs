using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Turns a _DP-Dim Ref line's target end into a dimension reference on a wall's END face.
    ///
    /// The end face, not a side face, is the whole point. ReferenceAlignment requires a
    /// reference's normal to run along the dimension direction: a wall parallel to a string has
    /// side faces facing ACROSS it — which is exactly why parallel walls are invisible to the
    /// automatic pass — and an end face facing ALONG it, which a string can measure. So this
    /// reaches the one reference on a parallel wall a string is entitled to, rather than
    /// bolting on an exception.
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
        /// Feet. How far along the wall a face may sit from the end we were pointed at. A join
        /// extends a wall's geometry past its location-curve end by the thickness of the wall it
        /// meets, so this cannot be zero.
        /// </summary>
        private const double EndFaceOffsetTolerance = 1.0;

        /// <summary>
        /// Null when there is no wall end within tolerance of the target point, when every wall
        /// end within tolerance runs too far off parallel to the string for its end face to be
        /// measurable, or when none of their end faces can be resolved — a wall joined into
        /// another at that end has its end face clipped or consumed by Revit, and there may be no
        /// planar face there at all. Tries every candidate end, nearest first, rather than
        /// betting everything on the single nearest one.
        /// </summary>
        public static Reference? TryResolve(
            DimensionCandidateSet candidates,
            XyPoint targetEnd,
            XyPoint stringStart,
            XyPoint stringEnd)
        {
            foreach (var wallEnd in WallEndsNear(candidates, targetEnd))
            {
                // An end face faces along its wall, so this is the AlongSegment case — the same
                // test an opening's jambs pass, and for the same geometric reason.
                if (!ReferenceAlignment.CanDimension(
                        stringStart, stringEnd, candidates.Occluders[wallEnd.Index],
                        ReferenceNormal.AlongSegment))
                    continue;

                var candidate = candidates.OccluderItems[wallEnd.Index];
                if (candidate.Wall == null) continue;

                var reference = TryEndFace(candidate.Wall, wallEnd.Point, candidate.Link);
                if (reference != null) return reference;
            }

            return null;
        }

        /// <summary>
        /// Every collected wall end within <see cref="WallEndTolerance"/> of the target point,
        /// nearest first.
        ///
        /// All of them, not just the nearest: a parallel wall's end is usually at a corner, and
        /// joined walls' location curves meet at the same point — so the nearest end is as likely
        /// to belong to the perpendicular wall, whose end face no string running along it can
        /// measure. Returning one candidate made that a coin flip.
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
        /// The wall's end face nearest the target point: a vertical planar face whose normal runs
        /// along the wall (its ends) rather than across it (its sides).
        ///
        /// ComputeReferences is required or the face's Reference comes back null, and the options
        /// carry no View on purpose — a view-specific resolution would have to be redone for every
        /// fanned-out view, and could differ between them.
        /// </summary>
        private static Reference? TryEndFace(Wall wall, XyPoint target, RevitLinkInstance? link)
        {
            try
            {
                if ((wall.Location as LocationCurve)?.Curve is not Line centerline) return null;

                var localTarget = ToLocal(target, link);
                var direction = centerline.Direction;
                var wallAngle = Math.Atan2(direction.Y, direction.X);

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
                        // Atan2(0, 0) is 0, which would read as perfectly parallel to the wall.
                        var normal = planar.FaceNormal;
                        if (Math.Abs(normal.Z) > VerticalTolerance) continue;

                        var normalAngle = Math.Atan2(normal.Y, normal.X);
                        if (Angle2D.FromParallelDegrees(wallAngle, normalAngle) > NormalToleranceDegrees)
                            continue;

                        // How far the face's plane sits from the wall end we were pointed at,
                        // measured ALONG the wall. The far end lands a whole wall length away and
                        // a door's jamb lands at the door — both are vertical faces whose normal
                        // runs along the wall, so they qualify exactly as well as the end we want.
                        // Without this bound the search silently returns one of them instead of
                        // reporting that it found nothing.
                        var origin = planar.Origin;
                        var distance = Math.Abs(
                            (origin.X - localTarget.X) * direction.X
                            + (origin.Y - localTarget.Y) * direction.Y);

                        if (distance > EndFaceOffsetTolerance) continue;
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
        private static XyPoint ToLocal(XyPoint host, RevitLinkInstance? link)
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
    }
}
