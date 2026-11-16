using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>A height text and the line it claims (null when it has no link).</summary>
    public sealed record TextClaim(long TextId, long? LineId, XyPoint Position);

    /// <summary>A topo line and the middle point its height text belongs at.</summary>
    public sealed record LineSpot(long LineId, XyPoint Midpoint);

    /// <summary>
    /// What to do with the height texts. LineByText holds every text that ends up linked
    /// (kept and relinked); Relinked lists the ones whose link changed.
    /// </summary>
    public sealed record LinkPlan(
        IReadOnlyDictionary<long, long> LineByText,
        IReadOnlyList<long> Relinked,
        IReadOnlyList<long> DeleteTexts,
        IReadOnlyList<long> UnlinkedTexts,
        IReadOnlyList<long> LinesNeedingText);

    /// <summary>
    /// Decides which height text belongs to which topo line, so a copied line gets its own
    /// text, a deleted line loses its text, and a re-run never shuffles settled links.
    /// Ties go to the lower id (text id for who keeps a line, line id for which free line).
    /// </summary>
    public static class HeightTextLinker
    {
        public static LinkPlan Plan(IReadOnlyList<TextClaim> texts, IReadOnlyList<LineSpot> lines)
        {
            var lineById = new Dictionary<long, LineSpot>();
            foreach (var line in lines) lineById[line.LineId] = line;

            var lineByText = new Dictionary<long, long>();
            var relinked = new List<long>();
            var delete = new List<long>();
            var unlinked = new List<long>();
            var needRelink = new List<TextClaim>();

            foreach (var group in texts.Where(t => t.LineId.HasValue && lineById.ContainsKey(t.LineId.Value))
                                       .GroupBy(t => t.LineId!.Value))
            {
                var mid = lineById[group.Key].Midpoint;
                var ordered = group.OrderBy(t => DistSq(t.Position, mid)).ThenBy(t => t.TextId).ToList();
                lineByText[ordered[0].TextId] = group.Key;
                needRelink.AddRange(ordered.Skip(1));
            }

            foreach (var t in texts)
            {
                if (!t.LineId.HasValue) needRelink.Add(t);
                else if (!lineById.ContainsKey(t.LineId.Value)) delete.Add(t.TextId);
            }

            var taken = new HashSet<long>(lineByText.Values);
            foreach (var t in needRelink.OrderBy(t => t.TextId))
            {
                LineSpot? best = null;
                double bestDist = 0;
                foreach (var line in lines)
                {
                    if (taken.Contains(line.LineId)) continue;
                    double d = DistSq(t.Position, line.Midpoint);
                    if (best == null || d < bestDist || (d == bestDist && line.LineId < best.LineId))
                    {
                        best = line;
                        bestDist = d;
                    }
                }

                if (best == null)
                {
                    unlinked.Add(t.TextId);
                    continue;
                }

                taken.Add(best.LineId);
                lineByText[t.TextId] = best.LineId;
                relinked.Add(t.TextId);
            }

            var needing = lines.Where(l => !taken.Contains(l.LineId)).Select(l => l.LineId).ToList();
            return new LinkPlan(lineByText, relinked, delete, unlinked, needing);
        }

        private static double DistSq(XyPoint a, XyPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
