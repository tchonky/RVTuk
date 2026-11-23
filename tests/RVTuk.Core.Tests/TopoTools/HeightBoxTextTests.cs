using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class HeightBoxTextTests
{
    [Fact]
    public void EmptySelectionShowsNothing() => Assert.Equal("", HeightBoxText.For(new string?[0]));

    [Fact]
    public void OneLabelShowsItsHeight() => Assert.Equal("42.75", HeightBoxText.For(new string?[] { "42,75 m" }));

    [Fact]
    public void SameHeightWrittenTwoWaysShowsOnce() =>
        Assert.Equal("42.70", HeightBoxText.For(new string?[] { "42.7", "42.70" }));

    [Fact]
    public void DifferentHeightsShowNothing() => Assert.Equal("", HeightBoxText.For(new string?[] { "42.70", "43.00" }));

    [Fact]
    public void AnUnreadableLabelShowsNothing() => Assert.Equal("", HeightBoxText.For(new string?[] { "42.70", "abc" }));

    [Fact]
    public void AMissingLabelShowsNothing() => Assert.Equal("", HeightBoxText.For(new string?[] { "42.70", null }));

    [Theory]
    [InlineData("", "42.70", true)]        // an empty box shows the selection
    [InlineData("  ", "42.70", true)]
    [InlineData(null, "", true)]
    [InlineData("42.70", "42.70", true)]   // what the box itself showed
    [InlineData("43", "42.70", false)]     // typed: kept, also after Enter
    [InlineData("+0.1", "", false)]
    public void TheBoxFollowsTheSelectionOnlyUntilSomethingIsTyped(string? current, string lastShown, bool follows)
        => Assert.Equal(follows, HeightBoxText.Follows(current, lastShown));
}
