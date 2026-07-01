using System;
using System.Collections.Generic;
using System.IO;
using RVTuk.Core.AreaSubmission;
using Xunit;

namespace RVTuk.Core.Tests.AreaSubmission;

public class DxfWriterTests
{
    private static AreaRecord OneArea() => new()
    {
        Number = "115",
        Name = "Storage",
        UsageCode = 115,
        Floor = "Ground Floor",
        Level = "Ground Floor",
        ElevationMeters = 0.0,
        PageNo = 1,
        IsUnderground = false,
        AreaValue = 12.0,
        BoundaryLoops = new List<List<Point2D>>
        {
            new()
            {
                new() { X = 0, Y = 0 },
                new() { X = 400, Y = 0 },
                new() { X = 400, Y = 300 },
                new() { X = 0, Y = 300 },
            }
        }
    };

    private static AreaSubmissionConfig Config() => new()
    {
        BuildingNo = 1,
        Asset = null,
        Scale = 100,
        OutputFolder = "C:\\out",
        FileBaseName = "test"
    };

    private static string LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AreaSubmission", "Fixtures", "one_area.dxf");
        return File.ReadAllText(path);
    }

    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void Build_OneArea_ContainsGoldenFixtureEntitiesUnit()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        var fixtureText = LoadFixture();

        Assert.Contains(Normalise(fixtureText).Trim(), Normalise(dxf));
    }

    [Fact]
    public void Build_AreaPolyline_IsClosedWithCorrectVertexCount()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        // The RZ_AREA LWPOLYLINE: group 90 = vertex count (4), group 70 = 1 (closed).
        Assert.Contains("8\r\nRZ_AREA\r\n62\r\n256\r\n6\r\nByLayer\r\n370\r\n-1\r\n48\r\n1.0\r\n60\r\n0\r\n100\r\nAcDbPolyline\r\n90\r\n4\r\n70\r\n1\r\n", dxf);
    }

    [Theory]
    [InlineData("USAGE_TYPE")]
    [InlineData("USAGE_TYPE_OLD")]
    [InlineData("AREA")]
    [InlineData("ASSET")]
    [InlineData("PAGE_NO")]
    [InlineData("BUILDING_NO")]
    [InlineData("FLOOR")]
    [InlineData("LEVEL_ELEVATION")]
    [InlineData("IS_UNDERGROUND")]
    public void Build_EmitsAllNineAttributeTags(string tag)
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        // Each ATTRIB carries its tag under group 2, immediately followed by the field-flag
        // group 70 (verified against the real Garmoshka.dxf sample: group 1 = value comes
        // earlier in the AcDbText subclass, group 2 = tag comes later in AcDbAttribute).
        Assert.Contains($"2\r\n{tag}\r\n70\r\n", dxf);
    }

    [Fact]
    public void Build_UsageTypeOld_MirrorsUsageType()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        Assert.Contains("2\r\nUSAGE_TYPE\r\n70\r\n0\r\n", dxf);
        Assert.Contains("1\r\n115\r\n", dxf);
    }

    [Theory]
    [InlineData(0.0, "+0.00")]
    [InlineData(3.0, "+3.00")]
    [InlineData(-2.5, "-2.50")]
    [InlineData(-0.0001, "+0.00")]   // rounds to zero: no negative zero
    public void Build_LevelElevation_IsSignedMetresNotLevelName(double meters, string expected)
    {
        var area = OneArea();
        area.ElevationMeters = meters;

        var dxf = DxfWriter.Build(new[] { area }, Config());

        // The RZ_FLOOR_SYM attrib value (group 1) preceding the LEVEL_ELEVATION tag (group 2):
        // spec format is a signed relative elevation like "+3.00", never the level's name.
        // Search only the ENTITIES section — the preamble's ATTDEF also carries the tag.
        var entitiesIndex = dxf.IndexOf("2\r\nENTITIES\r\n", StringComparison.Ordinal);
        Assert.True(entitiesIndex >= 0, "ENTITIES section missing");
        var tagIndex = dxf.IndexOf("2\r\nLEVEL_ELEVATION\r\n", entitiesIndex, StringComparison.Ordinal);
        Assert.True(tagIndex >= 0, "LEVEL_ELEVATION attrib missing");
        var attribStart = dxf.LastIndexOf("0\r\nATTRIB\r\n", tagIndex, StringComparison.Ordinal);
        var value = dxf.Substring(attribStart, tagIndex - attribStart);
        Assert.Contains($"1\r\n{expected}\r\n", value);
        Assert.DoesNotContain("Ground Floor", value);
    }

    [Fact]
    public void Build_StartsWithDxfHeaderSection()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        Assert.StartsWith("0\r\nSECTION\r\n2\r\nHEADER\r\n", dxf);
        Assert.EndsWith("0\r\nENDSEC\r\n0\r\nEOF\r\n", dxf);
    }
}
