using System.Globalization;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoSessionReportTests
{
    [Fact]
    public void CountsAppliedLines()
    {
        var report = new TopoSessionReport { LinesApplied = 3, PointsAdded = 40, PointsReplaced = 38 };

        Assert.Equal("3 lines applied: 40 points added, 38 replaced.", report.Summarise());
    }

    [Fact]
    public void NamesEachUnreadableHeight()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2 };
        report.UnreadableHeights.Add("42,7x");
        report.UnreadableHeights.Add("");

        var summary = report.Summarise();

        Assert.Contains("2 lines skipped: '42,7x', '' are not heights in metres.", summary);
        Assert.True(report.HasProblems);
    }

    [Fact]
    public void SingularForOne()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 1 };

        Assert.Equal("1 line applied: 1 point added, 0 replaced.", report.Summarise());
    }

    [Fact]
    public void SingularSkippedSentenceForOneUnreadableHeight()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 1 };
        report.UnreadableHeights.Add("abc");

        Assert.Contains("1 line skipped: 'abc' is not a height in metres.", report.Summarise());
    }

    [Fact]
    public void NothingOverTheTarget()
    {
        var report = new TopoSessionReport();

        Assert.Equal("No topo line in the session's view lies over the toposolid or floor being edited.", report.Summarise());
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void MentionsPointsMovedByHandAndLost()
    {
        var report = new TopoSessionReport
        {
            LinesApplied = 1,
            PointsAdded = 5,
            PointsMovedByHand = 2,
            PointsLost = 1,
        };

        var summary = report.Summarise();

        Assert.Contains("2 earlier points had been moved by hand and were left alone", summary);
        Assert.Contains("1 point could not be placed", summary);
        Assert.True(report.HasProblems);
    }

    [Fact]
    public void SingularMovedByHandSentence()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsMovedByHand = 1 };

        Assert.Contains("1 earlier point had been moved by hand and was left alone.", report.Summarise());
    }

    [Fact]
    public void UnlinkedSentenceForOneText()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 1, UnlinkedTexts = 1 };

        Assert.Contains(
            "1 height text is not linked to any line and was left alone.",
            report.Summarise());
        Assert.True(report.HasProblems);
    }

    [Fact]
    public void UnlinkedSentenceForSeveralTexts()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 1, UnlinkedTexts = 2 };

        Assert.Contains(
            "2 height texts are not linked to any line and were left alone.",
            report.Summarise());
    }

    [Fact]
    public void ZeroAppliedWithOneUnreadableHeightOmitsAppliedAndNothingOverSentences()
    {
        var report = new TopoSessionReport { LinesApplied = 0 };
        report.UnreadableHeights.Add("x");

        var summary = report.Summarise();

        Assert.Equal("1 line skipped: 'x' is not a height in metres.", summary);
        Assert.DoesNotContain("applied", summary);
        Assert.DoesNotContain("No topo line", summary);
    }

    [Fact]
    public void SaysHowManyCrossingPointsTookTheHigherHeight()
    {
        var report = new TopoSessionReport { LinesApplied = 2, PointsAdded = 7, PointsDroppedAtCrossings = 3 };

        Assert.Equal(
            "2 lines applied: 7 points added, 0 replaced. 3 points where lines cross took the higher line's height.",
            report.Summarise());
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void SingularCrossingSentence()
    {
        var report = new TopoSessionReport { LinesApplied = 2, PointsAdded = 3, PointsDroppedAtCrossings = 1 };

        Assert.Contains("1 point where lines cross took the higher line's height.", report.Summarise());
    }

    [Fact]
    public void NoCrossingSentenceWhenNoneWasDropped()
    {
        var report = new TopoSessionReport { LinesApplied = 2, PointsAdded = 3 };

        Assert.DoesNotContain("cross", report.Summarise());
    }

    [Fact]
    public void CleanRunHasNoProblems()
    {
        var report = new TopoSessionReport { LinesApplied = 2, PointsAdded = 4, PointsReplaced = 1 };

        Assert.False(report.HasProblems);
    }

    private const string Marker =
        "The Survey Point marker itself sits at +10.00 m; heights are measured like a Survey Point level.";

    [Fact]
    public void MarkerNoteComesLastAndIsNoProblem()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = 10 };
        Assert.Equal("1 line applied: 2 points added, 0 replaced. " + Marker, report.Summarise());
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void MarkerNoteForAMarkerBelowZero()
    {
        var report = new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = -3.2 };
        Assert.Contains("The Survey Point marker itself sits at -3.20 m;", report.Summarise());
    }

    [Fact]
    public void NothingOverTheTargetKeepsTheMarkerNote()
    {
        var report = new TopoSessionReport { SurveyPointMarkerMetres = 10 };
        Assert.Equal(TopoSessionReport.NothingOverTarget + " " + Marker, report.Summarise());
    }

    [Fact]
    public void MarkerNoteForAMarkerAFewMillimetresOff()
    {
        Assert.Contains("The Survey Point marker itself sits at -0.003 m;",
                        new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = -0.003 }.Summarise());
        Assert.Contains("The Survey Point marker itself sits at +0.003 m;",
                        new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = 0.003 }.Summarise());
    }

    [Theory]
    [InlineData(0.0004)]
    [InlineData(-0.0004)]
    [InlineData(-0.0000001)]
    public void NoMarkerNoteWhenTheMarkerRoundsToZero(double metres)
    {
        var applied = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = metres };
        Assert.Equal("1 line applied: 2 points added, 0 replaced.", applied.Summarise());
        Assert.Equal(TopoSessionReport.NothingOverTarget,
                     new TopoSessionReport { SurveyPointMarkerMetres = metres }.Summarise());
    }

    [Fact]
    public void MarkerFigureIsNeverZeroOrDoubleSigned()
    {
        for (int i = -200; i <= 200; i++)
        {
            var summary = new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = i / 10000.0 }.Summarise();
            Assert.DoesNotContain("-+", summary);
            Assert.DoesNotContain("sits at +0.00 m", summary);
            Assert.DoesNotContain("sits at -0.00 m", summary);
        }
    }

    [Theory]
    [InlineData(0.0005, "+0.001")]
    [InlineData(-0.0005, "-0.001")]
    [InlineData(0.0007, "+0.001")]
    [InlineData(-0.0009, "-0.001")]
    [InlineData(0.0025, "+0.003")]
    [InlineData(-0.0025, "-0.003")]
    [InlineData(1234.5, "+1234.50")]
    public void MarkerFigureIsTheHeightRoundedHalfAwayFromZeroToTheMillimetre(double metres, string figure)
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = metres };
        Assert.Equal(
            "1 line applied: 2 points added, 0 replaced. The Survey Point marker itself sits at " + figure +
            " m; heights are measured like a Survey Point level.",
            report.Summarise());
    }

    [Fact]
    public void MarkerNoteComesAfterEveryOtherSentence()
    {
        var report = new TopoSessionReport
        {
            LinesApplied = 2,
            PointsAdded = 7,
            PointsDroppedAtCrossings = 1,
            PointsMovedByHand = 1,
            PointsLost = 1,
            UnlinkedTexts = 1,
            SurveyPointMarkerMetres = 10,
        };
        report.UnreadableHeights.Add("x");

        Assert.Equal(
            "2 lines applied: 7 points added, 0 replaced. " +
            "1 point where lines cross took the higher line's height. " +
            "1 line skipped: 'x' is not a height in metres. " +
            "1 earlier point had been moved by hand and was left alone. " +
            "1 point could not be placed. " +
            "1 height text is not linked to any line and was left alone. " +
            Marker,
            report.Summarise());
    }

    [Fact]
    public void MarkerNoteFollowsTheProblemsWhenNoLineWasApplied()
    {
        var report = new TopoSessionReport { SurveyPointMarkerMetres = 10 };
        report.UnreadableHeights.Add("x");

        Assert.Equal("1 line skipped: 'x' is not a height in metres. " + Marker, report.Summarise());
        Assert.True(report.HasProblems);
    }

    [Fact]
    public void MarkerFigureIgnoresTheCurrentCulture()
    {
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = comma;
            CultureInfo.CurrentUICulture = comma;

            var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = 10 };
            Assert.Equal("1 line applied: 2 points added, 0 replaced. " + Marker, report.Summarise());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void PointsMovedByHandAloneAreAProblem()
    {
        Assert.True(new TopoSessionReport { LinesApplied = 1, PointsMovedByHand = 1 }.HasProblems);
    }

    [Fact]
    public void PointsLostAloneAreAProblem()
    {
        Assert.True(new TopoSessionReport { LinesApplied = 1, PointsLost = 1 }.HasProblems);
    }

    [Fact]
    public void AnUnreadableHeightAloneShowsTheSummaryAfterUpdate()
    {
        var report = new TopoSessionReport { LinesApplied = 1 };
        report.UnreadableHeights.Add("x");

        Assert.True(report.ShowAfterUpdate);
    }

    [Fact]
    public void APointMovedByHandAloneShowsTheSummaryAfterUpdate()
    {
        Assert.True(new TopoSessionReport { LinesApplied = 1, PointsMovedByHand = 1 }.ShowAfterUpdate);
    }

    [Fact]
    public void APointLostAloneShowsTheSummaryAfterUpdate()
    {
        Assert.True(new TopoSessionReport { LinesApplied = 1, PointsLost = 1 }.ShowAfterUpdate);
    }

    [Fact]
    public void AnUnlinkedTextAloneShowsTheSummaryAfterUpdate()
    {
        Assert.True(new TopoSessionReport { LinesApplied = 1, UnlinkedTexts = 1 }.ShowAfterUpdate);
    }

    [Fact]
    public void NothingAppliedShowsTheSummaryAfterUpdate()
    {
        var report = new TopoSessionReport();

        Assert.False(report.HasProblems);
        Assert.True(report.ShowAfterUpdate);
    }

    [Fact]
    public void AnUpdateWithNothingToReportShowsNoSummary()
    {
        Assert.False(new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, PointsReplaced = 1 }.ShowAfterUpdate);
    }

    [Fact]
    public void PointsDroppedAtCrossingsShowNoSummaryAfterUpdate()
    {
        Assert.False(new TopoSessionReport { LinesApplied = 2, PointsAdded = 7, PointsDroppedAtCrossings = 3 }.ShowAfterUpdate);
    }

    [Fact]
    public void AMarkerFigureShowsNoSummaryAfterUpdate()
    {
        var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = 10 };

        Assert.Contains(Marker, report.Summarise());
        Assert.False(report.ShowAfterUpdate);
    }
}
