using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoLineSamplerTests
{
    private const double Tolerance = 1e-9;

    private static List<XyPoint> Line(params (double X, double Y)[] points)
    {
        var polyline = new List<XyPoint>();
        foreach (var (x, y) in points) polyline.Add(new XyPoint(x, y));
        return polyline;
    }

    [Fact]
    public void DividesALongSegmentEvenly()
    {
        var sampled = TopoLineSampler.Sample(Line((0, 0), (10, 0)), 2);

        Assert.Equal(6, sampled.Count);
        for (int i = 0; i < sampled.Count; i++)
        {
            Assert.Equal(i * 2.0, sampled[i].X, Tolerance);
            Assert.Equal(0, sampled[i].Y, Tolerance);
        }
    }

    [Fact]
    public void DividesUnevenLengthsSoNoGapExceedsTheSpacing()
    {
        // 5 long at spacing 2 → 3 divisions of 1.667, never a gap over 2.
        var sampled = TopoLineSampler.Sample(Line((0, 0), (5, 0)), 2);

        Assert.Equal(4, sampled.Count);
        for (int i = 1; i < sampled.Count; i++)
            Assert.True(sampled[i].X - sampled[i - 1].X <= 2 + Tolerance);
    }

    [Fact]
    public void KeepsEveryVertexOfAPolylineWhenSegmentsAreShorterThanTheSpacing()
    {
        var sampled = TopoLineSampler.Sample(Line((0, 0), (0, 3), (4, 3)), 10);

        Assert.Equal(3, sampled.Count);
        Assert.Equal(new XyPoint(0, 0), sampled[0]);
        Assert.Equal(new XyPoint(0, 3), sampled[1]);
        Assert.Equal(new XyPoint(4, 3), sampled[2]);
    }

    [Fact]
    public void DropsTheRepeatedClosingPointOfAClosedLoop()
    {
        // Curve.Tessellate() hands back the start point again as the last point.
        var sampled = TopoLineSampler.Sample(
            Line((0, 0), (10, 0), (10, 10), (0, 10), (0, 0)), 50);

        Assert.Equal(4, sampled.Count);
        Assert.Equal(new XyPoint(0, 10), sampled[3]);
    }

    [Fact]
    public void DropsANearDuplicateVertex()
    {
        // A tessellated tight arc emits vertices a hair apart; they must not become slivers.
        var sampled = TopoLineSampler.Sample(Line((0, 0), (0.01, 0), (10, 0)), 5);

        Assert.Equal(3, sampled.Count);
        Assert.Equal(0, sampled[0].X, Tolerance);
        Assert.Equal(10, sampled[2].X, Tolerance);
    }

    [Fact]
    public void CollapsesALineShorterThanATenthOfTheSpacingToOnePoint()
    {
        var sampled = TopoLineSampler.Sample(Line((0, 0), (0.5, 0)), 100);

        Assert.Single(sampled);
    }

    [Fact]
    public void ReturnsNothingForAnEmptyPolyline()
    {
        Assert.Empty(TopoLineSampler.Sample(new List<XyPoint>(), 1));
    }

    [Fact]
    public void RejectsANonPositiveSpacing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TopoLineSampler.Sample(Line((0, 0), (1, 0)), 0));
    }
}
