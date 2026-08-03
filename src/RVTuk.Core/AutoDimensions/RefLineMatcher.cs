using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>Where a ref line meets one string: which string, and where along it.</summary>
    public readonly record struct RefLineHit(int LineIndex, double T);

    /// <summary>
    /// Which strings a ref line joins, and which of its own ends points away from them at the
    /// thing to be referenced.
    /// </summary>
    public sealed record RefLineMatch(IReadOnlyList<RefLineHit> Hits, XyPoint TargetEnd);

    /// <summary>
    /// The 2D half of a _DP-Dim Ref line: which dimension strings it touches or crosses, at what
    /// station along each, and which of its endpoints aims at the wall. Everything the Revit side
    /// needs before it goes looking for a face.
    ///
    /// A ref line joins EVERY string it meets — how far it is drawn is the control. Stopping it
    /// at the inner string marks only the inner string; running it out through both marks both.
    ///
    /// The target end is the endpoint farther from the strings it met, which is the natural
    /// reading of a line drawn from a wall end out to a string. Overshooting PAST the string by
    /// more than the wall's own distance from it flips which end is read as the target; the wall
    /// search then finds nothing and the run summary says so.
    /// </summary>
    public static class RefLineMatcher
    {
        private const double MinimumLength = 1e-12;

        public static RefLineMatch? Match(
            XyPoint refStart,
            XyPoint refEnd,
            IReadOnlyList<ReferenceLine> lines,
            double touchTolerance)
        {
            var refX = refEnd.X - refStart.X;
            var refY = refEnd.Y - refStart.Y;
            var refLengthSquared = refX * refX + refY * refY;
            if (refLengthSquared < MinimumLength) return null;

            // The tolerance is a length; the parameter it buys depends on how long the line is.
            var slack = touchTolerance / Math.Sqrt(refLengthSquared);

            var hits = new List<RefLineHit>();
            var minU = double.MaxValue;
            var maxU = double.MinValue;

            for (int l = 0; l < lines.Count; l++)
            {
                if (!TryIntersect(refStart, refX, refY, lines[l], out var u, out var t)) continue;

                // Slack on the ref line's own span, so a line stopping just short of the string
                // still counts as touching it. None on the string's span: a ref line meeting a
                // string past its end does not join it.
                if (u < -slack || u > 1 + slack) continue;
                if (t < 0 || t > 1) continue;

                hits.Add(new RefLineHit(l, t));
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }

            if (hits.Count == 0) return null;

            // Both ends sitting on strings leaves no end pointing at anything.
            if (minU <= slack && maxU >= 1 - slack) return null;

            var targetEnd = minU > 1 - maxU ? refStart : refEnd;
            return new RefLineMatch(hits, targetEnd);
        }

        /// <summary>
        /// Intersection in the two lines' own parameters: u along the ref line, t along the
        /// string. False when they are parallel — a ref line running along a string never meets
        /// it at a point.
        /// </summary>
        private static bool TryIntersect(
            XyPoint refStart,
            double refX,
            double refY,
            ReferenceLine line,
            out double u,
            out double t)
        {
            u = 0;
            t = 0;

            var lineX = line.End.X - line.Start.X;
            var lineY = line.End.Y - line.Start.Y;

            var denominator = refX * lineY - refY * lineX;
            if (Math.Abs(denominator) < MinimumLength) return false;

            var dx = line.Start.X - refStart.X;
            var dy = line.Start.Y - refStart.Y;

            u = (dx * lineY - dy * lineX) / denominator;
            t = (dx * refY - dy * refX) / denominator;
            return true;
        }
    }
}
