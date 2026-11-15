using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ShapeReplayPlannerTests
{
    // A freshly created 10 × 10 ft target, flat at Z = 10: its four corners.
    private static readonly XyzPoint[] FlatSquare =
    {
        new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10),
    };

    // The same square's outline, and the same square with a 2 × 2 hole in its middle, as the
    // tessellated loops the converter hands the planner.
    private static readonly IReadOnlyList<IReadOnlyList<XyPoint>> SquareOutline = new[]
    {
        new[] { new XyPoint(0, 0), new XyPoint(10, 0), new XyPoint(10, 10), new XyPoint(0, 10) },
    };

    private static readonly IReadOnlyList<IReadOnlyList<XyPoint>> SquareWithHole = new[]
    {
        new[] { new XyPoint(0, 0), new XyPoint(10, 0), new XyPoint(10, 10), new XyPoint(0, 10) },
        new[] { new XyPoint(4, 4), new XyPoint(6, 4), new XyPoint(6, 6), new XyPoint(4, 6) },
    };

    [Fact]
    public void RaisedAndLoweredCornersBecomeOffsetsFromTheTargetHeight()
    {
        var source = new[]
        {
            new XyzPoint(0, 0, 10), new XyzPoint(10, 0, 11),
            new XyzPoint(10, 10, 10), new XyzPoint(0, 10, 9.5),
        };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Equal(new[] { new VertexOffset(1, 1.0), new VertexOffset(3, -0.5) }, plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void ACornerAlreadyAtItsHeightNeedsNoOffset()
    {
        var plan = ShapeReplayPlanner.Plan(FlatSquare, FlatSquare, SquareOutline);

        Assert.Empty(plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void MatchesACornerWithinATenthOfAMillimetreButNotBeyond()
    {
        double within = 0.05 / 304.8;
        double beyond = 0.5 / 304.8;
        var source = new[] { new XyzPoint(10 + within, 0, 12), new XyzPoint(beyond, 10, 12) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Equal(new[] { new VertexOffset(1, 2.0) }, plan.Offsets);
        // Unmatched, and on the top edge: drawn, not added.
        Assert.Equal(new[] { new XyzPoint(beyond, 10, 12) }, plan.EdgePoints);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void InteriorPointsAreAddedAtTheirAbsolutePosition()
    {
        var source = new[] { new XyzPoint(5, 5, 12.25), new XyzPoint(2, 7, 9) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Empty(plan.Offsets);
        Assert.Equal(source, plan.Additions);
    }

    [Fact]
    public void HoleCornersMatchTheirOwnTargetVertices()
    {
        var target = new List<XyzPoint>(FlatSquare)
        {
            new(4, 4, 10), new(6, 4, 10), new(6, 6, 10), new(4, 6, 10),
        };
        var source = new[] { new XyzPoint(6, 4, 10.5), new XyzPoint(4, 6, 10.5) };

        var plan = ShapeReplayPlanner.Plan(source, target, SquareWithHole);

        Assert.Equal(new[] { new VertexOffset(5, 0.5), new VertexOffset(7, 0.5) }, plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void TwoSourcePointsOnOneXyAreSentOnce()
    {
        // AddPoints rejects a whole batch that holds two points on one XY.
        var source = new[]
        {
            new XyzPoint(5, 5, 12), new XyzPoint(5, 5, 13),
            new XyzPoint(10, 10, 11), new XyzPoint(10, 10, 14),
        };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Equal(new[] { new XyzPoint(5, 5, 12) }, plan.Additions);
        Assert.Equal(new[] { new VertexOffset(2, 1.0) }, plan.Offsets);
    }

    [Fact]
    public void AnEmptySourceGivesAnEmptyPlan()
    {
        var plan = ShapeReplayPlanner.Plan(new XyzPoint[0], FlatSquare, SquareOutline);

        Assert.Empty(plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void APointOnTheOutlineIsAnEdgePointNotAnAddition()
    {
        // AddPoints wants points strictly inside the boundary; one on an edge fails the batch.
        var source = new[] { new XyzPoint(5, 0, 12), new XyzPoint(5, 5, 12) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Equal(new[] { new XyzPoint(5, 0, 12) }, plan.EdgePoints);
        Assert.Equal(new[] { new XyzPoint(5, 5, 12) }, plan.Additions);
    }

    [Fact]
    public void APointOnAHoleEdgeIsAnEdgePoint()
    {
        var target = new List<XyzPoint>(FlatSquare)
        {
            new(4, 4, 10), new(6, 4, 10), new(6, 6, 10), new(4, 6, 10),
        };
        var source = new[] { new XyzPoint(5, 4, 11) };

        var plan = ShapeReplayPlanner.Plan(source, target, SquareWithHole);

        Assert.Equal(source, plan.EdgePoints);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void ThePointsWithinAMillimetreOfAnEdgeAreEdgePoints_TheRestAreAdded()
    {
        double within = 0.5 / 304.8;
        double beyond = 3.0 / 304.8;
        var source = new[] { new XyzPoint(5, within, 12), new XyzPoint(5, beyond, 12) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Equal(new[] { new XyzPoint(5, within, 12) }, plan.EdgePoints);
        Assert.Equal(new[] { new XyzPoint(5, beyond, 12) }, plan.Additions);
    }

    [Fact]
    public void TwoEdgePointsOnOneXyAreSentOnce()
    {
        var source = new[] { new XyzPoint(5, 0, 12), new XyzPoint(5, 0, 13), new XyzPoint(7, 0, 11) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare, SquareOutline);

        Assert.Equal(new[] { new XyzPoint(5, 0, 12), new XyzPoint(7, 0, 11) }, plan.EdgePoints);
    }

    [Fact]
    public void ACreaseIsPlannedBetweenTheVerticesAtItsEnds()
    {
        var live = new[] { new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10), new XyzPoint(5, 5, 12) };
        var creases = new[] { new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(5, 5, 12)) };

        var plan = ShapeReplayPlanner.PlanCreases(live, creases);

        Assert.Equal(new[] { new VertexPair(0, 2) }, plan.Pairs);
        Assert.Equal(0, plan.Dropped);
    }

    [Fact]
    public void ACreaseWithAnUnmatchedOrCollapsedEndIsDroppedAndCounted()
    {
        var live = new[] { new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10) };
        var creases = new[]
        {
            new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(3, 3, 10)),       // nothing at (3, 3)
            new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(0, 0.00001, 10)), // both ends on one vertex
            new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10)),
        };

        var plan = ShapeReplayPlanner.PlanCreases(live, creases);

        Assert.Equal(new[] { new VertexPair(0, 1) }, plan.Pairs);
        Assert.Equal(2, plan.Dropped);
    }
}
