using System.Linq;
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class RoundingReportTests
{
    [Fact]
    public void CountsAddUp()
    {
        var builder = new RoundingReportBuilder();
        builder.CountExamined();
        builder.RecordMoved();
        builder.CountExamined();
        builder.RecordAlreadyClean();
        builder.CountExamined();
        builder.RecordSkipped(1234, "Walls", SkipReason.NotOrthogonal);

        var report = builder.Build();

        Assert.Equal(3, report.Examined);
        Assert.Equal(1, report.Moved);
        Assert.Equal(1, report.AlreadyClean);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public void SkipsAreGroupedByReasonWithTheirDescription()
    {
        var builder = new RoundingReportBuilder();
        builder.RecordSkipped(1, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(2, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(3, "Furniture", SkipReason.InGroup);

        var groups = builder.Build().GroupedByReason();

        Assert.Equal(2, groups.Count);
        var notOrthogonal = groups.Single(g => g.Reason == SkipReason.NotOrthogonal);
        Assert.Equal(2, notOrthogonal.Elements.Count);
        Assert.False(string.IsNullOrWhiteSpace(notOrthogonal.Description));
    }

    [Fact]
    public void TheLargestGroupComesFirstSoTheWorstProblemIsVisible()
    {
        var builder = new RoundingReportBuilder();
        builder.RecordSkipped(1, "Furniture", SkipReason.InGroup);
        builder.RecordSkipped(2, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(3, "Walls", SkipReason.NotOrthogonal);

        var groups = builder.Build().GroupedByReason();

        Assert.Equal(SkipReason.NotOrthogonal, groups[0].Reason);
    }

    [Fact]
    public void ReleasedConstraintsAreRecordedSeparatelyFromSkips()
    {
        // Nothing in a Revit model shows what used to be constrained, so this list is the only
        // record that the change happened.
        var builder = new RoundingReportBuilder();
        builder.RecordReleasedConstraint(42, "Doors", "Unlocked dimension 991");

        var report = builder.Build();

        Assert.Empty(report.Skipped);
        Assert.Single(report.ReleasedConstraints);
        Assert.Equal(42, report.ReleasedConstraints[0].ElementId);
    }

    [Fact]
    public void EverySkipReasonHasADescription()
    {
        foreach (SkipReason reason in System.Enum.GetValues(typeof(SkipReason)))
        {
            Assert.False(string.IsNullOrWhiteSpace(SkipReasonText.Describe(reason)),
                $"{reason} has no description");
        }
    }
}
