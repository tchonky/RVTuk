using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoEdgePointDeduperTests
{
    private static KeyValuePair<long, IReadOnlyList<XyzPoint>> Line(long id, params XyzPoint[] points) =>
        new(id, points);

    [Fact]
    public void Chained_lines_meeting_on_the_outline_keep_the_shared_point_on_the_first_line()
    {
        var shared = new XyzPoint(10, 5, 42);
        var result = TopoEdgePointDeduper.Dedupe(new[]
        {
            Line(1, new XyzPoint(0, 5, 42), shared),
            Line(2, new XyzPoint(10 + 0.00001, 5, 43), new XyzPoint(10, 9, 43)),
        });

        Assert.Equal(new[] { new XyzPoint(0, 5, 42), shared }, result[1]);
        Assert.Equal(new[] { new XyzPoint(10, 9, 43) }, result[2]);
    }

    [Fact]
    public void Points_farther_apart_than_the_tolerance_are_all_kept()
    {
        double apart = 2 * ShapeReplayPlanner.ToleranceFeet;
        var result = TopoEdgePointDeduper.Dedupe(new[]
        {
            Line(1, new XyzPoint(0, 0, 1)),
            Line(2, new XyzPoint(apart, 0, 1)),
        });

        Assert.Single(result[1]);
        Assert.Single(result[2]);
    }

    [Fact]
    public void A_duplicate_within_one_line_is_dropped_and_every_line_gets_an_entry()
    {
        var result = TopoEdgePointDeduper.Dedupe(new[]
        {
            Line(1, new XyzPoint(3, 3, 0), new XyzPoint(3, 3, 0)),
            Line(2),
        });

        Assert.Single(result[1]);
        Assert.Empty(result[2]);
    }
}
