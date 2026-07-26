using System;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class SaveToLibraryPlannerTests
{
    private static (string? Category, string RelativePath)[] Items(params (string? c, string p)[] items)
        => Array.ConvertAll(items, i => ((string?)i.c, i.p));

    [Fact]
    public void Plan_UsesMostCommonFolderOfSameCategory()
    {
        var items = Items(("Doors", @"05_Doors\A.rfa"), ("Doors", @"05_Doors\B.rfa"),
                          ("Doors", @"Misc\C.rfa"), ("Windows", @"06_Windows\D.rfa"));
        var (path, error) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
        Assert.Null(error);
        Assert.Equal(@"05_Doors\New Door.rfa", path);
    }

    [Fact]
    public void Plan_CategoryCompareIsCaseInsensitive()
    {
        var items = Items(("doors", @"05_Doors\A.rfa"));
        var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
        Assert.Equal(@"05_Doors\New Door.rfa", path);
    }

    [Fact]
    public void Plan_TieBreaksOnOrdinalFolderName()
    {
        var items = Items(("Doors", @"B_Doors\A.rfa"), ("Doors", @"A_Doors\B.rfa"));
        var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
        Assert.Equal(@"A_Doors\New Door.rfa", path);
    }

    [Fact]
    public void Plan_HandlesForwardSlashSeparators()
    {
        var items = Items(("Doors", "05_Doors/Interior/A.rfa"));
        var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
        Assert.Equal(@"05_Doors/Interior\New Door.rfa", path);
    }

    [Fact]
    public void Plan_NoSameCategoryItems_FallsBackToCategoryNamedFolder()
    {
        var items = Items(("Windows", @"06_Windows\D.rfa"));
        var (path, error) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
        Assert.Null(error);
        Assert.Equal(@"Doors\New Door.rfa", path);
    }

    [Fact]
    public void Plan_SanitizesCategoryFolderName()
    {
        var (path, _) = SaveToLibraryPlanner.Plan("Fitting", "Pipe/Duct: Special",
            Items(("Doors", @"05_Doors\A.rfa")));
        Assert.Equal(@"PipeDuct Special\Fitting.rfa", path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("///")] // sanitises to empty
    public void Plan_NoUsableCategory_SavesAtLibraryRoot(string? category)
    {
        var (path, error) = SaveToLibraryPlanner.Plan("New Door", category, Items());
        Assert.Null(error);
        Assert.Equal("New Door.rfa", path);
    }

    [Fact]
    public void Plan_RootDwellingCategoryWinsOverSubfolder()
    {
        var items = Items(("Doors", "A.rfa"), ("Doors", "B.rfa"), ("Doors", @"Misc\C.rfa"));
        var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
        Assert.Equal("New Door.rfa", path); // most common folder is the root
    }

    [Fact]
    public void Plan_RejectsIllegalFamilyName()
    {
        var (path, error) = SaveToLibraryPlanner.Plan("24\" Door", "Doors", Items());
        Assert.Null(path);
        Assert.NotNull(error);
        Assert.Contains("file name", error, StringComparison.OrdinalIgnoreCase);
    }
}
