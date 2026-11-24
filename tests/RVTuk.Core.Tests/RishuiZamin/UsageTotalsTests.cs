using System.Linq;
using RVTuk.Core.RishuiZamin;
using Xunit;

namespace RVTuk.Core.Tests.RishuiZamin;

public class UsageTotalsTests
{
    private static AreaRecord Area(int? code, int? old, double m2 = 0) =>
        new AreaRecord { UsageCode = code, UsageCodePrev = old, AreaValue = m2 };

    [Fact]
    public void GroupOf_UsesNewCode() => Assert.Equal(UsageGroup.Primary, UsageGroups.Of(Area(1, 105)));

    [Fact]
    public void GroupOf_OldOnly_UsesOld() => Assert.Equal(UsageGroup.Service, UsageGroups.Of(Area(null, 105)));

    [Fact]
    public void GroupOf_UnknownNewCode_FallsBackToOld_ThenNoCode()
    {
        Assert.Equal(UsageGroup.Primary, UsageGroups.Of(Area(999, 30)));
        Assert.Equal(UsageGroup.NoCode, UsageGroups.Of(Area(3, null)));
    }

    [Fact]
    public void EffectiveCode_NewElseOldElseNull()
    {
        Assert.Equal(1, UsageGroups.EffectiveCode(Area(1, 105)));
        Assert.Equal(30, UsageGroups.EffectiveCode(Area(999, 30)));
        Assert.Equal(105, UsageGroups.EffectiveCode(Area(null, 105)));
        Assert.Null(UsageGroups.EffectiveCode(Area(3, null)));
        Assert.Null(UsageGroups.EffectiveCode(Area(null, null)));
    }

    [Fact]
    public void GroupOf_ProcessWithOld_IsProcess() => Assert.Equal(UsageGroup.Process, UsageGroups.Of(Area(301, 1)));

    [Fact]
    public void Sum_SplitsByGroup_TotalIsPrimaryPlusService()
    {
        var t = UsageTotals.Sum(new[]
        {
            Area(1, null, 84.30), Area(105, null, 22.00), Area(250, null, 10.00),
            Area(301, 1, 12.50), Area(null, null, 5.25),
        });

        Assert.Equal(84.30, t.Primary, 9);
        Assert.Equal(22.00, t.Service, 9);
        Assert.Equal(10.00, t.Other, 9);
        Assert.Equal(12.50, t.Process, 9);
        Assert.Equal(5.25, t.NoCode, 9);
        Assert.Equal(1, t.NoCodeCount);
        Assert.Equal(106.30, t.Total, 9);
    }

    [Fact]
    public void Sum_Empty_IsAllZero()
    {
        var t = UsageTotals.Sum(Enumerable.Empty<AreaRecord>());
        Assert.Equal(new UsageTotals(0, 0, 0, 0, 0, 0), t);
        Assert.Equal(0, t.Total);
    }
}
