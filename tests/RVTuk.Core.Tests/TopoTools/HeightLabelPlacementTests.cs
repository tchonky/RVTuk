using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class HeightLabelPlacementTests
{
    private const double Tolerance = 1e-9;

    private static List<XyPoint> Line(params (double X, double Y)[] points)
    {
        var polyline = new List<XyPoint>();
        foreach (var (x, y) in points) polyline.Add(new XyPoint(x, y));
        return polyline;
    }

    [Fact]
    public void MidpointOfAStraightLine()
    {
        var spot = HeightLabelPlacement.Midpoint(Line((0, 0), (10, 0)));

        Assert.Equal(5, spot.Point.X, Tolerance);
        Assert.Equal(0, spot.Point.Y, Tolerance);
        Assert.Equal(0, spot.AngleRadians, Tolerance);
    }

    [Fact]
    public void MidpointIsByLengthNotByVertexCount()
    {
        // Lengths 1 and 9, total 10: the midpoint sits 4 up the second segment.
        var spot = HeightLabelPlacement.Midpoint(Line((0, 0), (1, 0), (1, 9)));

        Assert.Equal(1, spot.Point.X, Tolerance);
        Assert.Equal(4, spot.Point.Y, Tolerance);
        Assert.Equal(Math.PI / 2, spot.AngleRadians, Tolerance);
    }

    [Fact]
    public void ALineDrawnRightToLeftReadsLeftToRight()
    {
        var spot = HeightLabelPlacement.Midpoint(Line((10, 0), (0, 0)));

        Assert.Equal(5, spot.Point.X, Tolerance);
        Assert.Equal(0, spot.Point.Y, Tolerance);
        Assert.Equal(0, spot.AngleRadians, Tolerance);
    }

    [Fact]
    public void ALineDrawnDownwardsReadsUpwards()
    {
        var spot = HeightLabelPlacement.Midpoint(Line((0, 10), (0, 0)));

        Assert.Equal(0, spot.Point.X, Tolerance);
        Assert.Equal(5, spot.Point.Y, Tolerance);
        Assert.Equal(Math.PI / 2, spot.AngleRadians, Tolerance);
    }

    [Fact]
    public void ADiagonalPointingDownLeftIsFlipped()
    {
        var spot = HeightLabelPlacement.Midpoint(Line((2, 2), (0, 0)));

        Assert.Equal(1, spot.Point.X, Tolerance);
        Assert.Equal(1, spot.Point.Y, Tolerance);
        Assert.Equal(Math.PI / 4, spot.AngleRadians, Tolerance);
    }

    [Fact]
    public void ZeroLengthPolylineReturnsItsFirstPointWithZeroAngle()
    {
        var spot = HeightLabelPlacement.Midpoint(Line((3, 4), (3, 4)));

        Assert.Equal(3, spot.Point.X, Tolerance);
        Assert.Equal(4, spot.Point.Y, Tolerance);
        Assert.Equal(0, spot.AngleRadians, Tolerance);
    }
}
