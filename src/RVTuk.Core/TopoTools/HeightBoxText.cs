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
