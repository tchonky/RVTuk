using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RishuiZamin
{
    public enum UsageValueKind
    {
        None,
        Code,
        Mixed
    }

    /// <summary>The usage value shared by the selected areas: none, one code, or mixed.</summary>
    public sealed record UsageValue(UsageValueKind Kind, int? Code);

    /// <summary>What the Area Calc window's detail strip says about the selected areas.</summary>
    public sealed class AreaSelectionSummary
    {
        public string Title { get; private set; } = "";
        public double AreaM2 { get; private set; }
        public string? Detail { get; private set; }
        public UsageValue New { get; private set; } = NoValue;
        public UsageValue Old { get; private set; } = NoValue;

        private static readonly UsageValue NoValue = new UsageValue(UsageValueKind.None, null);

        /// <summary>"«number» — «name»", with "(no #)" for an area without a Number.</summary>
        public static string NumberAndName(string? number, string? name)
        {
            var head = string.IsNullOrWhiteSpace(number) ? "(no #)" : number!;
            return string.IsNullOrWhiteSpace(name) ? head : head + " — " + name;
        }

        public static AreaSelectionSummary From(IReadOnlyList<AreaRecord> areas)
        {
            var s = new AreaSelectionSummary
            {
                AreaM2 = areas.Sum(a => a.AreaValue),
                New = Shared(areas.Select(a => a.UsageCode)),
                Old = Shared(areas.Select(a => a.UsageCodePrev)),
            };

            if (areas.Count == 0)
            {
                s.Title = "Select one or more areas to set their usage.";
            }
            else if (areas.Count == 1)
            {
                var a = areas[0];
                s.Title = NumberAndName(a.Number, a.Name);
                s.Detail = ErrorWords(a.Errors);
            }
            else
            {
                s.Title = $"{areas.Count} areas selected";
                var uncoded = areas.Count(a => a.Errors.HasFlag(AreaError.NoUsageCode));
                s.Detail = uncoded > 0 ? $"⚠ {uncoded} without a usage code" : null;
            }

            return s;
        }

        private static UsageValue Shared(IEnumerable<int?> codes)
        {
            var distinct = codes.Distinct().ToList();
            if (distinct.Count > 1) return new UsageValue(UsageValueKind.Mixed, null);
            var only = distinct.FirstOrDefault();
            return only == null ? NoValue : new UsageValue(UsageValueKind.Code, only);
        }

        private static string? ErrorWords(AreaError errors)
        {
            var words = new List<string>();
            if (errors.HasFlag(AreaError.NoUsageCode)) words.Add("No usage code");
            if (errors.HasFlag(AreaError.BadBoundary)) words.Add("Boundary is not closed");
            if (errors.HasFlag(AreaError.ZeroArea)) words.Add("Area is zero");
            return words.Count == 0 ? null : string.Join(" · ", words);
        }
    }
}
