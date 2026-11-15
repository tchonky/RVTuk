using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What one Topo Edit session did, in the words the pane's status line uses. Kept in Core
    /// and tested, as the only text the session shows after each run.
    ///
    /// Sentences are joined by a single space, in this order: applied, skipped, moved by hand,
    /// lost, unlinked.
    /// </summary>
    public sealed class TopoSessionReport
    {
        public const string NothingOverTarget = "No topo line in this view lies over the selected element.";

        public int LinesApplied { get; set; }
        public int PointsAdded { get; set; }
        public int PointsReplaced { get; set; }
        public int PointsMovedByHand { get; set; }
        public int PointsLost { get; set; }

        /// <summary>The raw text of each height that could not be read.</summary>
        public List<string> UnreadableHeights { get; } = new List<string>();

        public int UnlinkedTexts { get; set; }

        public bool HasProblems =>
            UnreadableHeights.Count > 0 || PointsMovedByHand > 0 || PointsLost > 0 || UnlinkedTexts > 0;

        public string Summarise()
        {
            var sentences = new List<string>();

            if (LinesApplied > 0)
            {
                sentences.Add(
                    Count(LinesApplied, "line", "lines") + " applied: " +
                    Count(PointsAdded, "point", "points") + " added, " +
                    PointsReplaced + " replaced.");
            }
            else if (!HasProblems)
            {
                return NothingOverTarget;
            }

            if (UnreadableHeights.Count > 0)
            {
                var quoted = new List<string>();
                foreach (var text in UnreadableHeights) quoted.Add("'" + text + "'");

                if (UnreadableHeights.Count == 1)
                    sentences.Add("1 line skipped: " + quoted[0] + " is not a height.");
                else
                    sentences.Add(UnreadableHeights.Count + " lines skipped: " + string.Join(", ", quoted) + " are not heights.");
            }

            if (PointsMovedByHand > 0)
            {
                sentences.Add(PointsMovedByHand == 1
                    ? "1 earlier point had been moved by hand and was left alone."
                    : PointsMovedByHand + " earlier points had been moved by hand and were left alone.");
            }

            if (PointsLost > 0)
                sentences.Add(Count(PointsLost, "point", "points") + " could not be placed.");

            if (UnlinkedTexts > 0)
            {
                sentences.Add(UnlinkedTexts == 1
                    ? "1 height text is not linked to any line and was left alone."
                    : UnlinkedTexts + " height texts are not linked to any line and were left alone.");
            }

            return string.Join(" ", sentences);
        }

        private static string Count(int n, string singular, string plural) =>
            n + " " + (n == 1 ? singular : plural);
    }
}
