using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class PerDrawingTransmittalTests
{
    private static DrawingProduction Produced(string fileName, params string[] files)
        => new DrawingProduction
        {
            File = new PlannedExportFile { FileName = fileName, ViewLabel = fileName },
            ProducedFiles = files,
        };

    [Fact]
    public void Plan_NamesEachArchiveAfterItsDrawing()
    {
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\A-101_logo.png"),
            Produced("A-102", @"D:\out\A-102.dwg"),
        });

        Assert.Equal(
            new[] { @"D:\out\A-101.zip", @"D:\out\A-102.zip" },
            plan.Select(b => b.ArchivePath));
    }

    [Fact]
    public void Plan_GivesEachDrawingItsOwnDrawingsAndNotAnothers()
    {
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg"),
            Produced("A-102", @"D:\out\A-102.dwg"),
        });

        Assert.Equal(new[] { @"D:\out\A-101.dwg" }, plan[0].Files);
        Assert.Equal(new[] { @"D:\out\A-102.dwg" }, plan[1].Files);
    }

    [Fact]
    public void Plan_SharesEveryImageWithEveryArchive()
    {
        // Revit writes one copy of an image for the whole run, during whichever drawing used
        // it first (confirmed against a real export, 2026-08-05). Without a DWG reader we
        // can't learn who else references it, so every archive carries every image —
        // duplicating bytes beats shipping a broken link.
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\logo.png"),
            Produced("A-102", @"D:\out\A-102.dwg"),
        });

        Assert.Contains(@"D:\out\logo.png", plan[0].Files);
        Assert.Contains(@"D:\out\logo.png", plan[1].Files);
    }

    [Fact]
    public void Plan_SharedImageIsNotDuplicatedWithinOneArchive()
    {
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\logo.png"),
        });

        Assert.Single(plan[0].Files, f => f == @"D:\out\logo.png");
    }

    [Fact]
    public void Plan_ADrawingThatProducedNoDrawingFile_GetsNoArchive()
    {
        // Shared images must not resurrect a drawing that produced nothing of its own —
        // an archive holding only other sheets' images would be a lie.
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\logo.png"),
            Produced("A-102", @"D:\out\A-102.pdf"),
        });

        Assert.Single(plan);
        Assert.Equal(@"D:\out\A-101.zip", plan[0].ArchivePath);
    }

    [Fact]
    public void Plan_SkipsADrawingThatProducedNothing()
    {
        // A view whose export failed leaves no files; an empty zip would be a lie.
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg"),
            Produced("A-102"),
        });

        Assert.Single(plan);
        Assert.Equal(@"D:\out\A-101.zip", plan[0].ArchivePath);
    }

    [Fact]
    public void Plan_SkipsADrawingThatProducedOnlyAPdf()
    {
        // PDF-only output is not a CAD transmittal; nothing to bundle.
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.pdf"),
        });

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_NeverBundlesAnArchiveIntoAnother()
    {
        // A previous run's zip in the folder must not be swept into this one's.
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\A-101.zip"),
        });

        Assert.Equal(new[] { @"D:\out\A-101.dwg" }, plan[0].Files);
    }

    [Fact]
    public void Plan_NothingProduced_YieldsNoBundles()
    {
        Assert.Empty(PerDrawingTransmittal.Plan(@"D:\out", new List<DrawingProduction>()));
    }

    [Fact]
    public void Plan_XrefDrawingsStayWithTheSheetThatProducedThem()
    {
        // MergedViews off: a sheet's export emits the sheet plus one DWG per view on it, and
        // those must travel with that sheet or its xrefs resolve to nothing.
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\A-101-Plan.dwg"),
            Produced("A-102", @"D:\out\A-102.dwg"),
        });

        Assert.Equal(new[] { @"D:\out\A-101.dwg", @"D:\out\A-101-Plan.dwg" }, plan[0].Files);
        Assert.DoesNotContain(@"D:\out\A-101-Plan.dwg", plan[1].Files);
    }

    [Fact]
    public void Plan_KeepsTheDrawingsOwnPdfOutOfItsArchive_ButStillBundlesTheRest()
    {
        var plan = PerDrawingTransmittal.Plan(@"D:\out", new[]
        {
            Produced("A-101", @"D:\out\A-101.dwg", @"D:\out\A-101.pdf", @"D:\out\A-101_logo.png"),
        });

        Assert.Equal(new[] { @"D:\out\A-101.dwg", @"D:\out\A-101_logo.png" }, plan[0].Files);
    }
}
