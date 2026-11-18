using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// The height of a topo line as its height text shows it: metres from the Survey Point, written
    /// and read the same way in every project, whatever units the project uses. Revit keeps heights
    /// in feet, so both directions take or give the shared height in feet.
    ///
    /// Reading takes a height the way people type it: with a comma for the point, a trailing "m",
    /// a minus sign (U+2212), or the invisible direction marks that Hebrew text leaves around a
    /// number. Anything else is refused rather than guessed at, and so is a height outside
    /// <see cref="LowestMetres"/> to <see cref="HighestMetres"/>: that catches a forgotten point on
    /// a site above 30 m.
    /// </summary>
    public static class SurveyHeightText
    {
        public const double MetresPerFoot = 0.3048;
        public const double LowestMetres = -500;
        public const double HighestMetres = 3000;

        /// <summary>
        /// The shared height in metres, rounded to six decimals and written with two to six of them,
        /// a point and no grouping: "42.70", "42.755", "-3.20".
        /// </summary>
        public static string Format(double sharedFeet)
        {
            double m = Math.Round(sharedFeet * MetresPerFoot, 6, MidpointRounding.AwayFromZero);
            if (m == 0) m = 0; // folds -0, which net8 and net10 write as "-0.00"
            return m.ToString("0.00####", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a height in metres into the shared height in feet. False, with
        /// <paramref name="sharedFeet"/> 0, for anything that is not one height in metres from
        /// <see cref="LowestMetres"/> to <see cref="HighestMetres"/>.
        /// </summary>
        public static bool TryParse(string? text, out double sharedFeet)
        {
            sharedFeet = 0;
            if (text == null) return false;

            // The marks go first, so a mark after the "m" does not hide it.
            var kept = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (IsInvisible(c)) continue;
                kept.Append(c == '\u2212' ? '-' : c == '\u00A0' ? ' ' : c);
            }
            string s = kept.ToString().Trim();

            // One "m" only, so "42.75 mm" stays unreadable instead of becoming 42.75 m.
            if (s.Length > 0 && (s[s.Length - 1] == 'm' || s[s.Length - 1] == 'M'))
                s = s.Substring(0, s.Length - 1).Trim();

            // One separator at most, and a comma is the point: "1,050" is 1.05 m.
            if (s.Count(c => c == ',' || c == '.') > 1) return false;
            s = s.Replace(',', '.');

            // "NaN" and "Infinity" parse even with these styles, and NaN would pass the range check.
            if (!double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out double m)
                || double.IsNaN(m) || double.IsInfinity(m))
                return false;

            if (m < LowestMetres || m > HighestMetres) return false;

            sharedFeet = m / MetresPerFoot;
            return true;
        }

        /// <summary>
        /// Dropped before reading: the direction marks (LRM, RLM, ALM), embeddings and overrides
        /// (LRE to RLO) and isolates (LRI to PDI) that Hebrew text leaves around a number, and the
        /// zero-width space and BOM that copied text can carry.
        /// </summary>
        private static bool IsInvisible(char c) =>
            c == '\u200E' || c == '\u200F' || c == '\u061C'
            || (c >= '\u202A' && c <= '\u202E')
            || (c >= '\u2066' && c <= '\u2069')
            || c == '\u200B' || c == '\uFEFF';
    }
}
