using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class WallCrossingFinderTests
{
    // Reference line: horizontal, from (0,0) to (10,0).
    private static readonly XyPoint LineStart = new(0, 0);
    private static readonly XyPoint LineEnd = new(10, 0);

    [Fact]
    public void WallsCrossingInVariousOrders_AreSortedByProjectionAlongLine()
    {
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),  // crosses at t=0.8
            new(new XyPoint(2, -5), new XyPoint(2, 5)),  // crosses at t=0.2
            new(new XyPoint(5, -5), new XyPoint(5, 5)),  // crosses at t=0.5
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Equal(new[] { 1, 2, 0 }, result);
    }

    [Fact]
    public void WallTouchingOnlyAtEndpoint_IsExcluded()
    {
        // Wall's own start point (5,0) lies exactly on the reference line: a T-junction.
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(5, 0), new XyPoint(5, 5)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void CollinearWall_IsExcluded()
    {
        // Wall runs along the same direction as the line (0 degrees from parallel).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(3, 0), new XyPoint(7, 0)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void ObliqueWallWithinTolerance_IsExcluded()
    {
        // ~3 degrees from parallel to the line (tan(3 deg) * 6 =~ 0.314).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(2, -0.157), new XyPoint(8, 0.157)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void ObliqueWallOutsideTolerance_IsIncluded()
    {
        // ~10 degrees from parallel to the line (tan(10 deg) * 6 =~ 1.058).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(2, -0.529), new XyPoint(8, 0.529)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void WallNotCrossingLineAtAll_ReturnsEmpty()
    {
        // Both endpoints are above the line (y > 0); never reaches y=0.
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(2, 5), new XyPoint(8, 10)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void WallCrossingBeyondLineBounds_IsExcluded()
    {
        // The infinite lines would cross at x=15, beyond the reference line's segment (x in [0,10]).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(15, -5), new XyPoint(15, 5)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }
}
