using System.Collections.Generic;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class DwgExportPlannerTests
{
    private static PlannedExportFile File(string name, string model = "")
        => new PlannedExportFile { ViewLabel = name, FileName = name, ModelTitle = model };

    [Fact]
    public void Check_CleanPlan_HasNoDuplicatesOrExisting()
    {
        var files = new List<PlannedExportFile> { File("A-101"), File("A-102") };

        var plan = DwgExportPlanner.Check(files, _ => false);

        Assert.Same(files, plan.Files);
        Assert.Empty(plan.Duplicates);
        Assert.Empty(plan.ExistingFileNames);
    }

    [Fact]
    public void Check_ReportsCaseInsensitiveDuplicates_UnderTheFirstSpelling()
    {
        var plan = DwgExportPlanner.Check(
            new List<PlannedExportFile> { File("A-101"), File("a-101"), File("A-102"), File("A-101") },
            _ => false);

        var duplicate = Assert.Single(plan.Duplicates);
        Assert.Equal("A-101", duplicate.FileName);
        Assert.Equal(3, duplicate.Sources.Count);
    }

    [Fact]
    public void Check_DuplicatesAcrossModels_NameTheModels()
    {
        var plan = DwgExportPlanner.Check(
            new List<PlannedExportFile> { File("A-101", "Tower-A.rvt"), File("A-101", "Tower-B.rvt") },
            _ => false);

        var duplicate = Assert.Single(plan.Duplicates);
        Assert.Equal(new[] { "Tower-A.rvt — A-101", "Tower-B.rvt — A-101" }, duplicate.Sources);
    }

    [Fact]
    public void Check_AllUnique_ReportsNoDuplicates()
    {
        Assert.Empty(
            DwgExportPlanner.Check(
                new List<PlannedExportFile> { File("A-101"), File("A-102") }, _ => false).Duplicates);
    }

    [Fact]
    public void Check_ReportsFilesAlreadyOnDisk()
    {
        var plan = DwgExportPlanner.Check(
            new List<PlannedExportFile> { File("A-101"), File("A-102") },
            name => name == "A-102");

        Assert.Equal(new[] { "A-102" }, plan.ExistingFileNames);
    }

    [Fact]
    public void DwgExportRequest_Defaults_DwgOnPdfOff()
    {
        var request = new DwgExportRequest();

        Assert.True(request.ExportDwg);
        Assert.False(request.ExportPdf);
    }

    [Fact]
    public void SheetSetItem_Display_ShowsCount()
    {
        Assert.Equal("Publish (12 sheets)", new SheetSetItem { Name = "Publish", SheetCount = 12 }.Display);
        Assert.Equal("One (1 sheet)", new SheetSetItem { Name = "One", SheetCount = 1 }.Display);
    }

    [Fact]
    public void DwgExportRequest_Defaults_ViewsUseTheViewName()
    {
        Assert.Equal("<View Name>", DwgExportDefaults.ViewNameNamingName);
        Assert.Equal(DwgExportDefaults.ViewNameNamingName, new DwgExportRequest().ViewNamingSetupName);
        Assert.False(new DwgExportRequest().SeparatePdfFolder);
        Assert.False(new DwgExportRequest().CopyMissingSetups);
        Assert.Empty(new DwgExportRequest().ExtraModelKeys);
    }

    [Fact]
    public void PdfFolder_IsTheOutputFolder_UnlessASeparateOneIsAskedForAndSet()
    {
        var shared = new DwgExportRequest { OutputFolder = @"D:\out", PdfOutputFolder = @"D:\pdf" };
        Assert.Equal(@"D:\out", shared.PdfFolder); // checkbox off: the second path is ignored

        var split = new DwgExportRequest
        {
            OutputFolder = @"D:\out", PdfOutputFolder = @"D:\pdf", SeparatePdfFolder = true,
        };
        Assert.Equal(@"D:\pdf", split.PdfFolder);

        var splitButBlank = new DwgExportRequest { OutputFolder = @"D:\out", SeparatePdfFolder = true };
        Assert.Equal(@"D:\out", splitButBlank.PdfFolder);
    }

    [Fact]
    public void PlannedExportFile_Source_NamesTheModelOnlyWhenThereIsOne()
    {
        Assert.Equal("A-101", new PlannedExportFile { ViewLabel = "A-101" }.Source);
        Assert.Equal(
            "Tower-A.rvt — A-101",
            new PlannedExportFile { ModelTitle = "Tower-A.rvt", ViewLabel = "A-101" }.Source);
    }
}
