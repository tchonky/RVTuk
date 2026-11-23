using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class HeightBoxInputTests
{
    private static string? Apply(string typed, string? label) =>
        HeightBoxInput.TryParse(typed, out var input) ? input.NewLabel(label) : "unreadable";

    [Theory]
    [InlineData("43", "42.70", "43.00")]          // a plain number sets the height
    [InlineData("43,25 m", "42.70", "43.25")]
    [InlineData("43", null, "43.00")]             // even on a line without a label
    [InlineData("=-3.20", "42.70", "-3.20")]      // "=" sets a height below zero
    [InlineData("= 12", "42.70", "12.00")]
    public void AnAbsoluteHeightReplacesTheLabel(string typed, string? label, string expected)
        => Assert.Equal(expected, Apply(typed, label));

    [Theory]
    [InlineData("+0.5", "42.70", "43.20")]
    [InlineData("-0.5", "42.70", "42.20")]
    [InlineData("−0.25", "42.70", "42.45")]  // the typographic minus lowers too
    [InlineData("+0,5", "42.70", "43.20")]
    [InlineData("-0.5 m", "0.20", "-0.30")]       // may go below zero
    [InlineData("+0.1", "42.2", "42.30")]         // no float residue: 42.300000000000004
    public void ASignedNumberMovesTheLabelsOwnHeight(string typed, string? label, string expected)
        => Assert.Equal(expected, Apply(typed, label));

    [Theory]
    [InlineData("+0.5", null)]
    [InlineData("+0.5", "abc")]
    [InlineData("+1", "2999.5")]                  // past the highest height a label may hold
    public void AMoveLeavesALabelWithoutAUsableResultAlone(string typed, string? label)
        => Assert.Null(Apply(typed, label));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("+")]
    [InlineData("=")]
    [InlineData("=+0.5")]                         // "=" takes a height, not a move
    [InlineData("++0.5")]
    public void NonsenseIsUnreadable(string typed) => Assert.False(HeightBoxInput.TryParse(typed, out _));

    [Fact]
    public void OnlyAbsoluteInputIsAbsolute()
    {
        Assert.True(HeightBoxInput.TryParse("43", out var set) && set.IsAbsolute);
        Assert.True(HeightBoxInput.TryParse("=-3.2", out var below) && below.IsAbsolute);
        Assert.True(HeightBoxInput.TryParse("+0.5", out var move) && !move.IsAbsolute);
    }
}
