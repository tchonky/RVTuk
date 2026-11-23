using System.Linq;
using RVTuk.Core.RishuiZamin;
using Xunit;

namespace RVTuk.Core.Tests.RishuiZamin;

public class PlotLimitsTests
{
    private static AreaRecord Area(int code, double m2, bool under = false) =>
        new AreaRecord { Floor = under ? "מרתף Basement" : "קומת קרקע Ground", LevelElevation = under ? -4.9 : 0,
            IsUnderground = under, UsageCode = code, AreaValue = m2 };

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void TryParse_EmptyIsNull(string? text)
    {
        Assert.True(PlotLimits.TryParseValue(text, out var v));
        Assert.Null(v);
    }

    [Theory]
    [InlineData("2,370.00", 2370)]
    [InlineData("2370", 2370)]
    [InlineData("35%", 35)]
    [InlineData(" 35.5 ", 35.5)]
    [InlineData("1,234,567", 1234567)]
    public void TryParse_AcceptsThousandsAndPercentSign(string text, double expected)
    {
        Assert.True(PlotLimits.TryParseValue(text, out var v));
        Assert.Equal(expected, v!.Value, 9);
    }

    [Theory]
    [InlineData("35,5")]
    [InlineData("1,23")]
    [InlineData("1,2345")]
    public void TryParse_RejectsCommaAsDecimal(string text) => Assert.False(PlotLimits.TryParseValue(text, out _));

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("35%%")]
    public void TryParse_RejectsNegativeAndJunk(string text) => Assert.False(PlotLimits.TryParseValue(text, out _));

    [Fact]
    public void UsedPercent_NullWithoutPositivePlot()
    {
        Assert.Null(PlotLimitCheck.UsedPercent(100, null));
        Assert.Null(PlotLimitCheck.UsedPercent(100, 0));
        Assert.Null(PlotLimitCheck.UsedPercent(100, -5));
        Assert.Equal(32.0999, PlotLimitCheck.UsedPercent(760.77, 2370)!.Value, 3);
    }

    [Fact]
    public void Status_Thresholds()
    {
        Assert.Equal(LimitStatus.Ok, PlotLimitCheck.Status(30, 35));
        Assert.Equal(LimitStatus.Close, PlotLimitCheck.Status(33.25, 35));
        Assert.Equal(LimitStatus.Close, PlotLimitCheck.Status(35, 35));
        Assert.Equal(LimitStatus.Over, PlotLimitCheck.Status(35.01, 35));
        Assert.Equal(LimitStatus.NotChecked, PlotLimitCheck.Status(30, null));
        Assert.Equal(LimitStatus.NotChecked, PlotLimitCheck.Status(null, 35));
    }

    [Fact]
    public void Chips_TextForms()
    {
        var table = AreaTable.Build(new[] { Area(1, 760.77), Area(105, 388.68) });
        var chips = PlotLimitCheck.Chips(table, new PlotLimits(2370, 35, null, null));

        Assert.Equal(3, chips.Count);
        Assert.Equal(new PlotLimitChip("Primary 32.1% / 35%", LimitStatus.Ok), chips[0]);
        Assert.Equal(new PlotLimitChip("Service 16.4%", LimitStatus.NotChecked), chips[1]);
        Assert.StartsWith("Total ", chips[2].Text);

        var noPlot = PlotLimitCheck.Chips(table, new PlotLimits(null, 35, 20, 55));
        Assert.Equal("Total —", noPlot[2].Text);
        Assert.All(noPlot, c => Assert.Equal(LimitStatus.NotChecked, c.Status));
    }

    [Fact]
    public void Chips_UseBuildingRow_BasementCounts()
    {
        var limits = new PlotLimits(2370, null, 20, null);

        var close = PlotLimitCheck.Chips(AreaTable.Build(new[] { Area(111, 474, under: true) }), limits);
        Assert.Equal(new PlotLimitChip("Service 20.0% / 20%", LimitStatus.Close), close[1]);

        var over = PlotLimitCheck.Chips(AreaTable.Build(new[] { Area(111, 480, under: true) }), limits);
        Assert.Equal(LimitStatus.Over, over[1].Status);
    }
}
