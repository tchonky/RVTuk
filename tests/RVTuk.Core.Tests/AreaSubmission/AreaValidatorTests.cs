using System.Linq;
using RVTuk.Core.AreaSubmission;
using Xunit;

namespace RVTuk.Core.Tests.AreaSubmission;

public class AreaValidatorTests
{
    [Fact]
    public void Area_NoUsageCode_IsError()
    {
        var a = new AreaRecord
        {
            UsageCode = null,
            AreaValue = 5,
            BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } }
        };
        Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.NoUsageCode));
    }

    [Fact]
    public void Area_InvalidUsageCode_IsError()
    {
        var a = new AreaRecord
        {
            UsageCode = 9999,
            AreaValue = 5,
            BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } }
        };
        Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.NoUsageCode));
    }

    [Fact]
    public void Area_ZeroArea_IsError()
    {
        var a = new AreaRecord { UsageCode = 1, AreaValue = 0 };
        Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.ZeroArea));
    }

    [Fact]
    public void Area_NegativeArea_IsError()
    {
        var a = new AreaRecord { UsageCode = 1, AreaValue = -3 };
        Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.ZeroArea));
    }

    [Fact]
    public void Area_OpenLoop_IsBadBoundary()
    {
        var a = new AreaRecord { UsageCode = 1, AreaValue = 5, BoundaryLoops = { new() { new() { X = 0, Y = 0 } } } }; // <3 pts
        Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.BadBoundary));
    }

    [Fact]
    public void Area_NoBoundaryLoops_IsBadBoundary()
    {
        var a = new AreaRecord { UsageCode = 1, AreaValue = 5 };
        Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.BadBoundary));
    }

    [Fact]
    public void Area_ValidRecord_HasNoErrors()
    {
        var a = new AreaRecord
        {
            UsageCode = 1,
            AreaValue = 5,
            BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } }
        };
        Assert.Equal(AreaError.None, AreaValidator.CheckArea(a));
    }

    [Fact]
    public void Config_MissingOutputFolder_IsError()
    {
        var errs = AreaValidator.CheckConfig(new AreaSubmissionConfig { OutputFolder = "" });
        Assert.Contains(errs, e => e.Contains("output", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Config_MissingFileBaseName_IsError()
    {
        var errs = AreaValidator.CheckConfig(new AreaSubmissionConfig { OutputFolder = "C:\\out", FileBaseName = "" });
        Assert.Contains(errs, e => e.Contains("file", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Config_ZeroScale_IsError()
    {
        var errs = AreaValidator.CheckConfig(new AreaSubmissionConfig { OutputFolder = "C:\\out", FileBaseName = "x", Scale = 0, BuildingNo = 1 });
        Assert.Contains(errs, e => e.Contains("scale", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Config_BuildingNoBelowOne_IsError()
    {
        var errs = AreaValidator.CheckConfig(new AreaSubmissionConfig { OutputFolder = "C:\\out", FileBaseName = "x", Scale = 100, BuildingNo = 0 });
        Assert.Contains(errs, e => e.Contains("building", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Config_AllValid_HasNoErrors()
    {
        var errs = AreaValidator.CheckConfig(new AreaSubmissionConfig
        {
            OutputFolder = "C:\\out",
            FileBaseName = "x",
            Scale = 100,
            BuildingNo = 1
        });
        Assert.Empty(errs);
    }

    [Fact]
    public void Validate_AggregatesAreaAndConfigErrors()
    {
        var areas = new System.Collections.Generic.List<AreaRecord>
        {
            new() { Number = "101", Name = "Living", UsageCode = null, AreaValue = 5,
                BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } } }
        };
        var config = new AreaSubmissionConfig { OutputFolder = "", FileBaseName = "x", Scale = 100, BuildingNo = 1 };

        var result = AreaValidator.Validate(areas, config);

        Assert.Contains(result.Errors, e => e.Contains("101"));
        Assert.Contains(result.Errors, e => e.Contains("output", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AreaMissingNumberOrName_IsWarningNotError()
    {
        var areas = new System.Collections.Generic.List<AreaRecord>
        {
            new() { Number = null, Name = null, UsageCode = 1, AreaValue = 5,
                BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } } }
        };
        var config = new AreaSubmissionConfig { OutputFolder = "C:\\out", FileBaseName = "x", Scale = 100, BuildingNo = 1 };

        var result = AreaValidator.Validate(areas, config);

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Validate_AllValid_HasNoErrorsOrWarnings()
    {
        var areas = new System.Collections.Generic.List<AreaRecord>
        {
            new() { Number = "101", Name = "Living", UsageCode = 1, AreaValue = 5,
                BoundaryLoops = { new() { new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 } } } }
        };
        var config = new AreaSubmissionConfig { OutputFolder = "C:\\out", FileBaseName = "x", Scale = 100, BuildingNo = 1 };

        var result = AreaValidator.Validate(areas, config);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }
}
