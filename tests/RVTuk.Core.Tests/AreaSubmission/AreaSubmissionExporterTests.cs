using System;
using System.Collections.Generic;
using System.IO;
using RVTuk.Core.AreaSubmission;
using Xunit;

namespace RVTuk.Core.Tests.AreaSubmission;

public class AreaSubmissionExporterTests : IDisposable
{
    private readonly string _outputFolder;

    public AreaSubmissionExporterTests()
    {
        _outputFolder = Path.Combine(Path.GetTempPath(), "rvtuk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_outputFolder)) Directory.Delete(_outputFolder, recursive: true); } catch { /* best effort */ }
    }

    private static AreaRecord ValidArea() => new()
    {
        Number = "101",
        Name = "Living",
        UsageCode = 1,
        AreaValue = 5,
        BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } }
    };

    private AreaSubmissionConfig ValidConfig() => new()
    {
        OutputFolder = _outputFolder,
        FileBaseName = "submission",
        Scale = 10,
        BuildingNo = 1,
    };

    [Fact]
    public void Export_ValidAreasAndConfig_WritesDxfAndDatFiles()
    {
        var areas = new List<AreaRecord> { ValidArea() };
        var config = ValidConfig();

        var (ok, message) = AreaSubmissionExporter.Export(areas, config);

        Assert.True(ok, message);
        Assert.NotNull(message);

        var dxfPath = Path.Combine(_outputFolder, "submission.dxf");
        var datPath = Path.Combine(_outputFolder, "submission.dat");

        Assert.True(File.Exists(dxfPath));
        Assert.True(File.Exists(datPath));

        var dxfContent = File.ReadAllText(dxfPath);
        Assert.False(string.IsNullOrEmpty(dxfContent));
        Assert.Contains("RZ_AREA", dxfContent);

        var datBytes = File.ReadAllBytes(datPath);
        Assert.Equal(DatWriter.Build(config.Scale), datBytes);
    }

    [Fact]
    public void Export_AreaMissingUsageCode_FailsAndWritesNoFiles()
    {
        var badArea = ValidArea();
        badArea.UsageCode = null;
        var areas = new List<AreaRecord> { badArea };
        var config = ValidConfig();

        var (ok, message) = AreaSubmissionExporter.Export(areas, config);

        Assert.False(ok);
        Assert.Contains("usage code", message, StringComparison.OrdinalIgnoreCase);

        var dxfPath = Path.Combine(_outputFolder, "submission.dxf");
        var datPath = Path.Combine(_outputFolder, "submission.dat");

        Assert.False(File.Exists(dxfPath));
        Assert.False(File.Exists(datPath));
    }
}
