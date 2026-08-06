using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class GridSnapperTests
{
    private const double Half = GridSnapper.HalfCentimetreStep;
    private const double Whole = GridSnapper.WholeCentimetreStep;
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(0.1, 0.0)]
    [InlineData(0.34, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1000.4, 1000.5)]
    [InlineData(1000.2, 1000.0)]
    public void HalfCentimetreGridSnapsToTheNearestHalf(double input, double expected)
    {
        Assert.Equal(expected, GridSnapper.Snap(input, Half), Tolerance);
    }

    [Theory]
    [InlineData(0.1, 0.0)]
    [InlineData(0.34, 0.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1000.4, 1000.0)]
    public void WholeCentimetreGridSnapsToTheNearestCentimetre(double input, double expected)
    {
        Assert.Equal(expected, GridSnapper.Snap(input, Whole), Tolerance);
    }

    [Fact]
    public void ExactMidpointsResolveAwayFromZeroOnBothSides()
    {
        // Banker's rounding would send 0.5 and 1.5 both to even values, so a model mirrored
        // about the origin would round asymmetrically.
        Assert.Equal(1.0, GridSnapper.Snap(0.5, Whole), Tolerance);
        Assert.Equal(-1.0, GridSnapper.Snap(-0.5, Whole), Tolerance);
        Assert.Equal(0.5, GridSnapper.Snap(0.25, Half), Tolerance);
        Assert.Equal(-0.5, GridSnapper.Snap(-0.25, Half), Tolerance);
    }

    [Theory]
    [InlineData(-0.1, 0.0)]
    [InlineData(-0.34, -0.5)]
    [InlineData(-1000.4, -1000.5)]
    public void NegativeCoordinatesSnapSymmetrically(double input, double expected)
    {
        Assert.Equal(expected, GridSnapper.Snap(input, Half), Tolerance);
    }

    [Theory]
    [InlineData(Half)]
    [InlineData(Whole)]
    public void NothingEverMovesFurtherThanHalfAStep(double step)
    {
        for (var raw = -50.0; raw <= 50.0; raw += 0.017)
        {
            var moved = System.Math.Abs(GridSnapper.Snap(raw, step) - raw);
            Assert.True(moved <= step / 2 + Tolerance, $"{raw} moved {moved}");
        }
    }

    [Fact]
    public void FeetAndCentimetresRoundTrip()
    {
        Assert.Equal(30.48, GridSnapper.FromFeet(1.0), Tolerance);
        Assert.Equal(1.0, GridSnapper.ToFeet(30.48), Tolerance);
        Assert.Equal(12.34, GridSnapper.ToFeet(GridSnapper.FromFeet(12.34)), Tolerance);
    }

    [Fact]
    public void StepForPicksTheGridTheCheckboxAsksFor()
    {
        Assert.Equal(Half, GridSnapper.StepFor(true), Tolerance);
        Assert.Equal(Whole, GridSnapper.StepFor(false), Tolerance);
    }

    [Fact]
    public void IsOnGridToleratesFloatDustButNotDrift()
    {
        Assert.True(GridSnapper.IsOnGrid(1.5 + 1e-12, Half));
        Assert.False(GridSnapper.IsOnGrid(1.5 + 1e-4, Half));
    }

    [Fact]
    public void SnappingAPointSnapsAllThreeAxes()
    {
        var snapped = GridSnapper.Snap(new GridPoint(1.001, 5.001, 2.4), Half);
        Assert.Equal(1.0, snapped.X, Tolerance);
        Assert.Equal(5.0, snapped.Y, Tolerance);
        Assert.Equal(2.5, snapped.Z, Tolerance);
    }
}
