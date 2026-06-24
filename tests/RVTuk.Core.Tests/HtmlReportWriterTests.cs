using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Reporting;
using Xunit;

namespace RVTuk.Core.Tests;

public class HtmlReportWriterTests
{
    private static ComparisonResult Sample()
    {
        var result = new ComparisonResult
        {
            SideA = new SnapshotMeta { SourceName = "Project Alpha" },
            SideB = new SnapshotMeta { SourceName = "The Standard" },
        };
        var cat = new CategoryDiffResult { CategoryId = "ViewTemplates", DisplayName = "View Templates" };
        cat.Items.Add(new ItemDiff
        {
            DisplayName = "Floor Plan - Working",
            Kind = DiffKind.Changed,
            CompletenessA = 0.6,
            CompletenessB = 0.8,
            Fields = { new FieldDiff("VIEW_SCALE", "Scale", "1:100", "1:50") },
        });
        cat.Items.Add(new ItemDiff { DisplayName = "Ceiling Plan", Kind = DiffKind.Added, CompletenessA = 1.0 });
        cat.Summary.Changed = 1;
        cat.Summary.Added = 1;
        result.Categories.Add(cat);
        return result;
    }

    [Fact]
    public void Report_IsSingleHtmlDocument()
    {
        var html = HtmlReportWriter.Write(Sample());
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void Report_ContainsModelNamesAndItems()
    {
        var html = HtmlReportWriter.Write(Sample());
        Assert.Contains("Project Alpha", html);
        Assert.Contains("The Standard", html);
        Assert.Contains("Floor Plan - Working", html);
        Assert.Contains("Ceiling Plan", html);
    }

    [Fact]
    public void Report_ShowsDiffMarkersAndRecommendation()
    {
        var html = HtmlReportWriter.Write(Sample());
        Assert.Contains("Changed", html);
        Assert.Contains("Only in A", html);
        Assert.Contains("B is more complete", html);
        Assert.Contains("Scale", html);
    }

    [Fact]
    public void Report_EncodesHtmlSpecialChars()
    {
        var result = new ComparisonResult
        {
            SideA = new SnapshotMeta { SourceName = "A <b>x</b>" },
            SideB = new SnapshotMeta { SourceName = "B" },
        };
        var html = HtmlReportWriter.Write(result);
        Assert.Contains("A &lt;b&gt;x&lt;/b&gt;", html);
    }
}
