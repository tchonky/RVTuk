using System;
using System.Collections.Generic;
using System.Globalization;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What one Update did, in the words of its summary dialog. Accept always shows the summary;
    /// the Update button only when <see cref="ShowAfterUpdate"/> is true. Kept in Core and tested,
    /// as the only account the user gets of what an Update applied.
    ///
    /// Sentences are joined by a single space, in this order: applied, crossings, skipped, moved by
    /// hand, lost, unlinked, Survey Point marker; when nothing was applied and there is no
    /// problem, <see cref="NothingOverTarget"/> stands in for all but the marker's, which alone can
    /// follow it.
    /// </summary>
    public sealed class TopoSessionReport
    {
        public const string NothingOverTarget =
            "No topo line in the session's view lies over the toposolid or floor being edited.";

        public int LinesApplied { get; set; }
        public int PointsAdded { get; set; }
        public int PointsReplaced { get; set; }
        public int PointsMovedByHand { get; set; }
        public int PointsLost { get; set; }

        /// <summary>Points dropped because a higher line's point stood at the same spot
        /// (<see cref="TopoPointDeduper"/>). The rule, not a problem: no warning.</summary>
        public int PointsDroppedAtCrossings { get; set; }

        /// <summary>The raw text of each height that could not be read.</summary>
        public List<string> UnreadableHeights { get; } = new List<string>();

        public int UnlinkedTexts { get; set; }

        /// <summary>The Survey Point marker's own height (its Elev) in metres, or null for no note.
        /// Heights are measured like a Survey Point level, not from the marker, so a marker at least
        /// half a millimetre off 0 is named in the last sentence, rounded to the millimetre: a note,
        /// not a problem.</summary>
        public double? SurveyPointMarkerMetres { get; set; }

        public bool HasProblems =>
            UnreadableHeights.Count > 0 || PointsMovedByHand > 0 || PointsLost > 0 || UnlinkedTexts > 0;

        /// <summary>
        /// Whether the Update button shows this summary: only when it names a problem or nothing was
        /// applied, as a silent Update would hide why nothing changed. Points dropped at crossings and
        /// the marker's note do not count. Accept shows the summary whatever this says.
        /// </summary>
        public bool ShowAfterUpdate => HasProblems || LinesApplied == 0;

        public string Summarise()
        {
            string? markerNote = SurveyPointMarkerNote();
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
                return markerNote == null ? NothingOverTarget : NothingOverTarget + " " + markerNote;
            }

            if (PointsDroppedAtCrossings > 0)
            {
                sentences.Add(Count(PointsDroppedAtCrossings, "point", "points") +
                    " where lines cross took the higher line's height.");
            }

            if (UnreadableHeights.Count > 0)
            {
                var quoted = new List<string>();
                foreach (var text in UnreadableHeights) quoted.Add("'" + text + "'");

                if (UnreadableHeights.Count == 1)
                    sentences.Add("1 line skipped: " + quoted[0] + " is not a height in metres.");
                else
                    sentences.Add(UnreadableHeights.Count + " lines skipped: " + string.Join(", ", quoted) + " are not heights in metres.");
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

            if (markerNote != null) sentences.Add(markerNote);

            return string.Join(" ", sentences);
        }

        /// <summary>
        /// The marker's sentence, or null when there is no marker height or it rounds to 0 at the
        /// millimetre (-0 included). The rounded value is the one written: two decimals of the raw
        /// value would write a marker 3 mm off as +0.00, and net8 and net10 write a negative value
        /// that rounds to zero as "-+0.00".
        /// </summary>
        private string? SurveyPointMarkerNote()
        {
            if (SurveyPointMarkerMetres == null) return null;

            double r = Math.Round(SurveyPointMarkerMetres.Value, 3, MidpointRounding.AwayFromZero);
            if (r == 0) return null;

            return "The Survey Point marker itself sits at " +
                r.ToString("+0.00#;-0.00#", CultureInfo.InvariantCulture) +
                " m; heights are measured like a Survey Point level.";
        }

        private static string Count(int n, string singular, string plural) =>
            n + " " + (n == 1 ? singular : plural);
    }
}
