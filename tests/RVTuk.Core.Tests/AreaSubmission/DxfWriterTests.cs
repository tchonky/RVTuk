using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        LevelElevation = 0.0,
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

    // Most of the suite pins Form B (the tekenplus TEXT encoding the golden fixture was
    // captured for); Form A tests opt in explicitly. The config default itself is Form A —
    // covered by Build_DefaultMarkerForm_IsOfficialFormA.
    private static AreaSubmissionConfig Config() => new()
    {
        BuildingNo = 1,
        Asset = null,
        Scale = 100,
        OutputFolder = "C:\\out",
        FileBaseName = "test",
        MarkerForm = MarkerForm.FormB,
    };

    private static AreaSubmissionConfig FormAConfig()
    {
        var cfg = Config();
        cfg.MarkerForm = MarkerForm.FormA;
        return cfg;
    }

    private static string LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AreaSubmission", "Fixtures", "one_area.dxf");
        return File.ReadAllText(path);
    }

    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    /// <summary>The generated ENTITIES section body only — after the section marker, before its
    /// ENDSEC. The preamble's BLOCKS section repeats the RZ_*_SYM names and the OBJECTS
    /// postamble has its own group-5 handles, so structural assertions must not scan those.</summary>
    private static string EntitiesSection(string dxf)
    {
        var start = dxf.IndexOf("2\r\nENTITIES\r\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "ENTITIES section not found");
        var end = dxf.IndexOf("0\r\nENDSEC\r\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "ENTITIES ENDSEC not found");
        return dxf.Substring(start, end - start);
    }

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

    [Fact]
    public void Build_AreaMarker_IsKeyValueText()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        // tekenplus marker form: one TEXT per polygon, KEY=VALUE pairs joined with &&&,
        // USAGE_TYPE_OLD left empty for new work (never mirrored from USAGE_TYPE).
        Assert.Contains("1\r\nUSAGE_TYPE=115&&&USAGE_TYPE_OLD=&&&AREA=&&&ASSET=\r\n", dxf);
        Assert.DoesNotContain("ATTRIB", dxf.Substring(dxf.IndexOf("ENTITIES", StringComparison.Ordinal)));
        Assert.DoesNotContain("SEQEND", dxf.Substring(dxf.IndexOf("ENTITIES", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_AreaMarker_CarriesPrevUsageWhenSet()
    {
        var area = OneArea();
        area.UsageCode = 301;      // demolition marker
        area.UsageCodePrev = 1;    // usage as existing in the permit

        var dxf = DxfWriter.Build(new[] { area }, Config());

        Assert.Contains("1\r\nUSAGE_TYPE=301&&&USAGE_TYPE_OLD=1&&&AREA=&&&ASSET=\r\n", dxf);
    }

    [Fact]
    public void Build_FloorMarker_CarriesAllFourKeys()
    {
        var area = OneArea();
        area.LevelElevation = -4.9;
        area.IsUnderground = true;

        var dxf = DxfWriter.Build(new[] { area }, Config());

        Assert.Contains("1\r\nBUILDING_NO=1&&&FLOOR=Ground Floor&&&LEVEL_ELEVATION=-4.90&&&IS_UNDERGROUND=1\r\n", dxf);
    }

    [Fact]
    public void Build_FrameMarker_CarriesPageNo()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        Assert.Contains("1\r\nPAGE_NO=1\r\n", dxf);
    }

    [Fact]
    public void Build_FixedSheet_FrameIsSheetRectangleAndGeometryUntranslated()
    {
        var cfg = Config();
        cfg.SheetWidthCm = 40000;
        cfg.SheetHeightCm = 9000;

        var area = OneArea();
        area.BoundaryLoops[0] = new List<Point2D>
        {
            new() { X = 1000, Y = 2000 },
            new() { X = 1400, Y = 2000 },
            new() { X = 1400, Y = 2300 },
            new() { X = 1000, Y = 2300 },
        };

        var dxf = DxfWriter.Build(new[] { area }, cfg);

        // Frame anchored at (0,0) with the real sheet size...
        Assert.Contains("8\r\nRZ_FRAME\r\n62\r\n256\r\n6\r\nByLayer\r\n370\r\n-1\r\n48\r\n1.0\r\n60\r\n0\r\n100\r\nAcDbPolyline\r\n90\r\n4\r\n70\r\n1\r\n38\r\n0.0\r\n39\r\n0.0\r\n10\r\n0.0\r\n20\r\n0.0\r\n", dxf);
        Assert.Contains("10\r\n40000.0\r\n20\r\n9000.0\r\n", dxf);
        // ...and the sheet-relative geometry kept as-is (no translation).
        Assert.Contains("10\r\n1000.0\r\n20\r\n2000.0\r\n", dxf);
    }

    [Fact]
    public void Build_ConcaveArea_MarkerAnchorLiesInsidePolygon()
    {
        // L-shape whose vertex average / bbox centre falls in the notch (outside the polygon).
        var area = OneArea();
        area.BoundaryLoops[0] = new List<Point2D>
        {
            new() { X = 0, Y = 0 },
            new() { X = 400, Y = 0 },
            new() { X = 400, Y = 100 },
            new() { X = 100, Y = 100 },
            new() { X = 100, Y = 400 },
            new() { X = 0, Y = 400 },
        };

        var cfg = Config();
        cfg.SheetWidthCm = 40000;
        cfg.SheetHeightCm = 9000;
        var dxf = DxfWriter.Build(new[] { area }, cfg);

        // Pull the RZ_AREA marker TEXT anchor out of the output.
        var match = Regex.Match(dxf,
            "8\r\nRZ_AREA\r\n(?:.*?\r\n)*?100\r\nAcDbText\r\n10\r\n(?<x>[-0-9.]+)\r\n20\r\n(?<y>[-0-9.]+)\r\n");
        Assert.True(match.Success, "RZ_AREA marker TEXT not found");
        var x = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
        var y = double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);

        Assert.True(InsideL(x, y), $"anchor ({x},{y}) is outside the L-polygon");
    }

    private static bool InsideL(double x, double y) =>
        (x > 0 && x < 400 && y > 0 && y < 100) || (x > 0 && x < 100 && y > 0 && y < 400);

    [Fact]
    public void Build_StartsWithDxfHeaderSection()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, Config());

        Assert.StartsWith("0\r\nSECTION\r\n2\r\nHEADER\r\n", dxf);
        Assert.EndsWith("0\r\nENDSEC\r\n0\r\nEOF\r\n", dxf);
    }

    [Fact]
    public void Build_DefaultMarkerForm_IsOfficialFormA()
    {
        Assert.Equal(MarkerForm.FormA, new AreaSubmissionConfig().MarkerForm);
    }

    [Fact]
    public void Build_FormA_AreaMarkerIsBlockInsertWithFourAttribs()
    {
        var entities = EntitiesSection(DxfWriter.Build(new[] { OneArea() }, FormAConfig()));

        // Marker = INSERT of RZ_AREA_SYM with attributes-follow (66=1), one ATTRIB per tag,
        // closed by SEQEND — and none of Form B's KEY=VALUE&&& TEXT payload.
        Assert.Contains("2\r\nRZ_AREA_SYM\r\n", entities);
        Assert.Contains("66\r\n1\r\n0\r\nATTRIB\r\n", entities);
        foreach (var tag in new[] { "USAGE_TYPE", "USAGE_TYPE_OLD", "AREA", "ASSET" })
        {
            Assert.Contains("2\r\n" + tag + "\r\n70\r\n", entities);
        }

        Assert.Contains("SEQEND", entities);
        Assert.DoesNotContain("&&&", entities);
    }

    [Fact]
    public void Build_FormA_AttribsAreOwnedByTheirInsert()
    {
        var entities = EntitiesSection(DxfWriter.Build(new[] { OneArea() }, FormAConfig()));

        // Locate the area marker's block-name group, then read its own INSERT's handle — the
        // last INSERT preceding it (a lazy cross-entity regex could grab the frame's instead).
        var blockNameAt = entities.IndexOf("2\r\nRZ_AREA_SYM\r\n", StringComparison.Ordinal);
        Assert.True(blockNameAt >= 0, "RZ_AREA_SYM INSERT not found");
        var insertAt = entities.LastIndexOf("0\r\nINSERT\r\n5\r\n", blockNameAt, StringComparison.Ordinal);
        Assert.True(insertAt >= 0, "INSERT entity start not found");
        var insertHandle = Regex.Match(entities.Substring(insertAt), "0\r\nINSERT\r\n5\r\n(?<h>[0-9A-F]+)\r\n").Groups["h"].Value;

        var attribOwners = Regex.Matches(
                entities.Substring(blockNameAt),
                "0\r\nATTRIB\r\n5\r\n[0-9A-F]+\r\n330\r\n(?<owner>[0-9A-F]+)\r\n")
            .Cast<Match>()
            .Select(m => m.Groups["owner"].Value)
            .Take(4)
            .ToList();

        Assert.Equal(4, attribOwners.Count);
        Assert.All(attribOwners, owner => Assert.Equal(insertHandle, owner));
    }

    [Fact]
    public void Build_FormA_FrameAndFloorMarkersCarryTheirTags()
    {
        var area = OneArea();
        area.LevelElevation = -4.9;
        area.IsUnderground = true;

        var entities = EntitiesSection(DxfWriter.Build(new[] { area }, FormAConfig()));

        Assert.Contains("2\r\nRZ_FRAME_SYM\r\n", entities);
        Assert.Contains("2\r\nRZ_FLOOR_SYM\r\n", entities);
        // ATTRIB serialises value (group 1) before tag (group 2).
        Assert.Contains("1\r\n1\r\n", entities); // PAGE_NO / BUILDING_NO value "1"
        Assert.Contains("2\r\nPAGE_NO\r\n", entities);
        Assert.Contains("1\r\nGround Floor\r\n", entities);
        Assert.Contains("2\r\nFLOOR\r\n", entities);
        Assert.Contains("1\r\n-4.90\r\n", entities);
        Assert.Contains("2\r\nLEVEL_ELEVATION\r\n", entities);
        Assert.Contains("2\r\nIS_UNDERGROUND\r\n", entities);
    }

    [Fact]
    public void Build_FormA_InvisibleFlagsMirrorTheBlockAttdefs()
    {
        var dxf = DxfWriter.Build(new[] { OneArea() }, FormAConfig());

        // Per Garmoshka's ATTDEFs: AREA and ASSET are invisible (70=1), the usage tags visible.
        Assert.Contains("2\r\nUSAGE_TYPE\r\n70\r\n0\r\n", dxf);
        Assert.Contains("2\r\nUSAGE_TYPE_OLD\r\n70\r\n0\r\n", dxf);
        Assert.Contains("2\r\nAREA\r\n70\r\n1\r\n", dxf);
        Assert.Contains("2\r\nASSET\r\n70\r\n1\r\n", dxf);
    }

    [Theory]
    [InlineData(MarkerForm.FormA)]
    [InlineData(MarkerForm.FormB)]
    public void Build_EntityHandlesAreUniqueAndBelowHandseed(MarkerForm form)
    {
        var cfg = Config();
        cfg.MarkerForm = form;
        var dxf = DxfWriter.Build(new[] { OneArea() }, cfg);

        // Handles at entity starts only — a bare "5" group match would also hit e.g. owner
        // pointers' neighbouring lines or the OBJECTS postamble's dictionary handles.
        var handles = Regex.Matches(EntitiesSection(dxf),
                "0\r\n(?:INSERT|ATTRIB|SEQEND|LWPOLYLINE|TEXT)\r\n5\r\n(?<h>[0-9A-F]+)\r\n")
            .Cast<Match>()
            .Select(m => Convert.ToInt32(m.Groups["h"].Value, 16))
            .ToList();

        Assert.NotEmpty(handles);
        Assert.Equal(handles.Count, new HashSet<int>(handles).Count);

        var handseed = Regex.Match(dxf, "\\$HANDSEED\r\n5\r\n(?<h>[0-9A-F]+)\r\n");
        Assert.True(handseed.Success, "$HANDSEED not found");
        var seed = Convert.ToInt32(handseed.Groups["h"].Value, 16);
        Assert.True(handles.Max() < seed, $"handle {handles.Max():X} not below $HANDSEED {seed:X}");
    }

    [Fact]
    public void Build_AreaMarker_CarriesPermitAreaAndPerAreaAsset()
    {
        var area = OneArea();
        area.PermitArea = "123.45";
        area.Asset = "7";

        var dxf = DxfWriter.Build(new[] { area }, Config());

        Assert.Contains("1\r\nUSAGE_TYPE=115&&&USAGE_TYPE_OLD=&&&AREA=123.45&&&ASSET=7\r\n", dxf);
    }

    [Fact]
    public void Build_AreaMarker_FallsBackToConfigAssetWhenAreaHasNone()
    {
        var cfg = Config();
        cfg.Asset = "12";

        var dxf = DxfWriter.Build(new[] { OneArea() }, cfg);

        Assert.Contains("&&&ASSET=12\r\n", dxf);
    }
}
