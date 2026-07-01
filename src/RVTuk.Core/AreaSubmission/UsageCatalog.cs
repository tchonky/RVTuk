using System.Collections.Generic;

#if REVIT2024
// net48 has no built-in System.Runtime.CompilerServices.IsExternalInit — the compiler
// needs this marker type to allow `init`-only members (and positional records, which
// generate them). net8 already ships it in the BCL.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// Broad grouping of a Rishui Zamin usage code, per the official
    /// "טבלת סוגי שימושים" (usage-type table). See docs/autoarea/rishui-zamin-notes.md §3.
    /// </summary>
    public enum UsageKind
    {
        /// <summary>שטחים עיקריים — primary areas.</summary>
        Primary,

        /// <summary>שטחי שירות — service areas.</summary>
        Service,

        /// <summary>אחר — other areas, not included in the floor's area summary.</summary>
        Other
    }

    /// <summary>One row of the Rishui Zamin usage-type table.</summary>
    /// <param name="Code">The numeric USAGE_TYPE code.</param>
    /// <param name="Kind">Which section of the table the code belongs to.</param>
    /// <param name="HebrewName">The Hebrew usage name, verbatim from the spec.</param>
    public record UsageEntry(int Code, UsageKind Kind, string HebrewName);

    /// <summary>
    /// The catalog of Rishui Zamin usage codes, transcribed verbatim from the official
    /// "טבלת סוגי שימושים" (usage-type table) quoted in docs/autoarea/rishui-zamin-notes.md §3.
    /// Covers the primary/service separation method only (codes 1-33, 101-130, 250-257);
    /// the "not coloured" process markers (300-302) and the alternate שטח-כולל code list
    /// are out of scope for this catalog. See the notes doc for gaps in the source table
    /// (codes 3, 5, 117-129, 251, 253, 254 are not listed there and are omitted here).
    /// </summary>
    public static class UsageCatalog
    {
        private static readonly List<UsageEntry> _all = new List<UsageEntry>
        {
            // שטחים עיקריים — Primary areas
            new UsageEntry(1, UsageKind.Primary, "מגורים"),
            new UsageEntry(2, UsageKind.Primary, "מסחר והסעדה"),
            new UsageEntry(4, UsageKind.Primary, "תעשייה ומלאכה"),
            new UsageEntry(6, UsageKind.Primary, "חקלאות"),
            new UsageEntry(7, UsageKind.Primary, "משרדים ותעשיות עתירות ידע"),
            new UsageEntry(8, UsageKind.Primary, "מלונאות ובתי אירוח אחרים"),
            new UsageEntry(9, UsageKind.Primary, "נופש וספורט"),
            new UsageEntry(10, UsageKind.Primary, "מבני ציבור ודת"),
            new UsageEntry(11, UsageKind.Primary, "פנאי ותרבות"),
            new UsageEntry(12, UsageKind.Primary, "מוסדות חינוך"),
            new UsageEntry(13, UsageKind.Primary, "מוסדות בריאות"),
            new UsageEntry(14, UsageKind.Primary, "מבני חרום וכליאה"),
            new UsageEntry(15, UsageKind.Primary, "מבני תחבורה, מבני דרך ותדלוק"),
            new UsageEntry(16, UsageKind.Primary, "מבנים טכניים, תשתיות ושמירה"),
            new UsageEntry(30, UsageKind.Primary, "מרפסת"),
            new UsageEntry(31, UsageKind.Primary, "אחסנה"),
            new UsageEntry(32, UsageKind.Primary, "מצללה"),
            new UsageEntry(33, UsageKind.Primary, "חניה"),

            // שטחי שירות — Service areas
            new UsageEntry(101, UsageKind.Service, "מרחב מוגן דירתי – שטח רצפה"),
            new UsageEntry(102, UsageKind.Service, "מרחב מוגן דירתי – שטח קירות"),
            new UsageEntry(103, UsageKind.Service, "מרחב מוגן קומתי / מוסדי / מקלט / מבנה שמירה"),
            new UsageEntry(104, UsageKind.Service, "מעלית"),
            new UsageEntry(105, UsageKind.Service, "מבואות וחדרי מדרגות"),
            new UsageEntry(106, UsageKind.Service, "קומת עמודים מפולשת ומקמרות"),
            new UsageEntry(107, UsageKind.Service, "מעברים לכלל הציבור"),
            new UsageEntry(108, UsageKind.Service, "מערכות טכניות ומבני שירות"),
            new UsageEntry(109, UsageKind.Service, "חדרי שירות משותפים"),
            new UsageEntry(110, UsageKind.Service, "מרפסת"),
            new UsageEntry(111, UsageKind.Service, "מרתף"),
            new UsageEntry(112, UsageKind.Service, "חניה"),
            new UsageEntry(113, UsageKind.Service, "עובי קירות"),
            new UsageEntry(114, UsageKind.Service, "בליטות, גגונים וקירוי"),
            new UsageEntry(115, UsageKind.Service, "אחסנה"),
            new UsageEntry(116, UsageKind.Service, "מבני שמירה"),
            new UsageEntry(130, UsageKind.Service, "אחר מתוקף תכנית"),

            // אחר — Other (NOT included in the floor's area summary)
            new UsageEntry(250, UsageKind.Other, "מרפסת זיזית"),
            new UsageEntry(252, UsageKind.Other, "שטח מרוצף לא מקורה"),
            new UsageEntry(255, UsageKind.Other, "מצללה"),
            new UsageEntry(256, UsageKind.Other, "בריכת שחיה"),
            new UsageEntry(257, UsageKind.Other, "בליטות, גגונים וקירוי"),
        };

        private static readonly Dictionary<int, UsageEntry> _byCode = BuildIndex();

        private static Dictionary<int, UsageEntry> BuildIndex()
        {
            var map = new Dictionary<int, UsageEntry>(_all.Count);
            foreach (var entry in _all)
            {
                map[entry.Code] = entry;
            }
            return map;
        }

        /// <summary>All catalog entries, in table order.</summary>
        public static IReadOnlyList<UsageEntry> All => _all;

        /// <summary>Looks up an entry by its numeric usage code, or null if unknown.</summary>
        public static UsageEntry? ByCode(int code)
        {
            return _byCode.TryGetValue(code, out var entry) ? entry : null;
        }

        /// <summary>True if the code exists in the catalog.</summary>
        public static bool IsValidCode(int code)
        {
            return _byCode.ContainsKey(code);
        }
    }
}
