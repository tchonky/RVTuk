using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ConvertReportTests
{
    [Fact]
    public void CountsEachDirectionWithItsOwnPlural()
    {
        var report = new ConvertReport { FloorsToToposolids = 2, ToposolidsToFloors = 1 };

        Assert.Equal("2 floors → toposolids, 1 toposolid → floor.", report.Summarise());
    }

    [Fact]
    public void ARunThatConvertedNothingSaysSoRatherThanCountingZero()
    {
        var report = new ConvertReport();
        report.Skipped.Add("Toposolid 5521: it is a subdivision");

        Assert.Equal("Nothing converted. Skipped 1: Toposolid 5521: it is a subdivision.", report.Summarise());
    }

    [Fact]
    public void ListsSkipsLossesAndDismissedWarnings()
    {
        var report = new ConvertReport
        {
            FloorsToToposolids = 1,
            PointsLost = 3,
            CreasesLost = 1,
            ParametersNotCarried = 2,
            WarningsDismissed = 1,
        };
        report.Skipped.Add("Floor 418732: it is in a model group");
        report.Skipped.Add("Floor 418733: kept its 2 hosted elements");

        Assert.Equal(
            "1 floor → toposolid. " +
            "Skipped 2: Floor 418732: it is in a model group; Floor 418733: kept its 2 hosted elements. " +
            "3 shape points and 1 split line could not be copied. " +
            "2 parameter values not carried. " +
            "1 Revit warning dismissed.",
            report.Summarise());
    }

    [Fact]
    public void ASkipReasonEndingInAFullStopIsNotDoubled()
    {
        var report = new ConvertReport();
        report.Skipped.Add("Floor 1: Revit could not create the toposolid.");
        report.Skipped.Add("Floor 2: Its sketch is invalid. ");

        Assert.Equal(
            "Nothing converted. Skipped 2: Floor 1: Revit could not create the toposolid; Floor 2: Its sketch is invalid.",
            report.Summarise());
    }

    [Theory]
    [InlineData(2, 0, 0, 0, "2 hosted elements")]
    [InlineData(0, 1, 0, 0, "1 tag or dimension")]
    [InlineData(1, 0, 3, 0, "1 hosted element and 3 subdivisions")]
    [InlineData(2, 3, 1, 0, "2 hosted elements, 3 tags or dimensions and 1 subdivision")]
    [InlineData(0, 0, 0, 1, "1 part")]
    [InlineData(1, 0, 0, 6, "1 hosted element and 6 parts")]
    [InlineData(0, 0, 0, 0, "")]
    public void DescribesOnlyTheDependentsThereAre(
        int hosted, int annotations, int subdivisions, int parts, string expected)
    {
        Assert.Equal(expected, ConvertReport.DescribeDependents(hosted, annotations, subdivisions, parts));
    }
}
