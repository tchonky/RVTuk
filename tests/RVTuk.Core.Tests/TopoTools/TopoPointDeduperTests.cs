using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoPointDeduperTests
{
    private const double Mm = 1 / 304.8;

    private static readonly XyzPoint[] None = new XyzPoint[0];

    private static KeyValuePair<long, LinePointsZ> Line(long id, XyzPoint[] interior, XyzPoint[] edge) =>
        new(id, new LinePointsZ(interior, edge));

    private static XyzPoint P(double x, double y, double z) => new(x, y, z);

    [Fact]
    public void Two_lines_crossing_at_one_spot_keep_the_higher_point()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(0, 5, 42), P(5, 5, 42), P(10, 5, 42) }, None),
            Line(2, new[] { P(5, 0, 43), P(5, 5, 43), P(5, 10, 43) }, None),
        });

        Assert.Equal(new[] { P(0, 5, 42), P(10, 5, 42) }, result.Kept[1].Interior);
        Assert.Equal(new[] { P(5, 0, 43), P(5, 5, 43), P(5, 10, 43) }, result.Kept[2].Interior);
        Assert.Equal(1, result.DroppedForHigher);
    }

    [Fact]
    public void The_higher_point_wins_whichever_line_comes_first()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(5, 5, 43) }, None),
            Line(2, new[] { P(5, 5, 42) }, None),
        });

        Assert.Equal(new[] { P(5, 5, 43) }, result.Kept[1].Interior);
        Assert.Empty(result.Kept[2].Interior);
        Assert.Equal(1, result.DroppedForHigher);
    }

    [Fact]
    public void Equal_heights_leave_the_point_on_the_first_line_and_are_not_counted()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(5, 5, 42) }, None),
            Line(2, new[] { P(5, 5, 42) }, None),
        });

        Assert.Equal(new[] { P(5, 5, 42) }, result.Kept[1].Interior);
        Assert.Empty(result.Kept[2].Interior);
        Assert.Equal(0, result.DroppedForHigher);
    }

    [Fact]
    public void Points_half_a_millimetre_apart_are_one_spot()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(5, 5, 42) }, None),
            Line(2, new[] { P(5 + 0.5 * Mm, 5, 43) }, None),
        });

        Assert.Empty(result.Kept[1].Interior);
        Assert.Single(result.Kept[2].Interior);
        Assert.Equal(1, result.DroppedForHigher);
    }

    // The deduper looks for a kept point in a grid of 1 mm cells: the point's own cell and the
    // eight around it. 5 ft is 1524 mm, so the lower point sits in the middle of a cell, and the
    // higher one is 0.6 mm away (0.85 mm on a diagonal) in each of the eight neighbours in turn.
    // A lookup that misses any neighbour keeps both points.
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-1, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void Points_under_a_millimetre_apart_in_neighbouring_grid_cells_are_one_spot(int dx, int dy)
    {
        var lower = P(5 + 0.5 * Mm, 5 + 0.5 * Mm, 42);
        var higher = P(lower.X + dx * 0.6 * Mm, lower.Y + dy * 0.6 * Mm, 43);
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { lower }, None),
            Line(2, new[] { higher }, None),
        });

        Assert.Empty(result.Kept[1].Interior);
        Assert.Equal(new[] { higher }, result.Kept[2].Interior);
        Assert.Equal(1, result.DroppedForHigher);
    }

    [Fact]
    public void Points_two_millimetres_apart_are_two_spots()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(5, 5, 42) }, None),
            Line(2, new[] { P(5 + 2 * Mm, 5, 43) }, None),
            Line(3, new[] { P(5, 5 - 2 * Mm, 44) }, None),
        });

        Assert.Single(result.Kept[1].Interior);
        Assert.Single(result.Kept[2].Interior);
        Assert.Single(result.Kept[3].Interior);
        Assert.Equal(0, result.DroppedForHigher);
    }

    [Fact]
    public void Chained_lines_meeting_on_the_outline_keep_the_shared_edge_point_on_the_first_line()
    {
        var shared = P(10, 5, 42);
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, None, new[] { P(0, 5, 42), shared }),
            Line(2, None, new[] { P(10 + 0.00001, 5, 42), P(10, 9, 42) }),
        });

        Assert.Equal(new[] { P(0, 5, 42), shared }, result.Kept[1].Edge);
        Assert.Equal(new[] { P(10, 9, 42) }, result.Kept[2].Edge);
        Assert.Equal(0, result.DroppedForHigher);
    }

    [Fact]
    public void An_edge_point_shared_by_lines_at_different_heights_goes_to_the_higher()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, None, new[] { P(0, 5, 42), P(10, 5, 42) }),
            Line(2, None, new[] { P(10, 5, 43), P(10, 9, 43) }),
        });

        Assert.Equal(new[] { P(0, 5, 42) }, result.Kept[1].Edge);
        Assert.Equal(new[] { P(10, 5, 43), P(10, 9, 43) }, result.Kept[2].Edge);
        Assert.Equal(1, result.DroppedForHigher);
    }

    [Fact]
    public void A_line_crossing_itself_keeps_one_interior_point_there()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(1, 1, 42), P(3, 3, 42), P(5, 1, 42), P(3, 3, 42) }, None),
        });

        Assert.Equal(new[] { P(1, 1, 42), P(3, 3, 42), P(5, 1, 42) }, result.Kept[1].Interior);
        Assert.Equal(0, result.DroppedForHigher);
    }

    [Fact]
    public void A_line_losing_a_point_keeps_its_others_in_order()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(1, 0, 40), P(2, 0, 40), P(3, 0, 40) }, new[] { P(0, 0, 40), P(4, 0, 40) }),
            Line(2, new[] { P(2, 0, 41) }, None),
        });

        Assert.Equal(new[] { P(1, 0, 40), P(3, 0, 40) }, result.Kept[1].Interior);
        Assert.Equal(new[] { P(0, 0, 40), P(4, 0, 40) }, result.Kept[1].Edge);
        Assert.Equal(1, result.DroppedForHigher);
    }

    [Fact]
    public void An_interior_point_and_an_edge_point_at_one_spot_keep_the_higher()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, None, new[] { P(5, 5, 42) }),
            Line(2, new[] { P(5, 5 + 0.5 * Mm, 45) }, None),
        });

        Assert.Empty(result.Kept[1].Edge);
        Assert.Single(result.Kept[2].Interior);
        Assert.Equal(1, result.DroppedForHigher);
    }

    [Fact]
    public void Every_line_gets_an_entry_even_without_points()
    {
        var result = TopoPointDeduper.Dedupe(new[]
        {
            Line(1, new[] { P(3, 3, 0) }, None),
            Line(2, None, None),
        });

        Assert.Single(result.Kept[1].Interior);
        Assert.Empty(result.Kept[2].Interior);
        Assert.Empty(result.Kept[2].Edge);
    }
}
