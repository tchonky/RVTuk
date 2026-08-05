using RVTuk.Core.DwgExporter;
using RVTuk.Core.Shared.Config;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class DwgExportSettingsStoreTests
{
    [Fact]
    public void Read_BlankConfig_IsTodaysBehaviour()
    {
        var settings = DwgExportSettingsStore.Read(new AppConfig(), @"C:\Projects\Tower.rvt");

        Assert.Equal(DwgExportDefaults.ViewNameNamingName, settings.ViewNamingSetupName);
        Assert.False(settings.SeparatePdfFolder);
        Assert.False(settings.CopyMissingSetups);
        Assert.True(settings.ExportDwg);
        Assert.False(settings.ExportPdf);
        Assert.False(settings.CreateTransmittalZip);
    }

    [Fact]
    public void CreateTransmittalZip_RoundTrips()
    {
        var config = new AppConfig();

        DwgExportSettingsStore.Write(config, "m", new DwgExportSettings { CreateTransmittalZip = true });

        Assert.True(DwgExportSettingsStore.Read(config, "m").CreateTransmittalZip);
    }

    [Fact]
    public void Read_MapsTheSheetsRuleOntoTheOriginalKey()
    {
        // The pre-split key kept its name, so an upgraded config restores its selection.
        var config = new AppConfig { DwgExportPdfSetupName = "Office A1" };

        Assert.Equal("Office A1", DwgExportSettingsStore.Read(config, "m").SheetNamingSetupName);
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var config = new AppConfig();
        var written = new DwgExportSettings
        {
            OutputFolder = @"D:\out",
            PdfOutputFolder = @"D:\pdf",
            SeparatePdfFolder = true,
            SheetNamingSetupName = "Sheets rule",
            ViewNamingSetupName = "Views rule",
            DwgSetupName = "Office",
            SheetSetName = "Issue 01",
            UseCurrentWindow = true,
            ExportDwg = false,
            ExportPdf = true,
            CopyMissingSetups = true,
        };

        DwgExportSettingsStore.Write(config, @"C:\Projects\Tower.rvt", written);
        var read = DwgExportSettingsStore.Read(config, @"C:\Projects\Tower.rvt");

        Assert.Equal(@"D:\out", read.OutputFolder);
        Assert.Equal(@"D:\pdf", read.PdfOutputFolder);
        Assert.True(read.SeparatePdfFolder);
        Assert.Equal("Sheets rule", read.SheetNamingSetupName);
        Assert.Equal("Views rule", read.ViewNamingSetupName);
        Assert.Equal("Office", read.DwgSetupName);
        Assert.Equal("Issue 01", read.SheetSetName);
        Assert.True(read.UseCurrentWindow);
        Assert.False(read.ExportDwg);
        Assert.True(read.ExportPdf);
        Assert.True(read.CopyMissingSetups);
    }

    [Fact]
    public void Write_DoesNotStrandThePdfFolder_WhenTheCheckboxIsOff()
    {
        // With one folder chosen, the PDF folder must track it rather than keep a stale path
        // that would come back the moment the checkbox is ticked again.
        var config = new AppConfig();

        DwgExportSettingsStore.Write(config, "m", new DwgExportSettings
        {
            OutputFolder = @"D:\out",
            PdfOutputFolder = @"D:\stale",
            SeparatePdfFolder = false,
        });

        Assert.Equal(@"D:\out", DwgExportSettingsStore.Read(config, "m").PdfOutputFolder);
    }
}
