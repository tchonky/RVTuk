using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// Resolves free-form text (the content of a Revit *text* parameter — the office template
    /// stores every robot field as a separate text parameter) into a Rishui Zamin usage code.
    /// Handles the junk real text values carry: RTL/LTR/bidi control marks, non-breaking
    /// spaces, stray whitespace, decimal renderings of integers ("103.0"), and Hebrew names
    /// whose dashes/spacing differ from the catalog's.
    /// </summary>
    public static class UsageCodeParser
    {
        /// <summary>
        /// Resolves <paramref name="text"/> to a usage code, or null when it holds nothing
        /// usable. A bare integer (or an integer-valued decimal) parses directly — code 0 is
        /// Revit's "never filled in" default and reads as unset. Anything non-numeric is
        /// matched against the catalog's Hebrew names, ignoring dash/whitespace differences.
        /// With <paramref name="requireCatalog"/> a numeric value is accepted only when it is
        /// a real catalog code — use for *Name/label parameters, where arbitrary numbers
        /// (observed: values derived from the area number) must never masquerade as codes.
        /// </summary>
        public static int? Resolve(string? text, bool requireCatalog)
        {
            var clean = StripBidiAndTrim(text);
            if (clean.Length == 0)
            {
                return null;
            }

            if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return Accept(parsed, requireCatalog);
            }

            // A numeric text parameter frequently carries "103.0"/"103.00" (formulas, copy
            // paste from schedules). Accept only integer-valued decimals — "103.4" is garbage,
            // not a code.
            if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDouble))
            {
                var rounded = (int)Math.Round(asDouble);
                return Math.Abs(asDouble - rounded) < 1e-6 ? Accept(rounded, requireCatalog) : null;
            }

            var name = NormalizeName(clean);
            return UsageCatalog.All.FirstOrDefault(e => NormalizeName(e.HebrewName) == name)?.Code;
        }

        private static int? Accept(int code, bool requireCatalog)
        {
            if (code == 0)
            {
                return null;
            }

            return requireCatalog && !UsageCatalog.IsValidCode(code) ? (int?)null : code;
        }

        /// <summary>Removes bidi control characters (LRM/RLM, embeddings, isolates) and
        /// converts non-breaking spaces to plain ones, then trims.</summary>
        private static string StripBidiAndTrim(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var sb = new StringBuilder(text!.Length);
            foreach (var c in text)
            {
                if (c == '\u200E' || c == '\u200F' ||          // LRM / RLM
                    (c >= '\u202A' && c <= '\u202E') ||        // bidi embeddings/overrides
                    (c >= '\u2066' && c <= '\u2069'))          // bidi isolates
                {
                    continue;
                }

                sb.Append(c == '\u00A0' ? ' ' : c);            // NBSP -> space
            }

            return sb.ToString().Trim();
        }

        /// <summary>Canonical form for Hebrew usage-name comparison: bidi marks stripped, all
        /// dash variants (hyphen/en/em, maqaf) unified, whitespace runs collapsed.</summary>
        private static string NormalizeName(string text)
        {
            var stripped = StripBidiAndTrim(text);
            var sb = new StringBuilder(stripped.Length);
            var lastWasSpace = false;
            foreach (var c in stripped)
            {
                var mapped = c switch
                {
                    '–' or '—' or '־' => '-',    // en dash / em dash / maqaf
                    _ when char.IsWhiteSpace(c) => ' ',
                    _ => c,
                };

                if (mapped == ' ')
                {
                    if (lastWasSpace)
                    {
                        continue;
                    }

                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }

                sb.Append(mapped);
            }

            return sb.ToString();
        }
    }
}
