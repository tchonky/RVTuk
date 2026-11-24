using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.RishuiZamin
{
    public enum AreaTableColumnKind
    {
        /// <summary>m² under one usage code.</summary>
        Code,
        /// <summary>The group's m² (for <see cref="UsageGroup.NoCode"/>: the uncoded m²).</summary>
        Sum,
        /// <summary>The group's m² as % of the plot area.</summary>
        Percent
    }

    /// <summary>One column of the area table. <see cref="Code"/> is set only for
    /// <see cref="AreaTableColumnKind.Code"/>.</summary>
    public sealed record AreaTableColumn(UsageGroup Group, AreaTableColumnKind Kind, int? Code);

    public enum AreaTableRowKind
    {
        Floor,
        AboveGround,
        BelowGround,
        Building
    }

    /// <summary>One row of the area table: a floor, or a sum of floors.</summary>
    public sealed class AreaTableRow
    {
        internal AreaTableRow(AreaTableRowKind kind, string label, double? elevation, bool isUnderground,
            IReadOnlyList<AreaRecord> areas)
        {
            Kind = kind;
            Label = label;
            Elevation = elevation;
            IsUnderground = isUnderground;
            AreaCount = areas.Count;
            Totals = UsageTotals.Sum(areas);
            var byCode = new Dictionary<int, double>();
            foreach (var a in areas)
            {
                if (UsageGroups.EffectiveCode(a) is not { } code) continue;
                byCode.TryGetValue(code, out var m2);
                byCode[code] = m2 + a.AreaValue;
            }
            ByCode = byCode;
        }

        public AreaTableRowKind Kind { get; }
        public string Label { get; }
        /// <summary>The floor's level elevation in metres; null for summary rows.</summary>
        public double? Elevation { get; }
        public bool IsUnderground { get; }
        public int AreaCount { get; }
        /// <summary>m² per effective code; uncoded areas are only in <see cref="Totals"/>.</summary>
        public IReadOnlyDictionary<int, double> ByCode { get; }
        public UsageTotals Totals { get; }
    }

    /// <summary>
    /// The Area Calc window's Table tab: one row per floor (by elevation, ascending), then Above
    /// ground / Below ground / Building; one column per usage code in use, grouped as Primary
    /// (codes, Σ, %), Service (codes, Σ, %), Other (codes, Σ), No code (only when some area has
    /// none) and the removal codes 300–302. Codes follow <see cref="UsageGroups.EffectiveCode"/>,
    /// so the table agrees with the floor and sheet totals.
    /// </summary>
    public sealed class AreaTable
    {
        public const string NoLevelLabel = "(ללא מפלס)";
        private const string Dash = "—";

        public static readonly AreaTable Empty = Build(new AreaRecord[0]);

        private AreaTable(IReadOnlyList<AreaTableColumn> columns, IReadOnlyList<AreaTableRow> rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public IReadOnlyList<AreaTableColumn> Columns { get; }
        public IReadOnlyList<AreaTableRow> Rows { get; }

        public static AreaTable Build(IEnumerable<AreaRecord> areas)
        {
            var list = areas.ToList();

            var floors = list
                .GroupBy(a => a.Floor ?? string.Empty)
                .OrderBy(g => g.First().LevelElevation)
                .Select(g => new AreaTableRow(AreaTableRowKind.Floor,
                    string.IsNullOrEmpty(g.Key) ? NoLevelLabel : g.Key,
                    g.First().LevelElevation, g.First().IsUnderground, g.ToList()))
                .ToList();

            var rows = new List<AreaTableRow>(floors)
            {
                new(AreaTableRowKind.AboveGround, "סה\"כ מעל הקרקע", null, false, list.Where(a => !a.IsUnderground).ToList()),
                new(AreaTableRowKind.BelowGround, "סה\"כ מתחת לקרקע", null, true, list.Where(a => a.IsUnderground).ToList()),
                new(AreaTableRowKind.Building, "סה\"כ בבניין", null, false, list),
            };

            var used = new HashSet<int>(list.Select(UsageGroups.EffectiveCode).Where(c => c != null).Select(c => c!.Value));
            var columns = new List<AreaTableColumn>();
            void Codes(UsageKind kind, UsageGroup group)
            {
                foreach (var e in UsageCatalog.All)
                    if (e.Kind == kind && used.Contains(e.Code))
                        columns.Add(new AreaTableColumn(group, AreaTableColumnKind.Code, e.Code));
            }

            Codes(UsageKind.Primary, UsageGroup.Primary);
            columns.Add(new AreaTableColumn(UsageGroup.Primary, AreaTableColumnKind.Sum, null));
            columns.Add(new AreaTableColumn(UsageGroup.Primary, AreaTableColumnKind.Percent, null));
            Codes(UsageKind.Service, UsageGroup.Service);
            columns.Add(new AreaTableColumn(UsageGroup.Service, AreaTableColumnKind.Sum, null));
            columns.Add(new AreaTableColumn(UsageGroup.Service, AreaTableColumnKind.Percent, null));
            Codes(UsageKind.Other, UsageGroup.Other);
            columns.Add(new AreaTableColumn(UsageGroup.Other, AreaTableColumnKind.Sum, null));
            if (list.Any(a => UsageGroups.EffectiveCode(a) == null))
                columns.Add(new AreaTableColumn(UsageGroup.NoCode, AreaTableColumnKind.Sum, null));
            Codes(UsageKind.Process, UsageGroup.Process);

            return new AreaTable(columns, rows);
        }

        /// <summary>The row's cells, one per <see cref="Columns"/> entry: m² as "F2" and % of
        /// <paramref name="plotAreaM2"/> as "F1", both empty when the m² is zero; % is "—" without
        /// a positive plot area.
        /// A summary row with no areas is all "—".</summary>
        public IReadOnlyList<string> Format(AreaTableRow row, double? plotAreaM2)
        {
            var cells = new string[Columns.Count];
            var emptySummary = row.Kind != AreaTableRowKind.Floor && row.AreaCount == 0;
            for (var i = 0; i < cells.Length; i++)
            {
                if (emptySummary) { cells[i] = Dash; continue; }
                var c = Columns[i];
                switch (c.Kind)
                {
                    case AreaTableColumnKind.Code:
                        cells[i] = M2(row.ByCode.TryGetValue(c.Code!.Value, out var m2) ? m2 : 0);
                        break;
                    case AreaTableColumnKind.Sum:
                        cells[i] = M2(GroupM2(row.Totals, c.Group));
                        break;
                    default:
                        var groupM2 = GroupM2(row.Totals, c.Group);
                        cells[i] = plotAreaM2 is not { } plot || plot <= 0 ? Dash
                            : groupM2 == 0 ? ""
                            : (groupM2 * 100 / plot).ToString("F1", CultureInfo.InvariantCulture);
                        break;
                }
            }
            return cells;
        }

        internal static double GroupM2(UsageTotals t, UsageGroup g) => g switch
        {
            UsageGroup.Primary => t.Primary,
            UsageGroup.Service => t.Service,
            UsageGroup.Other => t.Other,
            UsageGroup.Process => t.Process,
            _ => t.NoCode,
        };

        private static string M2(double m2) => m2 == 0 ? "" : m2.ToString("F2", CultureInfo.InvariantCulture);
    }
}
