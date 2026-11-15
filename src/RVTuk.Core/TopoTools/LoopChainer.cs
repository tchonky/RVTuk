using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>One curve of a sketch loop, by its two ends.</summary>
    public readonly record struct Segment(XyzPoint Start, XyzPoint End);

    /// <summary>A curve's place in a loop: which one, and whether it runs backwards.</summary>
    public readonly record struct ChainLink(int Index, bool Reversed);

    /// <summary>
    /// Puts a sketch loop's curves head to tail. <c>Sketch.Profile</c> hands back each loop's
    /// curves, but nothing promises they come in order or all run the same way round, and
    /// <c>CurveLoop.Append</c> throws unless each curve starts where the last one ended. A circle
    /// is the everyday case: Revit draws it as two arcs that may both run the same way.
    /// </summary>
    public static class LoopChainer
    {
        /// <returns>The curves in order, or null when they do not close into exactly one loop.</returns>
        public static IReadOnlyList<ChainLink>? Chain(IReadOnlyList<Segment> segments, double tolerance)
        {
            if (segments.Count == 0) return null;

            var used = new bool[segments.Count];
            var chain = new List<ChainLink> { new ChainLink(0, false) };
            used[0] = true;
            var loopStart = segments[0].Start;
            var end = segments[0].End;

            for (int step = 1; step < segments.Count; step++)
            {
                int next = -1;
                bool reversed = false;
                for (int i = 0; i < segments.Count && next < 0; i++)
                {
                    if (used[i]) continue;
                    if (Near(segments[i].Start, end, tolerance))
                    {
                        next = i;
                        reversed = false;
                    }
                    else if (Near(segments[i].End, end, tolerance))
                    {
                        next = i;
                        reversed = true;
                    }
                }

                if (next < 0) return null;

                used[next] = true;
                chain.Add(new ChainLink(next, reversed));
                end = reversed ? segments[next].Start : segments[next].End;
            }

            return Near(end, loopStart, tolerance) ? chain : null;
        }

        private static bool Near(XyzPoint a, XyzPoint b, double tolerance) =>
            Math.Abs(a.X - b.X) <= tolerance &&
            Math.Abs(a.Y - b.Y) <= tolerance &&
            Math.Abs(a.Z - b.Z) <= tolerance;
    }
}
