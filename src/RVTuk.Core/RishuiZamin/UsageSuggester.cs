using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>Suggests usage codes from area names, for the picker's "Suggested from the name"
    /// list. Suggestions only — never applied automatically.</summary>
    public static class UsageSuggester
    {
        // Case-insensitive substrings → codes, in suggestion order. ממ"ד is listed with both a
        // plain quote and the Hebrew gershayim (U+05F4), since office names use either.
        private static readonly (string[] Keywords, int[] Codes)[] Rules =
        {
            (new[] { "מרפסת", "balcony" }, new[] { 30, 110, 250 }),
            (new[] { "חני", "park" }, new[] { 33, 112 }),
            (new[] { "מחסן", "אחסנ", "storage" }, new[] { 31, 115 }),
            (new[] { "ממ\"ד", "ממ״ד", "מרחב מוגן" }, new[] { 101 }),
            (new[] { "מבוא", "לובי", "מדרגות", "lobby", "stair" }, new[] { 105 }),
            (new[] { "מעלית", "lift", "elevator" }, new[] { 104 }),
            (new[] { "דירה", "מגורים", "apartment" }, new[] { 1 }),
            (new[] { "טכני", "משאבות", "pump", "technical" }, new[] { 108 }),
            (new[] { "מרתף", "basement" }, new[] { 111 }),
            (new[] { "מצלל", "pergola" }, new[] { 32, 255 }),
            (new[] { "בריכ", "pool" }, new[] { 256 }),
            (new[] { "משרד", "office" }, new[] { 7 }),
        };

        public static IReadOnlyList<int> Suggest(IEnumerable<string?> names, UsageKeySlot slot, int max = 4)
        {
            var offered = new HashSet<int>(UsageSearch.Offered(slot).Select(e => e.Code));
            var result = new List<int>();
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var (keywords, codes) in Rules)
                {
                    if (!keywords.Any(k => name!.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    foreach (var code in codes)
                    {
                        if (result.Count >= max) return result;
                        if (offered.Contains(code) && !result.Contains(code)) result.Add(code);
                    }
                }
            }

            return result;
        }
    }
}
