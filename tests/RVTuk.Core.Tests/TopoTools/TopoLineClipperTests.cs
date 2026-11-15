using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoLineClipperTests
{
    private const double Tolerance = 1e-9;

    private static List<XyPoint> Line(params (double X, double Y)[] points) =>
        points.Select(p => new XyPoint(p.X, p.Y)).ToList();

    private static IReadOnlyList<IReadOnlyList<XyPoint>> Square(bool withHole = false)
    {
        var loops = new List<IReadOnlyList<XyPoint>> { Line((0, 0), (10, 0), (10, 10), (0, 10)) };
        if (withHole) loops.Add(Line((4, 4), (6, 4), (6, 6), (4, 6)));
        return loops;
    }

    private static void AssertPoints(IReadOnlyList<XyPoint> actual, params (double X, double Y)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, Tolerance);
            Assert.Equal(expected[i].Y, actual[i].Y, Tolerance);
        }
    }

    [Fact]
    public void ALineWhollyInsideIsOneRunUnchanged()
    {
        var runs = TopoLineClipper.Clip(Line((1, 1), (9, 1)), Square());

        Assert.Single(runs);
        AssertPoints(runs[0], (1, 1), (9, 1));
    }

    [Fact]
    public void ALineWhollyOutsideHasNoRuns()
    {
        var line = Line((11, 0), (20, 0));

        Assert.Empty(TopoLineClipper.Clip(line, Square()));
        Assert.True(TopoLineClipper.Points(line, Square(), 1, 0.001).IsEmpty);
    }

    [Fact]
    public void ALineCrossingTheOutlineIsCutAtTheCrossing()
    {
        var runs = TopoLineClipper.Clip(Line((-5, 5), (5, 5)), Square());

        Assert.Single(runs);
        AssertPoints(runs[0], (0, 5), (5, 5));
    }

    [Fact]
    public void ALineThroughAHoleBecomesTwoRuns()
    {
        var runs = TopoLineClipper.Clip(Line((1, 5), (9, 5)), Square(withHole: true));

        Assert.Equal(2, runs.Count);
        AssertPoints(runs[0], (1, 5), (4, 5));
        AssertPoints(runs[1], (6, 5), (9, 5));
    }

    [Fact]
    public void ALineWeavingAcrossTheEdgeGetsOneEdgePointPerCrossing()
    {
        var line = Line((-1, 2), (1, 2), (1, 4), (-1, 4), (-1, 6), (1, 6));

        var runs = TopoLineClipper.Clip(line, Square());

        Assert.Equal(2, runs.Count);
        AssertPoints(runs[0], (0, 2), (1, 2), (1, 4), (0, 4));
        AssertPoints(runs[1], (0, 6), (1, 6));

        var points = TopoLineClipper.Points(line, Square(), 1, 0.001);
        AssertPoints(points.Edge, (0, 2), (0, 4), (0, 6));
    }

    [Fact]
    public void EdgePointsAreNotRepeatedAsInterior()
    {
        var line = Line((-1, 2), (1, 2), (1, 4), (-1, 4), (-1, 6), (1, 6));

        var points = TopoLineClipper.Points(line, Square(), 1, 0.001);

        Assert.NotEmpty(points.Interior);
        foreach (var edge in points.Edge)
            Assert.DoesNotContain(points.Interior,
                p => System.Math.Abs(p.X - edge.X) < Tolerance && System.Math.Abs(p.Y - edge.Y) < Tolerance);
    }

    [Fact]
    public void ALineAlongTheEdgeIsAllEdge()
    {
        var points = TopoLineClipper.Points(Line((0, 1), (0, 9)), Square(), 1, 0.001);

        Assert.Empty(points.Interior);
        Assert.NotEmpty(points.Edge);
    }
}
