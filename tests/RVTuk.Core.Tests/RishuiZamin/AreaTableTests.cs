using System.Linq;
using RVTuk.Core.RishuiZamin;
using Xunit;

namespace RVTuk.Core.Tests.RishuiZamin;

public class AreaTableTests
{
    private static AreaRecord Area(string floor, double elev, bool under, int? code, double m2, int? old = null) =>
        new AreaRecord { Floor = floor, LevelElevation = elev, IsUnderground = under, UsageCode = code, UsageCodePrev = old, AreaValue = m2 };

    private static AreaTableColumn C(UsageGroup g, AreaTableColumnKind k, int? code = null) => new AreaTableColumn(g, k, code);

    [Fact]
    public void Columns_UsedCodesInCatalogOrder_WithSumsAndPercents()
    {
        var t = AreaTable.Build(new[]
        {
            Area("קומת קרקע Ground", 0, false, 105, 10), Area("קומת קרקע Ground", 0, false, 1, 10),
            Area("קומת קרקע Ground", 0, false, 104, 10), Area("קומת קרקע Ground", 0, false, 252, 10),
            Area("קומת קרקע Ground", 0, false, 300, 10, old: 1),
        });

        Assert.Equal(new[]
        {
            C(UsageGroup.Primary, AreaTableColumnKind.Code, 1), C(UsageGroup.Primary, AreaTableColumnKind.Sum),
            C(UsageGroup.Primary, AreaTableColumnKind.Percent),
            C(UsageGroup.Service, AreaTableColumnKind.Code, 104), C(UsageGroup.Service, AreaTableColumnKind.Code, 105),
            C(UsageGroup.Service, AreaTableColumnKind.Sum), C(UsageGroup.Service, AreaTableColumnKind.Percent),
            C(UsageGroup.Other, AreaTableColumnKind.Code, 252), C(UsageGroup.Other, AreaTableColumnKind.Sum),
            C(UsageGroup.Process, AreaTableColumnKind.Code, 300),
        }, t.Columns);
    }

    [Fact]
    public void Columns_EmptyGroup_KeepsSumAndPercent()
    {
        var t = AreaTable.Build(new[] { Area("קומה א First", 4, false, 105, 10) });

        Assert.Equal(new[]
        {
            C(UsageGroup.Primary, AreaTableColumnKind.Sum), C(UsageGroup.Primary, AreaTableColumnKind.Percent),
            C(UsageGroup.Service, AreaTableColumnKind.Code, 105), C(UsageGroup.Service, AreaTableColumnKind.Sum),
            C(UsageGroup.Service, AreaTableColumnKind.Percent),
            C(UsageGroup.Other, AreaTableColumnKind.Sum),
        }, t.Columns);
    }

    [Fact]
    public void Columns_NoCodeColumn_OnlyWhenSomeAreaHasNone()
    {
        var with = AreaTable.Build(new[]
        {
            Area("קומה א First", 4, false, null, 5), Area("קומה א First", 4, false, 301, 3, old: 1),
        });
        var noCode = C(UsageGroup.NoCode, AreaTableColumnKind.Sum);
        var i = with.Columns.ToList().IndexOf(noCode);
        Assert.Equal(C(UsageGroup.Other, AreaTableColumnKind.Sum), with.Columns[i - 1]);
        Assert.Equal(C(UsageGroup.Process, AreaTableColumnKind.Code, 301), with.Columns[i + 1]);

        var without = AreaTable.Build(new[] { Area("קומה א First", 4, false, 1, 5) });
        Assert.DoesNotContain(noCode, without.Columns);
    }

    [Fact]
    public void Columns_UseEffectiveCode()
    {
        var t = AreaTable.Build(new[] { Area("קומה א First", 4, false, 999, 5, old: 30) });
        Assert.Contains(C(UsageGroup.Primary, AreaTableColumnKind.Code, 30), t.Columns);
        Assert.DoesNotContain(t.Columns, c => c.Code == 999);
    }

    [Fact]
    public void Rows_FloorsByElevationAscending_ThenSummaries()
    {
        var t = AreaTable.Build(new[]
        {
            Area("קומה א First", 4.00, false, 1, 5), Area("מרתף Basement", -4.90, true, 111, 5),
            Area("קומת קרקע Ground", 0.00, false, 1, 5),
        });

        Assert.Equal(new[] { "מרתף Basement", "קומת קרקע Ground", "קומה א First" },
            t.Rows.Where(r => r.Kind == AreaTableRowKind.Floor).Select(r => r.Label));
        Assert.Equal(new[] { -4.90, 0.00, 4.00 },
            t.Rows.Where(r => r.Kind == AreaTableRowKind.Floor).Select(r => r.Elevation!.Value));
        Assert.Equal(new[] { AreaTableRowKind.AboveGround, AreaTableRowKind.BelowGround, AreaTableRowKind.Building },
            t.Rows.Skip(3).Select(r => r.Kind));
    }

    [Fact]
    public void Rows_SummariesSplitByUnderground()
    {
        var t = AreaTable.Build(new[]
        {
            Area("מרתף Basement", -4.90, true, 111, 24.40), Area("קומת קרקע Ground", 0, false, 1, 100),
            Area("קומה א First", 4, false, 105, 50),
        });
        var above = t.Rows.Single(r => r.Kind == AreaTableRowKind.AboveGround);
        var below = t.Rows.Single(r => r.Kind == AreaTableRowKind.BelowGround);
        var all = t.Rows.Single(r => r.Kind == AreaTableRowKind.Building);

        Assert.Equal((100, 50), (above.Totals.Primary, above.Totals.Service));
        Assert.Equal((0, 24.40), (below.Totals.Primary, below.Totals.Service));
        Assert.Equal(100, all.Totals.Primary, 9);
        Assert.Equal(74.40, all.Totals.Service, 9);
        Assert.Equal((2, 1, 3), (above.AreaCount, below.AreaCount, all.AreaCount));
        Assert.True(t.Rows.Single(r => r.Label == "מרתף Basement").IsUnderground);
        Assert.Equal(24.40, below.ByCode[111], 9);
    }

    [Fact]
    public void Building_MatchesUsageTotalsSum()
    {
        var areas = new[]
        {
            Area("מרתף Basement", -4.90, true, 111, 24.40), Area("קומת קרקע Ground", 0, false, 1, 100.25),
            Area("קומת קרקע Ground", 0, false, 252, 7.5), Area("קומה א First", 4, false, 300, 11.04, old: 1),
            Area("קומה א First", 4, false, null, 3.3), Area("קומה א First", 4, false, 999, 2, old: 105),
        };
        var t = AreaTable.Build(areas);
        Assert.Equal(UsageTotals.Sum(areas), t.Rows.Single(r => r.Kind == AreaTableRowKind.Building).Totals);
    }

    [Fact]
    public void Rows_EmptyFloorName_ShowsNoLevel()
    {
        var t = AreaTable.Build(new[] { Area("", 0, false, 1, 5) });
        Assert.Equal("(no level)", t.Rows.First().Label);
    }

    [Fact]
    public void Format_M2TwoDecimals_EmptyWhenZero()
    {
        var t = AreaTable.Build(new[]
        {
            Area("קומת קרקע Ground", 0, false, 12, 1437.94), Area("קומה א First", 4, false, 104, 11.04),
        });
        var ground = t.Rows.Single(r => r.Label == "קומת קרקע Ground");
        var cells = t.Format(ground, 2370);
        var cols = t.Columns.ToList();
        Assert.Equal("1437.94", cells[cols.IndexOf(C(UsageGroup.Primary, AreaTableColumnKind.Code, 12))]);
        Assert.Equal("", cells[cols.IndexOf(C(UsageGroup.Service, AreaTableColumnKind.Code, 104))]);
    }

    [Fact]
    public void Format_Percent_OneDecimal_DashWithoutPlot()
    {
        var t = AreaTable.Build(new[] { Area("קומת קרקע Ground", 0, false, 1, 760.77) });
        var row = t.Rows.Single(r => r.Kind == AreaTableRowKind.Building);
        var pct = t.Columns.ToList().IndexOf(C(UsageGroup.Primary, AreaTableColumnKind.Percent));

        Assert.Equal("32.1", t.Format(row, 2370)[pct]);
        Assert.Equal("—", t.Format(row, null)[pct]);
        Assert.Equal("—", t.Format(row, 0)[pct]);
    }

    [Fact]
    public void Format_PercentEmptyWhenGroupIsZero()
    {
        var t = AreaTable.Build(new[] { Area("מרתף Basement", -4.90, true, 105, 24.40), Area("קומת קרקע Ground", 0, false, 1, 5) });
        var basement = t.Rows.Single(r => r.Label == "מרתף Basement");
        var pct = t.Columns.ToList().IndexOf(C(UsageGroup.Primary, AreaTableColumnKind.Percent));
        Assert.Equal("", t.Format(basement, 2370)[pct]);
    }

    [Fact]
    public void Format_EmptySummaryRow_IsDashes()
    {
        var t = AreaTable.Build(new[] { Area("קומת קרקע Ground", 0, false, 1, 5), Area("קומת קרקע Ground", 0, false, 105, 5) });
        var below = t.Rows.Single(r => r.Kind == AreaTableRowKind.BelowGround);
        Assert.All(t.Format(below, 2370), c => Assert.Equal("—", c));
    }

    [Fact]
    public void Format_LengthMatchesColumns()
    {
        var t = AreaTable.Build(new[]
        {
            Area("מרתף Basement", -4.90, true, 111, 24.40), Area("קומת קרקע Ground", 0, false, 1, 100),
            Area("קומה א First", 4, false, null, 3.3), Area("קומה א First", 4, false, 302, 2, old: 1),
        });
        Assert.All(t.Rows, r => Assert.Equal(t.Columns.Count, t.Format(r, 2370).Count));
    }
}
