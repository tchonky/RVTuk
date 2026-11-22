using System;
using RVTuk.Core.RishuiZamin;
using Xunit;

namespace RVTuk.Core.Tests.RishuiZamin;

public class AreaSelectionSummaryTests
{
    private static AreaRecord Area(string? number, string? name, double m2 = 0,
        AreaError errors = AreaError.None, int? code = null, int? old = null) =>
        new AreaRecord { Number = number, Name = name, AreaValue = m2, Errors = errors, UsageCode = code, UsageCodePrev = old };

    [Fact]
    public void None_Selected()
    {
        var s = AreaSelectionSummary.From(Array.Empty<AreaRecord>());
        Assert.Equal("Select one or more areas to set their usage.", s.Title);
        Assert.Equal(0, s.AreaM2);
        Assert.Null(s.Detail);
        Assert.Equal(UsageValueKind.None, s.New.Kind);
        Assert.Equal(UsageValueKind.None, s.Old.Kind);
    }

    [Fact]
    public void One_ShowsNumberNameAndErrorsInWords()
    {
        var s = AreaSelectionSummary.From(new[] { Area("002", "דירה 1 - Apt", 84.3, AreaError.NoUsageCode | AreaError.ZeroArea) });
        Assert.Equal("002 — דירה 1 - Apt", s.Title);
        Assert.Equal(84.3, s.AreaM2, 9);
        Assert.Equal("No usage code · Area is zero", s.Detail);

        var b = AreaSelectionSummary.From(new[] { Area("003", "Lobby מבואה", 1, AreaError.BadBoundary, code: 105) });
        Assert.Equal("Boundary is not closed", b.Detail);

        var noNumber = AreaSelectionSummary.From(new[] { Area(null, "Lobby מבואה") });
        Assert.StartsWith("(no #)", noNumber.Title);
        Assert.Equal("(no #) — Lobby מבואה", noNumber.Title);
    }

    [Fact]
    public void One_NoErrors_DetailNull()
    {
        var s = AreaSelectionSummary.From(new[] { Area("001", "Lobby מבואה", 22, code: 105) });
        Assert.Null(s.Detail);
        Assert.Equal(new UsageValue(UsageValueKind.Code, 105), s.New);
        Assert.Equal(new UsageValue(UsageValueKind.None, null), s.Old);
    }

    [Fact]
    public void Many_SumsAndCountsUncoded()
    {
        var s = AreaSelectionSummary.From(new[]
        {
            Area("1", "דירה 1 - Apt", 10.00, code: 1),
            Area("2", "Lobby מבואה", 20.50, AreaError.NoUsageCode),
            Area("3", "מחסן Storage", 1.25, code: 31),
        });
        Assert.Equal("3 areas selected", s.Title);
        Assert.Equal(31.75, s.AreaM2, 9);
        Assert.Equal("⚠ 1 without a usage code", s.Detail);
    }

    [Fact]
    public void Many_SameNew_IsCode_DifferentOld_IsMixed()
    {
        var s = AreaSelectionSummary.From(new[]
        {
            Area("1", "דירה 1 - Apt", code: 1, old: 105),
            Area("2", "דירה 2 - Apt", code: 1, old: null),
        });
        Assert.Equal(new UsageValue(UsageValueKind.Code, 1), s.New);
        Assert.Equal(new UsageValue(UsageValueKind.Mixed, null), s.Old);
        Assert.Null(s.Detail);
    }

    [Fact]
    public void Many_AllNull_IsNone()
    {
        var s = AreaSelectionSummary.From(new[] { Area("1", "a"), Area("2", "b") });
        Assert.Equal(UsageValueKind.None, s.New.Kind);
        Assert.Equal(UsageValueKind.None, s.Old.Kind);
    }
}
