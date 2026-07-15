using System.Collections.Generic;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class DwgExportPlannerTests
{
    private static PlannedExportFile File(string name)
        => new PlannedExportFile { ViewLabel = name, FileName = name };

    [Fact]
    public void Check_CleanPlan_HasNoDuplicatesOrExisting()
    {
        var files = new List<PlannedExportFile> { File("A-101"), File("A-102") };

        var plan = DwgExportPlanner.Check(files, _ => false);

        Assert.Same(files, plan.Files);
        Assert.Empty(plan.DuplicateNames);
        Assert.Empty(plan.ExistingFileNames);
    }

    [Fact]
    public void Check_ReportsCaseInsensitiveDuplicates()
    {
        var plan = DwgExportPlanner.Check(
            new List<PlannedExportFile> { File("A-101"), File("a-101"), File("A-102") },
            _ => false);

        Assert.Equal(new[] { "A-101" }, plan.DuplicateNames);
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
    public void SheetSetItem_Display_ShowsCount()
    {
        Assert.Equal("Publish (12 sheets)", new SheetSetItem { Name = "Publish", SheetCount = 12 }.Display);
        Assert.Equal("One (1 sheet)", new SheetSetItem { Name = "One", SheetCount = 1 }.Display);
    }
}
