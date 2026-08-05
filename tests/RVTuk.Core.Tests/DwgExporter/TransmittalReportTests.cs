using System;
using System.Collections.Generic;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class TransmittalReportTests
{
    private static TransmittalInfo Info() => new TransmittalInfo
    {
        CreatedUtc = new DateTime(2026, 8, 4, 10, 30, 0, DateTimeKind.Utc),
        SheetSetName = "Issue 01",
        ModelTitles = new List<string> { "Tower-A.rvt", "Tower-B.rvt" },
        DwgSetupName = "Office standard",
        SheetNamingSetupName = "Sheets rule",
        ViewNamingSetupName = "<View Name>",
    };

    private static TransmittalEntry Entry(string name, TransmittalKind kind)
        => new TransmittalEntry { SourcePath = @"D:\out\" + name, EntryName = name, Kind = kind };

    [Fact]
    public void Render_NamesTheRangeTheModelsAndTheSetups()
    {
        var text = TransmittalReport.Render(Info(), new TransmittalContents());

        Assert.Contains("Issue 01", text);
        Assert.Contains("Tower-A.rvt", text);
        Assert.Contains("Tower-B.rvt", text);
        Assert.Contains("Office standard", text);
        Assert.Contains("Sheets rule", text);
    }

    [Fact]
    public void Render_GroupsEntriesByKind()
    {
        var contents = new TransmittalContents
        {
            Entries = new List<TransmittalEntry>
            {
                Entry("A-101.dwg", TransmittalKind.Drawing),
                Entry("logo.png", TransmittalKind.Image),
                Entry("romans.shx", TransmittalKind.Font),
            },
        };

        var text = TransmittalReport.Render(Info(), contents);

        Assert.Contains("Drawings", text);
        Assert.Contains("A-101.dwg", text);
        Assert.Contains("Images", text);
        Assert.Contains("logo.png", text);
        Assert.Contains("Fonts", text);
        Assert.Contains("romans.shx", text);
    }

    [Fact]
    public void Render_ListsFontsTheReceiverMustSupply_WithTheReasonWhy()
    {
        var contents = new TransmittalContents
        {
            FontsNotIncluded = new List<string> { "Arial", "Calibri" },
        };

        var text = TransmittalReport.Render(Info(), contents);

        Assert.Contains("Arial", text);
        Assert.Contains("Calibri", text);
        // The receiver has to understand these are deliberately absent, not forgotten.
        Assert.Contains("licence", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_NamesTheDrawing_WhenTheArchiveIsForJustOne()
    {
        var info = Info();
        info.DrawingName = "A-101";

        Assert.Contains("A-101", TransmittalReport.Render(info, new TransmittalContents()));
    }

    [Fact]
    public void Render_IncludesWarnings()
    {
        var contents = new TransmittalContents
        {
            Warnings = new List<string> { "No AutoCAD font folder found on this machine." },
        };

        var text = TransmittalReport.Render(Info(), contents);

        Assert.Contains("No AutoCAD font folder found", text);
    }

    [Fact]
    public void Render_OmitsEmptySections()
    {
        // A bundle of plain drawings shouldn't show empty Images/Fonts headings.
        var contents = new TransmittalContents
        {
            Entries = new List<TransmittalEntry> { Entry("A-101.dwg", TransmittalKind.Drawing) },
        };

        var text = TransmittalReport.Render(Info(), contents);

        Assert.DoesNotContain("Images", text);
        Assert.DoesNotContain("Warnings", text);
    }
}
