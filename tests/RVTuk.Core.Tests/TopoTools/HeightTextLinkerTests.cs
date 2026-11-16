using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class HeightTextLinkerTests
{
    private static LineSpot Spot(long id, double x, double y) => new LineSpot(id, new XyPoint(x, y));

    private static TextClaim Text(long id, long? line, double x, double y) =>
        new TextClaim(id, line, new XyPoint(x, y));

    [Fact]
    public void EachTextKeepsItsOwnLine()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(1, 10, 0, 0), Text(2, 11, 0, 10) },
            new[] { Spot(10, 0, 0), Spot(11, 0, 10) });

        Assert.Equal(10, plan.LineByText[1]);
        Assert.Equal(11, plan.LineByText[2]);
        Assert.Equal(2, plan.LineByText.Count);
        Assert.Empty(plan.Relinked);
        Assert.Empty(plan.DeleteTexts);
        Assert.Empty(plan.UnlinkedTexts);
        Assert.Empty(plan.LinesNeedingText);
    }

    [Fact]
    public void ACopiedLineWithItsTextGetsTheCopy()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(1, 10, 0, 0.2), Text(2, 10, 0, 10.2) },
            new[] { Spot(10, 0, 0), Spot(20, 0, 10) });

        Assert.Equal(10, plan.LineByText[1]);
        Assert.Equal(20, plan.LineByText[2]);
        Assert.Equal(new long[] { 2 }, plan.Relinked.ToArray());
        Assert.Empty(plan.DeleteTexts);
        Assert.Empty(plan.UnlinkedTexts);
        Assert.Empty(plan.LinesNeedingText);
    }

    [Fact]
    public void ATextWhoseLineIsGoneIsDeleted()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(5, 99, 0, 0) },
            new List<LineSpot>());

        Assert.Equal(new long[] { 5 }, plan.DeleteTexts.ToArray());
        Assert.Empty(plan.LineByText);
    }

    [Fact]
    public void ALineWithoutATextNeedsOne()
    {
        var plan = HeightTextLinker.Plan(new List<TextClaim>(), new[] { Spot(10, 0, 0) });

        Assert.Equal(new long[] { 10 }, plan.LinesNeedingText.ToArray());
    }

    [Fact]
    public void ASurplusTextWithNoFreeLineIsLeftAlone()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(1, 10, 0, 0), Text(2, 10, 5, 5) },
            new[] { Spot(10, 0, 0) });

        Assert.Equal(10, plan.LineByText[1]);
        Assert.Equal(new long[] { 2 }, plan.UnlinkedTexts.ToArray());
        Assert.Empty(plan.DeleteTexts);
        Assert.Empty(plan.Relinked);
        Assert.Empty(plan.LinesNeedingText);
    }

    [Fact]
    public void AnUnlinkedTextTakesTheNearestFreeLine()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(1, null, 0, 9) },
            new[] { Spot(10, 0, 0), Spot(11, 0, 10) });

        Assert.Equal(11, plan.LineByText[1]);
        Assert.Equal(new long[] { 1 }, plan.Relinked.ToArray());
        Assert.Equal(new long[] { 10 }, plan.LinesNeedingText.ToArray());
    }

    [Fact]
    public void EquidistantFreeLinesGoToTheLowerLineId()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(1, null, 0, 5) },
            new[] { Spot(30, 0, 10), Spot(20, 0, 0) });

        Assert.Equal(20, plan.LineByText[1]);
    }

    [Fact]
    public void EquidistantTextsGoToTheLowerTextIdForTheLine()
    {
        var plan = HeightTextLinker.Plan(
            new[] { Text(7, 10, 0, 1), Text(3, 10, 0, -1) },
            new[] { Spot(10, 0, 0) });

        Assert.Equal(10, plan.LineByText[3]);
        Assert.Equal(new long[] { 7 }, plan.UnlinkedTexts.ToArray());
    }
}
