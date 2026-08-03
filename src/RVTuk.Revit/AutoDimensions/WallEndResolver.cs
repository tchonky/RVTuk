using System;
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
        /// Null when there is no wall end within tolerance of the target point, when the wall
        /// runs too far off parallel to the string for its end face to be measurable, or when the
        /// end face itself cannot be resolved — a wall joined into another at that end has its
        /// end face clipped or consumed by Revit, and there may be no planar face there at all.
        /// </summary>
        public static Reference? TryResolve(
            DimensionCandidateSet candidates,
            XyPoint targetEnd,
            XyPoint stringStart,
            XyPoint stringEnd)
        {
            var index = NearestWallEnd(candidates, targetEnd, out var endPoint);
            if (index < 0) return null;

            // An end face faces along its wall, so this is the AlongSegment case — the same test
            // an opening's jambs pass, and for the same geometric reason.
            if (!ReferenceAlignment.CanDimension(
                    stringStart, stringEnd, candidates.Occluders[index], ReferenceNormal.AlongSegment))
                return null;

            var candidate = candidates.OccluderItems[index];
            if (candidate.Wall == null) return null;

            return TryEndFace(candidate.Wall, endPoint, candidate.Link);
        }

        /// <summary>
        /// The nearest end of any collected wall to the target point, within
        /// <see cref="WallEndTolerance"/>. Returns its index into Occluders/OccluderItems, and
        /// hands back the end point itself so the face search knows which end to look at.
        /// </summary>
        private static int NearestWallEnd(
            DimensionCandidateSet candidates, XyPoint target, out XyPoint endPoint)
        {
            endPoint = default;

            var best = -1;
            var bestDistance = WallEndTolerance;

            for (int i = 0; i < candidates.Occluders.Count; i++)
            {
                // OccluderItems is index-aligned with Occluders, but be defensive: a mismatch
                // would otherwise throw deep inside a run.
                if (i >= candidates.OccluderItems.Count) break;

                var segment = candidates.Occluders[i];
                foreach (var end in new[] { segment.Start, segment.End })
                {
                    var dx = end.X - target.X;
                    var dy = end.Y - target.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance >= bestDistance) continue;

                    best = i;
                    bestDistance = distance;
                    endPoint = end;
                }
            }

            return best;
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

                        var origin = planar.Origin;
                        var dx = origin.X - localTarget.X;
                        var dy = origin.Y - localTarget.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);
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
