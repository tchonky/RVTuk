using RVTuk.Core.RishuiZamin;
using Xunit;

namespace RVTuk.Core.Tests.RishuiZamin;

public class UsageSuggesterTests
{
    [Fact]
    public void Suggest_Balcony() =>
        Assert.Equal(new[] { 30, 110, 250 }, UsageSuggester.Suggest(new[] { "מרפסת 3 - Balcony" }, UsageKeySlot.New));

    [Fact]
    public void Suggest_LatinOnly() =>
        Assert.Equal(new[] { 105 }, UsageSuggester.Suggest(new[] { "Lobby" }, UsageKeySlot.New));

    [Fact]
    public void Suggest_IsCaseInsensitive() =>
        Assert.Equal(new[] { 104 }, UsageSuggester.Suggest(new[] { "LIFT 2" }, UsageKeySlot.New));

    [Fact]
    public void Suggest_MatchesMamadWithGershayim()
    {
        Assert.Equal(new[] { 101 }, UsageSuggester.Suggest(new[] { "ממ״ד 2 - Safe room" }, UsageKeySlot.New));
        Assert.Equal(new[] { 101 }, UsageSuggester.Suggest(new[] { "ממ\"ד" }, UsageKeySlot.New));
    }

    [Fact]
    public void Suggest_ManyNames_DistinctAndCappedAt4() =>
        Assert.Equal(new[] { 30, 110, 250, 33 },
            UsageSuggester.Suggest(new[] { "מרפסת", "מרפסת 2", "חניה", "מחסן" }, UsageKeySlot.New));

    [Fact]
    public void Suggest_NoMatch_Empty() =>
        Assert.Empty(UsageSuggester.Suggest(new[] { "Room 12 חדר" }, UsageKeySlot.New));

    [Fact]
    public void Suggest_NullNames_Ignored() =>
        Assert.Equal(new[] { 1 }, UsageSuggester.Suggest(new string?[] { null, "", "דירה 4 - Apt" }, UsageKeySlot.New));
}
