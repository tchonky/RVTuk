using System.Collections.Generic;
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class ModelSetupResolverTests
{
    private static ModelSetupInventory Model(bool readOnly = false) => new ModelSetupInventory
    {
        Key = @"C:\Projects\Tower-B.rvt",
        Title = "Tower-B.rvt",
        IsReadOnly = readOnly,
        SheetSetNames = new List<string> { "Issue 01" },
        PdfSetupNames = new List<string> { "Sheets rule", "Views rule" },
        DwgSetupNames = new List<string> { "Office" },
    };

    private static DwgExportRequest Request() => new DwgExportRequest
    {
        SheetSetName = "Issue 01",
        SheetNamingSetupName = "Sheets rule",
        ViewNamingSetupName = "Views rule",
        DwgSetupName = "Office",
        ExportDwg = true,
    };

    [Fact]
    public void EveryNameMatches_RunsAndCopiesNothing()
    {
        var plan = ModelSetupResolver.Resolve(Model(), Request());

        Assert.True(plan.CanRun);
        Assert.Equal("", plan.SkipReason);
        Assert.Empty(plan.PdfSetupsToCopy);
        Assert.False(plan.CopyDwgSetup);
        Assert.Equal("Tower-B.rvt", plan.Title);
        Assert.Equal(@"C:\Projects\Tower-B.rvt", plan.Key);
    }

    [Fact]
    public void NamesMatchCaseInsensitively()
    {
        var request = Request();
        request.SheetNamingSetupName = "SHEETS RULE";
        request.SheetSetName = "issue 01";

        Assert.True(ModelSetupResolver.Resolve(Model(), request).CanRun);
    }

    [Fact]
    public void MissingSheetSet_SkipsWhateverTheCopyPolicy()
    {
        var model = Model();
        model.SheetSetNames = new List<string> { "Something else" };
        var request = Request();
        request.CopyMissingSetups = true;

        var plan = ModelSetupResolver.Resolve(model, request);

        Assert.False(plan.CanRun);
        Assert.Contains("Issue 01", plan.SkipReason);
        Assert.Contains("view/sheet set", plan.SkipReason);
    }

    [Fact]
    public void MissingPdfSetup_WithCopyingOff_Skips()
    {
        var model = Model();
        model.PdfSetupNames = new List<string> { "Views rule" };

        var plan = ModelSetupResolver.Resolve(model, Request());

        Assert.False(plan.CanRun);
        Assert.Contains("Sheets rule", plan.SkipReason);
    }

    [Fact]
    public void MissingPdfSetup_WithCopyingOn_RunsAndQueuesTheCopy()
    {
        var model = Model();
        model.PdfSetupNames = new List<string> { "Views rule" };
        var request = Request();
        request.CopyMissingSetups = true;

        var plan = ModelSetupResolver.Resolve(model, request);

        Assert.True(plan.CanRun);
        Assert.Equal(new[] { "Sheets rule" }, plan.PdfSetupsToCopy);
        Assert.False(plan.CopyDwgSetup);
    }

    [Fact]
    public void MissingDwgSetup_WithCopyingOn_QueuesTheDwgCopy()
    {
        var model = Model();
        model.DwgSetupNames = new List<string>();
        var request = Request();
        request.CopyMissingSetups = true;

        var plan = ModelSetupResolver.Resolve(model, request);

        Assert.True(plan.CanRun);
        Assert.True(plan.CopyDwgSetup);
        Assert.Empty(plan.PdfSetupsToCopy);
    }

    [Fact]
    public void ReadOnlyModel_CannotBeCopiedInto_SoItSkips()
    {
        var model = Model(readOnly: true);
        model.PdfSetupNames = new List<string> { "Views rule" };
        var request = Request();
        request.CopyMissingSetups = true;

        var plan = ModelSetupResolver.Resolve(model, request);

        Assert.False(plan.CanRun);
        Assert.Contains("read-only", plan.SkipReason);
    }

    [Fact]
    public void ReadOnlyModel_ThatNeedsNothing_StillRuns()
    {
        Assert.True(ModelSetupResolver.Resolve(Model(readOnly: true), Request()).CanRun);
    }

    [Fact]
    public void DwgSetupIsNotRequired_WhenDwgIsNotBeingExported()
    {
        var model = Model();
        model.DwgSetupNames = new List<string>();
        var request = Request();
        request.ExportDwg = false;
        request.ExportPdf = true;

        Assert.True(ModelSetupResolver.Resolve(model, request).CanRun);
    }

    [Fact]
    public void BuiltInSentinels_NeverNeedAnythingFromTheModel()
    {
        var model = Model();
        model.PdfSetupNames = new List<string>();
        model.DwgSetupNames = new List<string>();
        var request = Request();
        request.SheetNamingSetupName = DwgExportDefaults.FallbackPdfSetupName;
        request.ViewNamingSetupName = DwgExportDefaults.ViewNameNamingName;
        request.DwgSetupName = DwgExportDefaults.DefaultDwgSetupName;

        var plan = ModelSetupResolver.Resolve(model, request);

        Assert.True(plan.CanRun);
        Assert.Empty(plan.PdfSetupsToCopy);
        Assert.False(plan.CopyDwgSetup);
    }

    [Fact]
    public void OneSetupUsedForBothKinds_IsCopiedOnce()
    {
        var model = Model();
        model.PdfSetupNames = new List<string>();
        var request = Request();
        request.ViewNamingSetupName = "Sheets rule";
        request.CopyMissingSetups = true;

        Assert.Equal(new[] { "Sheets rule" }, ModelSetupResolver.Resolve(model, request).PdfSetupsToCopy);
    }
}
