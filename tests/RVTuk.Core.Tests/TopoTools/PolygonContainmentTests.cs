using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class PolygonContainmentTests
{
    private static IReadOnlyList<XyPoint> Loop(params (double X, double Y)[] points)
    {
        var loop = new List<XyPoint>();
        foreach (var (x, y) in points) loop.Add(new XyPoint(x, y));
        return loop;
    }

    private static IReadOnlyList<IReadOnlyList<XyPoint>> Loops(params IReadOnlyList<XyPoint>[] loops)
        => new List<IReadOnlyList<XyPoint>>(loops);

    private static readonly IReadOnlyList<XyPoint> Square =
        Loop((0, 0), (10, 0), (10, 10), (0, 10));

    [Fact]
    public void FindsAPointInsideASquare()
    {
        Assert.True(PolygonContainment.Contains(Loops(Square), new XyPoint(5, 5)));
    }

    [Fact]
    public void RejectsAPointOutsideASquare()
    {
        Assert.False(PolygonContainment.Contains(Loops(Square), new XyPoint(15, 5)));
    }

    [Fact]
    public void CountsAPointOnAnEdgeAsInside()
    {
        Assert.True(PolygonContainment.Contains(Loops(Square), new XyPoint(10, 5)));
    }

    [Fact]
    public void CountsAPointOnACornerAsInside()
    {
        Assert.True(PolygonContainment.Contains(Loops(Square), new XyPoint(0, 0)));
    }

    [Fact]
    public void RejectsAPointInTheNotchOfAConcaveFootprint()
    {
        // An L: the notch is the missing top-right quadrant.
        var shape = Loop((0, 0), (10, 0), (10, 5), (5, 5), (5, 10), (0, 10));

        Assert.True(PolygonContainment.Contains(Loops(shape), new XyPoint(2, 8)));
        Assert.False(PolygonContainment.Contains(Loops(shape), new XyPoint(8, 8)));
    }

    [Fact]
    public void ReadsASecondLoopAsAHole()
    {
        var hole = Loop((4, 4), (6, 4), (6, 6), (4, 6));

        Assert.False(PolygonContainment.Contains(Loops(Square, hole), new XyPoint(5, 5)));
        Assert.True(PolygonContainment.Contains(Loops(Square, hole), new XyPoint(1, 1)));
    }

    [Fact]
    public void ToleratesTheRepeatedClosingPointTessellationLeaves()
    {
        var closed = Loop((0, 0), (10, 0), (10, 10), (0, 10), (0, 0));

        Assert.True(PolygonContainment.Contains(Loops(closed), new XyPoint(5, 5)));
        Assert.False(PolygonContainment.Contains(Loops(closed), new XyPoint(50, 5)));
    }

    [Fact]
    public void RejectsEverythingWhenThereAreNoLoops()
    {
        Assert.False(PolygonContainment.Contains(Loops(), new XyPoint(0, 0)));
    }
}
