namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What was typed in the Topo Edit panel's Height box. A plain height ("43", "43,25 m") sets
    /// every selected line to it; a signed number moves each line from its own height ("+0.5"
    /// raises by half a metre, "-0.5" or "−0.5" lowers); "=" before a height sets it, which is how
    /// a height below zero is typed ("=-3.20").
    /// </summary>
    public sealed class HeightBoxInput
    {
        private readonly double _feet;

        private HeightBoxInput(bool isAbsolute, double feet)
        {
            IsAbsolute = isAbsolute;
            _feet = feet;
        }

        /// <summary>True for a height to set, false for a move from each line's own height.</summary>
        public bool IsAbsolute { get; }

        public static bool TryParse(string? typed, out HeightBoxInput input)
        {
            input = null!;
            var s = (typed ?? "").Trim();
            if (s.Length == 0) return false;

            if (s[0] == '=')
            {
                var rest = s.Substring(1).Trim();
                if (rest.Length > 0 && rest[0] == '+') return false; // "=" takes a height, not a move
                if (!SurveyHeightText.TryParse(rest, out double height)) return false;
                input = new HeightBoxInput(true, height);
                return true;
            }

            bool signed = s[0] == '+' || s[0] == '-' || s[0] == '−';
            if (!SurveyHeightText.TryParse(s, out double feet)) return false;
            input = new HeightBoxInput(!signed, feet);
            return true;
        }

        /// <summary>
        /// The text a line's label takes, given its <paramref name="label"/> now. A move needs a
        /// readable label, and a result a label may hold; otherwise null: that line is left alone.
        /// </summary>
        public string? NewLabel(string? label)
        {
            if (IsAbsolute) return SurveyHeightText.Format(_feet);
            if (!SurveyHeightText.TryParse(label, out double current)) return null;

            var text = SurveyHeightText.Format(current + _feet);
            return SurveyHeightText.TryParse(text, out _) ? text : null;
        }
    }
}
