using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>One topo line for the corner rule: its height (shared metres) and its plan polyline
    /// (metres), as drawn.</summary>
    public sealed record CornerLine(double HeightMetres, IReadOnlyList<XyPoint> PolylineMetres);

    /// <summary>
    /// Rule N of the 2026-11-19 spec ("The rule"): the height an outline corner takes from the topo
    /// lines around it. <see cref="Prepare"/> is run once per Update (step −1, cleaning, and step 0,
    /// bridges); <see cref="HeightAt"/> is steps 1–5 for one corner. Every number below is part of
    /// the rule. Ported from docs/tools/topo-tools/specs/2026-11-19-outline-corner-heights-reference.py;
    /// where the two differ the spec text is followed.
    ///
    /// Every tie is a set selection (the smallest value, then everything within the tolerance of it,
    /// then an exact key), never a sort with a tolerance comparer, and every segment is worked with
    /// its ends in a fixed (x, then y) order, so the result does not depend on the order or the
    /// direction the lines were drawn in.
    /// </summary>
    public sealed class OutlineCornerHeights
    {
        private const double SameSpot = 0.001;        // 1 mm: on-line snap, closed lines, bridge ends, sight lines
        private const long SameZ = 5;                 // tenths of a millimetre: 0.5 mm is "the same height"
        private const double HeightQuantum = 10000.0; // heights are whole tenths of a millimetre
        private const double NearLine = 0.05;         // nothing beyond, but within 5 cm: take the line's height
        private const double Tie = 1e-9;              // distances within this are equal
        private const double Touch = 1e-9;            // segments this close touch
        private const double FarEps = 1e-9;           // the far side is (P − V)·u − 1e-9 > 0
        private const double ShortPart = 1e-9;        // far parts no longer than this are ignored
        private const double CleanSpot = 1e-6;        // consecutive vertices closer than this merge
        private const double InViewShortcut = 0.002;  // a candidate this close is in view
        private const double Parallel = 1e-12;        // |cross(u, d)| ≤ 1e-12·|d|: the walk skips the segment
        private const double RayParam = 1e-12;        // the walk's segment parameter tolerance (as the reference)
        private static readonly double FacingCos = Math.Cos(60.0 * Math.PI / 180.0);

        private readonly Line[] _all;   // the drawn lines, then the bridges
        private readonly double _ox;
        private readonly double _oy;
        private readonly int _maxSegments;

        private OutlineCornerHeights(Line[] all, double ox, double oy, IReadOnlyList<CornerLine> bridges)
        {
            _all = all;
            _ox = ox;
            _oy = oy;
            Bridges = bridges;
            foreach (var ln in all) _maxSegments = Math.Max(_maxSegments, ln.Ax.Length);
        }

        /// <summary>The bridges of step 0, in the callers' coordinates (metres), at their height. Never
        /// placed, stored or drawn; they only take part in <see cref="HeightAt"/>.</summary>
        public IReadOnlyList<CornerLine> Bridges { get; }

        /// <summary>Step −1 (cleaning) and step 0 (bridges), once per Update.</summary>
        public static OutlineCornerHeights Prepare(IReadOnlyList<CornerLine> lines)
        {
            if (lines is null) throw new ArgumentNullException(nameof(lines));

            // The local origin: the bounding-box minimum, rounded to whole metres.
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            foreach (var line in lines)
            {
                foreach (var p in line.PolylineMetres)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                }
            }
            var ox = double.IsInfinity(minX) ? 0.0 : Math.Round(minX);
            var oy = double.IsInfinity(minY) ? 0.0 : Math.Round(minY);

            var drawn = new List<Line>(lines.Count);
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var line in lines)
            {
                xs.Clear();
                ys.Clear();
                foreach (var p in line.PolylineMetres)
                {
                    double x = p.X - ox, y = p.Y - oy;
                    if (xs.Count > 0 && Hypot(x - xs[xs.Count - 1], y - ys[ys.Count - 1]) < CleanSpot) continue;
                    xs.Add(x);
                    ys.Add(y);
                }
                if (xs.Count < 2) continue;
                var zq = (long)Math.Round(line.HeightMetres * HeightQuantum, MidpointRounding.AwayFromZero);
                drawn.Add(new Line(zq, xs.ToArray(), ys.ToArray()));
            }

            var bridges = new List<Line>();
            var published = new List<CornerLine>();
            MakeBridges(drawn, bridges);
            foreach (var b in bridges)
            {
                published.Add(new CornerLine(b.Z, new[]
                {
                    new XyPoint(b.Xs[0] + ox, b.Ys[0] + oy),
                    new XyPoint(b.Xs[1] + ox, b.Ys[1] + oy),
                }));
            }

            var all = new Line[drawn.Count + bridges.Count];
            drawn.CopyTo(all, 0);
            bridges.CopyTo(all, drawn.Count);
            return new OutlineCornerHeights(all, ox, oy, published);
        }

        // ---- step 0: bridges ------------------------------------------------------------------

        private readonly struct End
        {
            public End(int line, double x, double y, double tx, double ty, long zq)
            {
                Line = line; X = x; Y = y; Tx = tx; Ty = ty; Zq = zq;
            }

            public int Line { get; }
            public double X { get; }
            public double Y { get; }
            public double Tx { get; }
            public double Ty { get; }
            public long Zq { get; }
        }

        private static void MakeBridges(List<Line> drawn, List<Line> bridges)
        {
            var ends = new List<End>();
            for (var i = 0; i < drawn.Count; i++)
            {
                var ln = drawn[i];
                var n = ln.Xs.Length;
                if (Hypot(ln.Xs[0] - ln.Xs[n - 1], ln.Ys[0] - ln.Ys[n - 1]) <= SameSpot) continue; // closed
                AddEnd(ends, drawn, i, 0, +1);
                AddEnd(ends, drawn, i, n - 1, -1);
            }

            // Each free end picks the nearest free end facing it; within 1e-9 m the smaller x of the
            // other end, then the smaller y.
            var pick = new int[ends.Count];
            for (var a = 0; a < ends.Count; a++)
            {
                pick[a] = -1;
                var gMin = double.PositiveInfinity;
                for (var b = 0; b < ends.Count; b++)
                {
                    if (Faces(ends, a, b, out var g) && g < gMin) gMin = g;
                }
                if (double.IsInfinity(gMin)) continue;
                for (var b = 0; b < ends.Count; b++)
                {
                    if (!Faces(ends, a, b, out var g) || g > gMin + Tie) continue;
                    if (pick[a] < 0 || ends[b].X < ends[pick[a]].X
                        || (ends[b].X == ends[pick[a]].X && ends[b].Y < ends[pick[a]].Y))
                    {
                        pick[a] = b;
                    }
                }
            }

            for (var a = 0; a < ends.Count; a++)
            {
                var b = pick[a];
                if (b <= a || pick[b] != a) continue;
                var e = ends[a];
                var f = ends[b];
                var zq = Math.Max(e.Zq, f.Zq);
                var g = Hypot(f.X - e.X, f.Y - e.Y);
                double wx = (f.X - e.X) / g, wy = (f.Y - e.Y) / g;
                double s0x = e.X + SameSpot * wx, s0y = e.Y + SameSpot * wy;
                double s1x = f.X - SameSpot * wx, s1y = f.Y - SameSpot * wy;
                var blocked = false;
                foreach (var ln in drawn)
                {
                    if (Math.Abs(ln.Zq - zq) <= SameZ) continue;
                    if (TouchesLine(ln, s0x, s0y, s1x, s1y)) { blocked = true; break; }
                }
                if (!blocked) bridges.Add(new Line(zq, new[] { e.X, f.X }, new[] { e.Y, f.Y }));
            }
        }

        /// <summary>The end of line <paramref name="i"/> at vertex <paramref name="k"/>, when free.
        /// Its direction comes from the first vertex at least 1 mm back along the line.</summary>
        private static void AddEnd(List<End> ends, List<Line> drawn, int i, int k, int step)
        {
            var ln = drawn[i];
            double ex = ln.Xs[k], ey = ln.Ys[k];
            var back = -1;
            for (var m = k + step; m >= 0 && m < ln.Xs.Length; m += step)
            {
                if (Hypot(ln.Xs[m] - ex, ln.Ys[m] - ey) >= SameSpot) { back = m; break; }
            }
            if (back < 0) return;

            for (var j = 0; j < drawn.Count; j++)
            {
                if (j == i || Math.Abs(drawn[j].Zq - ln.Zq) > SameZ) continue;
                if (!drawn[j].BoxNear(ex, ey, SameSpot)) continue;
                if (DistanceToLine(drawn[j], ex, ey) <= SameSpot) return;
            }

            double dx = ex - ln.Xs[back], dy = ey - ln.Ys[back];
            var len = Hypot(dx, dy);
            ends.Add(new End(i, ex, ey, dx / len, dy / len, ln.Zq));
        }

        private static bool Faces(List<End> ends, int a, int b, out double g)
        {
            g = 0;
            if (a == b) return false;
            var e = ends[a];
            var f = ends[b];
            if (Math.Abs(e.Zq - f.Zq) > SameZ) return false;
            double dx = f.X - e.X, dy = f.Y - e.Y;
            g = Hypot(dx, dy);
            if (g <= SameSpot) return false;
            return dx * e.Tx + dy * e.Ty >= FacingCos * g && -dx * f.Tx - dy * f.Ty >= FacingCos * g;
        }

        // ---- steps 1–5 ------------------------------------------------------------------------

        /// <summary>The height outline point <paramref name="v"/> (metres, the callers'
        /// coordinates) takes from the lines, or null when it is left alone.</summary>
        public double? HeightAt(XyPoint v)
        {
            var n = _all.Length;
            if (n == 0) return null;
            double vx = v.X - _ox, vy = v.Y - _oy;

            // 1. The nearest point of every line. 2. On a line: the highest such line's height.
            // A line whose bounding box is further than both 1 mm and (the nearest line found so
            // far + 1e-9 m) can be neither on V nor in step 3's set: its distance stays infinite.
            var scratch = new double[_maxSegments];
            var d = new double[n];
            var px = new double[n];
            var py = new double[n];
            var lowerBound = new double[n];
            var first = 0;
            for (var i = 0; i < n; i++)
            {
                lowerBound[i] = _all[i].BoxDistance(vx, vy) - 1e-12; // round-off margin
                if (lowerBound[i] < lowerBound[first]) first = i;
            }
            var onLine = long.MinValue;
            var dMin = double.PositiveInfinity;
            for (var k = 0; k < n; k++)
            {
                var i = k == 0 ? first : (k == first ? 0 : k);
                if (lowerBound[i] > SameSpot && lowerBound[i] > dMin + Tie)
                {
                    d[i] = double.PositiveInfinity;
                    continue;
                }
                Nearest(_all[i], vx, vy, scratch, out d[i], out px[i], out py[i]);
                if (d[i] <= SameSpot && _all[i].Zq > onLine) onLine = _all[i].Zq;
                if (d[i] < dMin) dMin = d[i];
            }
            if (onLine != long.MinValue) return onLine / HeightQuantum;

            // 3. L1: the nearest within 1e-9 m, within 0.5 mm of the highest of those, then the
            // smallest P by (x, then y).
            var tHigh = long.MinValue;
            for (var i = 0; i < n; i++)
            {
                if (d[i] <= dMin + Tie && _all[i].Zq > tHigh) tHigh = _all[i].Zq;
            }
            var i1 = -1;
            for (var i = 0; i < n; i++)
            {
                if (d[i] > dMin + Tie || tHigh - _all[i].Zq > SameZ) continue;
                if (i1 < 0 || px[i] < px[i1] || (px[i] == px[i1] && py[i] < py[i1])) i1 = i;
            }
            var d1 = d[i1];
            var zq1 = _all[i1].Zq;
            var z1 = _all[i1].Z;
            double ux = (vx - px[i1]) / d1, uy = (vy - py[i1]) / d1;

            // 4. Every line's part on the far side, and its nearest point.
            var e = new double[n];
            var qx = new double[n];
            var qy = new double[n];
            var candidate = new bool[n];
            var farMin = double.PositiveInfinity;
            var anyCandidate = false;
            for (var j = 0; j < n; j++)
            {
                if (!FarNearest(_all[j], vx, vy, ux, uy, scratch, out e[j], out qx[j], out qy[j])) continue;
                if (e[j] < farMin) farMin = e[j];
                if (Math.Abs(_all[j].Zq - zq1) > SameZ)
                {
                    candidate[j] = true;
                    anyCandidate = true;
                }
            }

            // 4a. Nothing beyond.
            if (double.IsInfinity(farMin)) return d1 <= NearLine + Tie ? z1 : (double?)null;
            if (!anyCandidate) return z1;

            // 4b–d. The candidates, nearest first (within 1e-9 m the higher, then list order).
            var tSame = double.NaN;
            while (true)
            {
                var eMin = double.PositiveInfinity;
                for (var j = 0; j < n; j++)
                {
                    if (candidate[j] && e[j] < eMin) eMin = e[j];
                }
                if (double.IsInfinity(eMin)) break;
                var c = -1;
                for (var j = 0; j < n; j++)
                {
                    if (!candidate[j] || e[j] > eMin + Tie) continue;
                    if (c < 0 || _all[j].Zq > _all[c].Zq) c = j;
                }

                if (e[c] > farMin + Tie)
                {
                    if (double.IsNaN(tSame)) tSame = WalkToSameHeight(vx, vy, ux, uy, zq1);
                    if (e[c] > tSame) break;
                    if (Hypot(qx[c] - vx, qy[c] - vy) > InViewShortcut && SightBlocked(vx, vy, qx[c], qy[c], c))
                    {
                        candidate[c] = false;
                        continue;
                    }
                }

                // 5. Between L1 and L2.
                return z1 + (_all[c].Z - z1) * d1 / (d1 + e[c]);
            }
            return z1;
        }

        /// <summary>4c. The smallest t &gt; 0 at which V + t·u meets a line of h1's height, a vertex
        /// within 1 mm of the walk counting at its projection; infinite when there is none.</summary>
        private double WalkToSameHeight(double vx, double vy, double ux, double uy, long zq1)
        {
            var best = double.PositiveInfinity;
            foreach (var ln in _all)
            {
                if (Math.Abs(ln.Zq - zq1) > SameZ) continue;
                for (var s = 0; s < ln.Ax.Length; s++)
                {
                    double ax = ln.Ax[s], ay = ln.Ay[s];
                    double ex = ln.Bx[s] - ax, ey = ln.By[s] - ay;
                    var den = ux * ey - uy * ex;
                    if (Math.Abs(den) <= Parallel * Hypot(ex, ey)) continue;
                    double apx = ax - vx, apy = ay - vy;
                    var t = (apx * ey - apy * ex) / den;
                    var r = (apx * uy - apy * ux) / den;
                    if (t > 0 && r >= -RayParam && r <= 1 + RayParam && t < best) best = t;
                }
                for (var k = 0; k < ln.Xs.Length; k++)
                {
                    double wx = ln.Xs[k] - vx, wy = ln.Ys[k] - vy;
                    var t = wx * ux + wy * uy;
                    if (t > 0 && Math.Abs(wx * uy - wy * ux) <= SameSpot && t < best) best = t;
                }
            }
            return best;
        }

        /// <summary>4d. The segment from V to 1 mm short of Q touches a line other than
        /// <paramref name="skip"/> (drawn or bridge).</summary>
        private bool SightBlocked(double vx, double vy, double qx, double qy, int skip)
        {
            var len = Hypot(qx - vx, qy - vy);
            double sx = qx + (vx - qx) * SameSpot / len, sy = qy + (vy - qy) * SameSpot / len;
            for (var k = 0; k < _all.Length; k++)
            {
                if (k != skip && TouchesLine(_all[k], vx, vy, sx, sy)) return true;
            }
            return false;
        }

        // ---- geometry ---------------------------------------------------------------------------

        private sealed class Line
        {
            public Line(long zq, double[] xs, double[] ys)
            {
                Zq = zq;
                Z = zq / HeightQuantum;
                Xs = xs;
                Ys = ys;
                var m = xs.Length - 1;
                Ax = new double[m];
                Ay = new double[m];
                Bx = new double[m];
                By = new double[m];
                MinX = MinY = double.PositiveInfinity;
                MaxX = MaxY = double.NegativeInfinity;
                for (var k = 0; k < xs.Length; k++)
                {
                    MinX = Math.Min(MinX, xs[k]);
                    MinY = Math.Min(MinY, ys[k]);
                    MaxX = Math.Max(MaxX, xs[k]);
                    MaxY = Math.Max(MaxY, ys[k]);
                }
                for (var s = 0; s < m; s++)
                {
                    double ax = xs[s], ay = ys[s], bx = xs[s + 1], by = ys[s + 1];
                    if (bx < ax || (bx == ax && by < ay))
                    {
                        (ax, bx) = (bx, ax);
                        (ay, by) = (by, ay);
                    }
                    Ax[s] = ax;
                    Ay[s] = ay;
                    Bx[s] = bx;
                    By[s] = by;
                }
            }

            public long Zq { get; }
            public double Z { get; }
            public double[] Xs { get; }
            public double[] Ys { get; }

            /// <summary>The segments, each with its ends in (x, then y) order.</summary>
            public double[] Ax { get; }
            public double[] Ay { get; }
            public double[] Bx { get; }
            public double[] By { get; }

            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }

            /// <summary>The distance from (x, y) to the bounding box: never more than the distance
            /// to the polyline.</summary>
            public double BoxDistance(double x, double y)
            {
                var dx = x < MinX ? MinX - x : (x > MaxX ? x - MaxX : 0.0);
                var dy = y < MinY ? MinY - y : (y > MaxY ? y - MaxY : 0.0);
                return Hypot(dx, dy);
            }

            public bool BoxNear(double x, double y, double margin) =>
                x >= MinX - margin && x <= MaxX + margin && y >= MinY - margin && y <= MaxY + margin;
        }

        private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

        /// <summary>The nearest point of segment ab to v, by clamped projection.</summary>
        private static double SegNearest(double vx, double vy, double ax, double ay, double bx, double by,
            out double px, out double py)
        {
            double dx = bx - ax, dy = by - ay;
            var ll = dx * dx + dy * dy;
            var t = ll <= 0 ? 0.0 : ((vx - ax) * dx + (vy - ay) * dy) / ll;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            px = ax + t * dx;
            py = ay + t * dy;
            return Hypot(vx - px, vy - py);
        }

        private static double DistanceToLine(Line ln, double vx, double vy)
        {
            var best = double.PositiveInfinity;
            for (var s = 0; s < ln.Ax.Length; s++)
            {
                var dd = SegNearest(vx, vy, ln.Ax[s], ln.Ay[s], ln.Bx[s], ln.By[s], out _, out _);
                if (dd < best) best = dd;
            }
            return best;
        }

        /// <summary>Step 1: the distance from v to the polyline and its nearest point; equally near
        /// points (within 1e-9 m) give the one with the smaller x, then the smaller y.
        /// <paramref name="scratch"/> holds each segment's distance between the two passes.</summary>
        private static void Nearest(Line ln, double vx, double vy, double[] scratch,
            out double d, out double px, out double py)
        {
            d = double.PositiveInfinity;
            for (var s = 0; s < ln.Ax.Length; s++)
            {
                var dd = SegNearest(vx, vy, ln.Ax[s], ln.Ay[s], ln.Bx[s], ln.By[s], out _, out _);
                scratch[s] = dd;
                if (dd < d) d = dd;
            }
            px = py = double.NaN;
            var found = false;
            for (var s = 0; s < ln.Ax.Length; s++)
            {
                if (scratch[s] > d + Tie) continue;
                SegNearest(vx, vy, ln.Ax[s], ln.Ay[s], ln.Bx[s], ln.By[s], out var x, out var y);
                if (!found || x < px || (x == px && y < py))
                {
                    px = x;
                    py = y;
                    found = true;
                }
            }
        }

        /// <summary>Segment s of the line clipped to the far side {P : (P − V)·u − 1e-9 &gt; 0}: false
        /// when no part longer than 1e-9 m is left, else the part's nearest point to v.</summary>
        private static bool FarSegment(Line ln, int s, double vx, double vy, double ux, double uy,
            out double dist, out double px, out double py)
        {
            dist = px = py = 0;
            double ax = ln.Ax[s], ay = ln.Ay[s], bx = ln.Bx[s], by = ln.By[s];
            var sa = ux * (ax - vx) + uy * (ay - vy) - FarEps;
            var sb = ux * (bx - vx) + uy * (by - vy) - FarEps;
            if (sa <= 0 && sb <= 0) return false;
            double lo = 0, hi = 1;
            if (!(sa > 0 && sb > 0))
            {
                var tc = sa / (sa - sb);
                if (sa <= 0) lo = tc;
                else hi = tc;
            }
            double dx = bx - ax, dy = by - ay;
            if ((hi - lo) * Hypot(dx, dy) <= ShortPart) return false;
            dist = SegNearest(vx, vy, ax + dx * lo, ay + dy * lo, ax + dx * hi, ay + dy * hi, out px, out py);
            return true;
        }

        /// <summary>Step 4: the nearest point of the line's far part (equally near, within 1e-9 m:
        /// the smaller x, then y); false when the line has no far part.</summary>
        private static bool FarNearest(Line ln, double vx, double vy, double ux, double uy, double[] scratch,
            out double e, out double qx, out double qy)
        {
            e = double.PositiveInfinity;
            qx = qy = double.NaN;
            // The whole box on the near side: no far part. The box's best corner bounds every
            // vertex's s, also in floating point (each term is monotone in its coordinate).
            var sBox = Math.Max(ux * (ln.MaxX - vx), ux * (ln.MinX - vx))
                + Math.Max(uy * (ln.MaxY - vy), uy * (ln.MinY - vy));
            if (sBox - FarEps <= 0) return false;
            for (var s = 0; s < ln.Ax.Length; s++)
            {
                scratch[s] = FarSegment(ln, s, vx, vy, ux, uy, out var dd, out _, out _) ? dd : double.PositiveInfinity;
                if (scratch[s] < e) e = scratch[s];
            }
            if (double.IsInfinity(e)) return false;
            var found = false;
            for (var s = 0; s < ln.Ax.Length; s++)
            {
                if (scratch[s] > e + Tie) continue;
                FarSegment(ln, s, vx, vy, ux, uy, out _, out var x, out var y);
                if (!found || x < qx || (x == qx && y < qy))
                {
                    qx = x;
                    qy = y;
                    found = true;
                }
            }
            return true;
        }

        /// <summary>Some segment of the line touches the closed segment pq.</summary>
        private static bool TouchesLine(Line ln, double px, double py, double qx, double qy)
        {
            if (px > qx || (px == qx && py > qy))
            {
                (px, qx) = (qx, px);
                (py, qy) = (qy, py);
            }
            double loX = Math.Min(px, qx) - Touch, hiX = Math.Max(px, qx) + Touch;
            double loY = Math.Min(py, qy) - Touch, hiY = Math.Max(py, qy) + Touch;
            if (ln.MaxX < loX || ln.MinX > hiX || ln.MaxY < loY || ln.MinY > hiY) return false;
            for (var s = 0; s < ln.Ax.Length; s++)
            {
                double ax = ln.Ax[s], ay = ln.Ay[s], bx = ln.Bx[s], by = ln.By[s];
                if (Math.Max(ax, bx) < loX || Math.Min(ax, bx) > hiX || Math.Max(ay, by) < loY || Math.Min(ay, by) > hiY) continue;
                if (SegmentsTouch(px, py, qx, qy, ax, ay, bx, by)) return true;
            }
            return false;
        }

        /// <summary>Two closed segments touch when their distance is at most 1e-9 m: 0 when the
        /// strict orientation signs show a proper crossing, else the smallest endpoint-to-segment
        /// distance. Both segments come with their ends in (x, then y) order.</summary>
        private static bool SegmentsTouch(double px, double py, double qx, double qy,
            double ax, double ay, double bx, double by)
        {
            var d1 = Orient(ax, ay, bx, by, px, py);
            var d2 = Orient(ax, ay, bx, by, qx, qy);
            var d3 = Orient(px, py, qx, qy, ax, ay);
            var d4 = Orient(px, py, qx, qy, bx, by);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            return SegNearest(px, py, ax, ay, bx, by, out _, out _) <= Touch
                || SegNearest(qx, qy, ax, ay, bx, by, out _, out _) <= Touch
                || SegNearest(ax, ay, px, py, qx, qy, out _, out _) <= Touch
                || SegNearest(bx, by, px, py, qx, qy, out _, out _) <= Touch;
        }

        private static double Orient(double ax, double ay, double bx, double by, double cx, double cy) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
    }
}
