using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class ElevationRangeTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void WallSpanningTheCutPlaneIsCut()
    {
        Assert.True(ElevationRange.CrossesCutPlane(0, 10, 4, Tolerance));
    }

    [Fact]
    public void WallEntirelyBelowTheCutPlaneIsNotCut()
    {
        // The floor below's walls must not be dimensioned into this level's view.
        Assert.False(ElevationRange.CrossesCutPlane(-10, -1, 4, Tolerance));
    }

    [Fact]
    public void WallEntirelyAboveTheCutPlaneIsNotCut()
    {
        Assert.False(ElevationRange.CrossesCutPlane(20, 30, 4, Tolerance));
    }

    [Fact]
    public void WallTouchingExactlyAtItsTopIsCut()
    {
        // Inclusive at the boundary: a wall whose top lands exactly on the cut plane still
        // reads as cut, matching how a plan view draws it.
        Assert.True(ElevationRange.CrossesCutPlane(0, 4, 4, Tolerance));
    }

    [Fact]
    public void WallTouchingExactlyAtItsBaseIsCut()
    {
        Assert.True(ElevationRange.CrossesCutPlane(4, 12, 4, Tolerance));
    }

    [Fact]
    public void ToleranceAdmitsAHairBelowTheBase()
    {
        Assert.True(ElevationRange.CrossesCutPlane(4.0000001, 12, 4, Tolerance));
        Assert.False(ElevationRange.CrossesCutPlane(4.1, 12, 4, Tolerance));
    }

    [Fact]
    public void InvertedRangeIsNormalised()
    {
        // A transformed bounding box can hand back min/max the wrong way round.
        Assert.True(ElevationRange.CrossesCutPlane(10, 0, 4, Tolerance));
    }
}
