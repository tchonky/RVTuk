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

        Assert.Contains("2 lines skipped: '42,7x', '' are not heights.", summary);
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

        Assert.Contains("1 line skipped: 'abc' is not a height.", report.Summarise());
    }

    [Fact]
    public void NothingOverTheTarget()
    {
        var report = new TopoSessionReport();

        Assert.Equal("No topo line in this view lies over the selected element.", report.Summarise());
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

        Assert.Equal("1 line skipped: 'x' is not a height.", summary);
        Assert.DoesNotContain("applied", summary);
        Assert.DoesNotContain("No topo line", summary);
    }

    [Fact]
    public void CleanRunHasNoProblems()
    {
        var report = new TopoSessionReport { LinesApplied = 2, PointsAdded = 4, PointsReplaced = 1 };

        Assert.False(report.HasProblems);
    }
}
