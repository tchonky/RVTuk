using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>The Area Calc usage picker's search: digits filter by code prefix, other text by
    /// a substring of the Hebrew name or the start of a group's English name.</summary>
    public static class UsageSearch
    {
        /// <summary>The entries a slot offers: Old usage never offers the process codes.</summary>
        public static IEnumerable<UsageEntry> Offered(UsageKeySlot slot) =>
            UsageCatalog.All.Where(e => slot == UsageKeySlot.New || e.Kind != UsageKind.Process);

        /// <summary>Hebrew gershayim (U+05F4) and geresh (U+05F3) typed as their ASCII
        /// look-alikes, so ממ״ד finds the catalog's ממ"ד.</summary>
        private static string Normalize(string s) => s.Replace('״', '"').Replace('׳', '\'');

        public static IReadOnlyList<UsageEntry> Filter(string? query, UsageKeySlot slot)
        {
            var q = Normalize((query ?? "").Trim());
            var offered = Offered(slot);
            if (q.Length == 0)
            {
                return offered.ToList();
            }

            if (q.All(char.IsDigit))
            {
                return offered
                    .Where(e => e.Code.ToString(CultureInfo.InvariantCulture).StartsWith(q, StringComparison.Ordinal))
                    .ToList();
            }

            return offered
                .Where(e => Normalize(e.HebrewName).IndexOf(q, StringComparison.Ordinal) >= 0
                         || e.Kind.ToString().StartsWith(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
