using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What one ⇄ run did, as its summary dialog says it. Kept in Core, and tested, as the only
    /// text the converter shows every time.
    ///
    /// Skip entries name elements by category and id ("Floor 418732"), never by type name: the
    /// summary is left-to-right text, and a Hebrew type name there would drag the punctuation
    /// around it into its own run.
    /// </summary>
    public sealed class ConvertReport
    {
        public const string NothingSelected = "Select floors or toposolids first.";
        public const string Cancelled = "Cancelled — nothing converted.";

        public int FloorsToToposolids { get; set; }
        public int ToposolidsToFloors { get; set; }
        public List<string> Skipped { get; } = new List<string>();
        public int PointsLost { get; set; }
        public int CreasesLost { get; set; }
        public int ParametersNotCarried { get; set; }
        public int WarningsDismissed { get; set; }

        public string Summarise()
        {
            var sentences = new List<string>();

            var converted = new List<string>();
            if (FloorsToToposolids > 0)
                converted.Add(Count(FloorsToToposolids, "floor → toposolid", "floors → toposolids"));
            if (ToposolidsToFloors > 0)
                converted.Add(Count(ToposolidsToFloors, "toposolid → floor", "toposolids → floors"));
            sentences.Add(converted.Count > 0 ? string.Join(", ", converted) + "." : "Nothing converted.");

            if (Skipped.Count > 0)
                sentences.Add($"Skipped {Skipped.Count}: {string.Join("; ", Skipped.Select(Sentence.WithoutFullStop))}.");

            var lost = new List<string>();
            if (PointsLost > 0) lost.Add(Count(PointsLost, "shape point", "shape points"));
            if (CreasesLost > 0) lost.Add(Count(CreasesLost, "split line", "split lines"));
            if (lost.Count > 0) sentences.Add(string.Join(" and ", lost) + " could not be copied.");

            if (ParametersNotCarried > 0)
                sentences.Add(Count(ParametersNotCarried, "parameter value", "parameter values") + " not carried.");
            if (WarningsDismissed > 0)
                sentences.Add(Count(WarningsDismissed, "Revit warning", "Revit warnings") + " dismissed.");

            return string.Join(" ", sentences);
        }

        /// <summary>
        /// "2 hosted elements, 3 tags or dimensions and 1 subdivision". Only the counts that are
        /// not zero; "" when all are.
        /// </summary>
        /// <param name="parts">Parts divided from the element (Modify → Create Parts).</param>
        public static string DescribeDependents(int hosted, int annotations, int subdivisions, int parts)
        {
            var items = new List<string>();
            if (hosted > 0) items.Add(Count(hosted, "hosted element", "hosted elements"));
            if (annotations > 0) items.Add(Count(annotations, "tag or dimension", "tags or dimensions"));
            if (subdivisions > 0) items.Add(Count(subdivisions, "subdivision", "subdivisions"));
            if (parts > 0) items.Add(Count(parts, "part", "parts"));

            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
        }

        private static string Count(int n, string singular, string plural) =>
            n + " " + (n == 1 ? singular : plural);
    }
}
