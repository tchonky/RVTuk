using System.Linq;
using RVTuk.Core.RishuiZamin;
using Xunit;

namespace RVTuk.Core.Tests.RishuiZamin;

public class UsageSearchTests
{
    [Fact]
    public void Empty_ReturnsAll_New()
    {
        Assert.Equal(UsageCatalog.All.Count, UsageSearch.Filter("", UsageKeySlot.New).Count);
        Assert.Equal(UsageCatalog.All.Count, UsageSearch.Filter(null, UsageKeySlot.New).Count);
    }

    [Fact]
    public void Empty_Old_HasNoProcess()
    {
        var old = UsageSearch.Filter("", UsageKeySlot.Old);
        Assert.DoesNotContain(old, e => e.Kind == UsageKind.Process);
        Assert.Equal(UsageCatalog.All.Count(e => e.Kind != UsageKind.Process), old.Count);
    }

    [Fact]
    public void Digits_FilterByCodePrefix()
    {
        var codes = UsageSearch.Filter("11", UsageKeySlot.New).Select(e => e.Code).ToArray();
        Assert.Equal(new[] { 11, 117, 118, 119, 110, 111, 112, 113, 114, 115, 116 }, codes);
    }

    [Fact]
    public void Text_MatchesHebrewSubstring()
    {
        Assert.Contains(UsageSearch.Filter("מרפסת", UsageKeySlot.New), e => e.Code == 30);
        Assert.Contains(UsageSearch.Filter("  מרפסת ", UsageKeySlot.New), e => e.Code == 30);
        Assert.DoesNotContain(UsageSearch.Filter("מרפסת", UsageKeySlot.New), e => e.Code == 1);
    }

    [Fact]
    public void GroupName_MatchesByPrefix_CaseInsensitive()
    {
        var serv = UsageSearch.Filter("serv", UsageKeySlot.New);
        Assert.NotEmpty(serv);
        Assert.All(serv, e => Assert.Equal(UsageKind.Service, e.Kind));
        Assert.Equal(UsageCatalog.All.Count(e => e.Kind == UsageKind.Service), serv.Count);

        Assert.Empty(UsageSearch.Filter("PROC", UsageKeySlot.Old));
        Assert.Equal(3, UsageSearch.Filter("PROC", UsageKeySlot.New).Count);
    }
}
