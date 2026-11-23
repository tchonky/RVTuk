using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What the Topo Edit panel's Height box shows for the height texts of the selected lines:
    /// their height when they all read as the same one ("42.7" and "42.70" do), and nothing when
    /// none is selected, any text is missing or unreadable, or the heights differ.
    /// </summary>
    public static class HeightBoxText
    {
        /// <summary>
        /// Whether the box may show the selection's height over what it holds now: only when it is
        /// empty or holds what it last showed (<paramref name="lastShown"/>). Anything else was
        /// typed, and stays — after Enter too — so Enter and Pick can use it again.
        /// </summary>
        public static bool Follows(string? current, string lastShown)
        {
            var text = (current ?? "").Trim();
            return text.Length == 0 || text == lastShown;
        }

        public static string For(IEnumerable<string?> labels)
        {
            string? shared = null;
            foreach (var label in labels)
            {
                if (!SurveyHeightText.TryParse(label, out double feet)) return "";
                var text = SurveyHeightText.Format(feet);
                if (shared != null && shared != text) return "";
                shared = text;
            }
            return shared ?? "";
        }
    }
}
