using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

/// <summary>
/// The Revit side's corner pass inputs and gate, without Revit: the conversions between internal
/// feet and the rule's metres, and when Update runs the pass, switches shape editing on, or makes a
/// floor plain again.
/// </summary>
public class OutlineCornerPassTests
{
    private const double M = SurveyHeightText.MetresPerFoot;

    // ---------------------------------------------------------------- CornerPass.Line

    [Fact]
    public void ALineGoesToTheRuleInMetresAtItsSharedHeight()
    {
        var line = CornerPass.Line(42.75 / M, new[] { new XyPoint(0, 0), new XyPoint(10, -2.5) });

        Assert.Equal(42.75, line.HeightMetres, 12);
        Assert.Equal(2, line.PolylineMetres.Count);
        Assert.Equal(0, line.PolylineMetres[0].X);
        Assert.Equal(10 * M, line.PolylineMetres[1].X, 12);
        Assert.Equal(-2.5 * M, line.PolylineMetres[1].Y, 12);
    }

    [Theory]
    [InlineData(1.0, 18)]     // a quarter circle of 1 m radius: 17.56 → 18
    [InlineData(10.0, 56)]    // of 10 m: 55.54 → 56
    public void AnArcInFeetTakesTheRulesChordCount(double radiusMetres, int expected)
    {
        double radiusFeet = radiusMetres / M;
        double lengthFeet = Math.PI / 2 * radiusFeet;

        Assert.Equal(expected, CornerPass.ArcChordCount(lengthFeet, radiusFeet));
        Assert.Equal(OutlineCornerHeights.ArcChordCount(Math.PI / 2, radiusMetres),
                     CornerPass.ArcChordCount(lengthFeet, radiusFeet));
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(1.0, double.NaN)]
    public void AnArcWithNoUsableRadiusTakesOneChord(double lengthFeet, double radiusFeet)
    {
        Assert.Equal(1, CornerPass.ArcChordCount(lengthFeet, radiusFeet));
    }

    // ---------------------------------------------------------------- CornerPass.TargetZ

    private static OutlineCornerHeights OneLine(double heightMetres) =>
        OutlineCornerHeights.Prepare(new[]
        {
            new CornerLine(heightMetres, new[] { new XyPoint(100, 200), new XyPoint(110, 200) }),
        });

    [Fact]
    public void AVertexOnALineTakesItsHeightAsInternalZ()
    {
        var pass = new CornerPass(OneLine(42.75), 30);
        var onLine = new XyzPoint(105 / M, 200 / M, 0);

        var z = pass.TargetZ(onLine);

        Assert.NotNull(z);
        Assert.Equal(42.75 / M - 30, z!.Value, 9);
    }

    [Fact]
    public void AVertexTheRuleLeavesAloneHasNoTarget()
    {
        // One line, a metre away, nothing beyond: left alone.
        var pass = new CornerPass(OneLine(42.75), 30);

        Assert.Null(pass.TargetZ(new XyzPoint(105 / M, 201 / M, 0)));
    }

    [Fact]
    public void NoRuleMeansNoTarget()
    {
        Assert.Null(new CornerPass(null, 30).TargetZ(new XyzPoint(0, 0, 0)));
    }

    // ---------------------------------------------------------------- CornerPassGate.Start

    private static readonly CornerPass WithRule = new CornerPass(OneLine(42), 0);
    private static readonly CornerPass NoRule = new CornerPass(null, 0);

    private static Func<bool> Needs(bool answer, List<bool>? asked = null) => () =>
    {
        asked?.Add(true);
        return answer;
    };

    [Fact]
    public void LineWorkAlwaysRuns()
    {
        foreach (var pass in new[] { null, NoRule, WithRule })
        {
            foreach (var enabled in new[] { false, true })
            {
                Assert.Equal(CornerPassStart.Run, CornerPassGate.Start(true, pass, enabled, 0, Needs(false)));
            }
        }
    }

    [Fact]
    public void NoLineWorkAndNoPassSkips()
    {
        Assert.Equal(CornerPassStart.Skip, CornerPassGate.Start(false, null, true, 3, Needs(true)));
        Assert.Equal(CornerPassStart.Skip, CornerPassGate.Start(false, null, false, 0, Needs(true)));
    }

    [Fact]
    public void APlainElementRunsOnlyWhenTheDryPlanRaisesSomething()
    {
        var asked = new List<bool>();

        Assert.Equal(CornerPassStart.Run, CornerPassGate.Start(false, WithRule, false, 0, Needs(true, asked)));
        Assert.Equal(CornerPassStart.DropRecords, CornerPassGate.Start(false, WithRule, false, 0, Needs(false, asked)));
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public void APlainElementWithNoRuleDropsItsRecordsWithoutADryPlan()
    {
        var asked = new List<bool>();

        Assert.Equal(CornerPassStart.DropRecords, CornerPassGate.Start(false, NoRule, false, 4, Needs(true, asked)));
        Assert.Empty(asked);
    }

    [Fact]
    public void AShapeEditedElementRunsWithARuleOrARecord()
    {
        Assert.Equal(CornerPassStart.Run, CornerPassGate.Start(false, WithRule, true, 0, Needs(false)));
        Assert.Equal(CornerPassStart.Run, CornerPassGate.Start(false, NoRule, true, 1, Needs(false)));
        Assert.Equal(CornerPassStart.Skip, CornerPassGate.Start(false, NoRule, true, 0, Needs(true)));
    }

    // ---------------------------------------------------------------- CornerPassGate.ResetToPlain

    [Fact]
    public void OnlyAFloorSwitchedOnForThePassAloneThatChangedNothingIsMadePlainAgain()
    {
        Assert.True(CornerPassGate.ResetToPlain(wasEnabled: false, lineWork: false, isFloor: true, changed: 0));

        Assert.False(CornerPassGate.ResetToPlain(wasEnabled: true, lineWork: false, isFloor: true, changed: 0));
        Assert.False(CornerPassGate.ResetToPlain(wasEnabled: false, lineWork: true, isFloor: true, changed: 0));
        Assert.False(CornerPassGate.ResetToPlain(wasEnabled: false, lineWork: false, isFloor: false, changed: 0));
        Assert.False(CornerPassGate.ResetToPlain(wasEnabled: false, lineWork: false, isFloor: true, changed: 1));
    }
}
