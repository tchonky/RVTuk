using System.Collections.Generic;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>The group an area is counted and coloured under in the Area Calc window: one of
    /// the four <see cref="UsageKind"/> sections, or <see cref="NoCode"/>.</summary>
    public enum UsageGroup
    {
        Primary,
        Service,
        Other,
        Process,
        NoCode
    }

    public static class UsageGroups
    {
        /// <summary>The group of a single code; null or a code missing from
        /// <see cref="UsageCatalog"/> is <see cref="UsageGroup.NoCode"/>.</summary>
        public static UsageGroup OfCode(int? code)
        {
            var entry = code == null ? null : UsageCatalog.ByCode(code.Value);
            return entry == null ? UsageGroup.NoCode : (UsageGroup)(int)entry.Kind;
        }

        /// <summary>The area's New usage group, else its Old usage group, else
        /// <see cref="UsageGroup.NoCode"/>.</summary>
        public static UsageGroup Of(AreaRecord a) => OfCode(EffectiveCode(a));

        /// <summary>The code an area is counted under: its New code if it is in
        /// <see cref="UsageCatalog"/>, else its Old code if that is, else null.</summary>
        public static int? EffectiveCode(AreaRecord a)
        {
            if (a.UsageCode is { } n && UsageCatalog.ByCode(n) != null) return n;
            if (a.UsageCodePrev is { } o && UsageCatalog.ByCode(o) != null) return o;
            return null;
        }
    }

    /// <summary>m² per usage group. <see cref="Total"/> is Primary + Service: Other is outside a
    /// floor's area summary, Process marks demolition/excavation, and uncoded areas have no group
    /// yet.</summary>
    public sealed record UsageTotals(double Primary, double Service, double Other, double Process,
        double NoCode, int NoCodeCount)
    {
        public double Total => Primary + Service;

        public static UsageTotals Sum(IEnumerable<AreaRecord> areas)
        {
            double p = 0, s = 0, o = 0, x = 0, nc = 0;
            var ncCount = 0;
            foreach (var a in areas)
            {
                switch (UsageGroups.Of(a))
                {
                    case UsageGroup.Primary: p += a.AreaValue; break;
                    case UsageGroup.Service: s += a.AreaValue; break;
                    case UsageGroup.Other: o += a.AreaValue; break;
                    case UsageGroup.Process: x += a.AreaValue; break;
                    default: nc += a.AreaValue; ncCount++; break;
                }
            }

            return new UsageTotals(p, s, o, x, nc, ncCount);
        }
    }
}
