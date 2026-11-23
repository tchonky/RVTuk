using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RVTuk.Core.Tests.Shared;

// The frozen identifiers live in RVTuk.Revit, which no test can load, and phase 3 moves their
// files, so this counts exact source text over the whole repository: a move changes nothing,
// an edit or a copy does.
public class FrozenIdentifierTests
{
    // count|snippet. A row changes only on purpose: "AreaCalc" becomes 1 in phase 3's Area Calc
    // step, which deletes src\KKarea.Revit; the 0 rows at the end are retired names that must
    // never come back (the last three before "TopoSpacing" went with the Topo Tools pane, replaced by the edit session; "TopoSpacing" with the Spacing box, on 2026-11-23).
    private const string Table = """
        1|new Guid("7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4")
        1|SchemaName = "RVTukAutoDimensionTrackingByView"
        1|FieldName = "DimensionIdsByViewId"
        1|new Guid("6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51")
        1|SchemaName = "RVTukAutoDimensionsScopeSelectionV3"
        1|CategoryMaskField = "CategoryMask"
        1|ViewIdsField = "CheckedViewIds"
        1|DimensionTypeIdField = "DimensionTypeId"
        1|AddSimpleField(CategoryMaskField, typeof(int))
        1|AddSimpleField(ViewIdsField, typeof(string))
        1|AddSimpleField(DimensionTypeIdField, typeof(string))
        1|new Guid("ebcc7eef-5290-473c-bc7a-79e14b69cea0")
        1|SchemaName = "RVTukTopoHeightTextLink"
        1|FieldName = "LineId"
        1|AddSimpleField(FieldName, typeof(long))
        1|new Guid("0aa14034-8c77-48a4-b5d6-dca41ed6da08")
        1|new Guid("3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62")
        1|SchemaName = "RVTukTopoToolsPointLedger"
        1|FieldName = "PointsByLineId"
        1|new Guid("a9b2b113-115d-48a4-80a5-3063ddc3caca")
        1|SchemaName = "RVTukRoomFloorsLink"
        1|FieldName = "RoomUniqueId"
        1|new Guid("309b77b4-64cd-45c7-bd73-d6a81b1f85ae")
        1|SchemaName = "RVTukTopoToolsParkedParameters"
        1|FieldName = "Entries"
        1|new Guid("cca5f35d-98c8-466c-9175-4d51afe00df1")
        1|SchemaName = "RVTukTopoToolsOutlineHeights"
        1|FieldName = "CornersByXy"
        5|AddSimpleField(FieldName, typeof(string))
        7|builder.SetVendorId("KnafoKlimor");
        7|builder.SetReadAccessLevel(AccessLevel.Public);
        7|builder.SetWriteAccessLevel(AccessLevel.Public);
        1|OurVendorId = "KnafoKlimor"
        1|new DockablePaneId(new Guid("e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2"))
        1|new DockablePaneId(new Guid("88a9c4fa-9c6a-4b02-b169-701be8358090"))
        1|new DockablePaneId(new Guid("b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370"))
        1|UsageTypeParam = "RZ_USAGE_TYPE"
        1|UsageTypeOldParam = "RZ_USAGE_TYPE_OLD"
        1|PermitAreaParam = "RZ_AREA"
        1|AssetParam = "RZ_ASSET"
        1|BuildingNoParam = "RZ_BUILDING_NO"
        1|(UsageTypeParam, new Guid("737fe9fb-a9ab-4e92-b951-bafbeb34eec0"))
        1|(UsageTypeOldParam, new Guid("50640c88-869d-40cc-b9c0-d82180e83a57"))
        1|(PermitAreaParam, new Guid("e59fbd50-19bb-4185-a7d6-6dc9191c7c4f"))
        1|(AssetParam, new Guid("7af015fb-bc87-438a-93c8-869578f37f67"))
        1|(BuildingNoParam, new Guid("e8702634-4fc1-4bb0-b0e1-2d944aa3944a"))
        1|Groups.get_Item("RVTuk") ?? sharedFile.Groups.Create("RVTuk")
        1|NewUsageScheduleName = "New Usage"
        1|OldUsageScheduleName = "Old Usage"
        1|EnsureSchedule(doc, NewUsageScheduleName, "New Usage",
        1|EnsureSchedule(doc, OldUsageScheduleName, "Old Usage",
        1|<LogicalName>RVTuk.Core.RishuiZamin.DxfTemplates.Preamble.dxf</LogicalName>
        1|<LogicalName>RVTuk.Core.RishuiZamin.DxfTemplates.Postamble.dxf</LogicalName>
        1|"RVTuk.Core.RishuiZamin.DxfTemplates." + fileName
        1|OuterLineStyleName = "_DP-Dim Outer"
        1|InnerLineStyleName = "_DP-Dim Inner"
        1|RefLineStyleName = "_DP-Dim Ref"
        1|LineStyleName = "_DP-Topo Line"
        1|"BrowseLibrary"
        2|"AreaCalc"
        1|"DwgExport"
        1|"RoundPositions"
        1|"AutoDimensions"
        1|"TopoTools"
        1|"RoomFloors"
        1|"NeoProperties"
        1|"FloorTopoConvert"
        1|"TopoDrawLines"
        1|"TopoHeightLabels"
        1|"TopoUpdate"
        1|"TopoAccept"
        1|"TopoCancel"
        1|"TopoHeight"
        1|"TopoPick"
        1|"TopoUpdateMenu"
        1|PanelName = "Topo Edit"
        1|Name = "_DP-Topo Height"
        3|"RVTuk.db"
        1|DbFolderName = ".DB"
        1|LegacyDbFolderName = ".Setup"
        1|"Autodesk", "Revit", "Addins", "RVTuk"
        1|"config.json"
        1|ParameterName = "_Version"
        1|LibraryFolderPath { get; set; }
        1|IgnoredSubfolders { get; set; }
        1|IgnoredFilePatterns { get; set; }
        1|AreaCalcOutputFolder { get; set; }
        1|AreaCalcMarkerForm { get; set; }
        1|DwgExportFolder { get; set; }
        1|DwgExportPdfSetupName { get; set; }
        1|DwgExportDwgSetupName { get; set; }
        1|DwgExportSheetSetName { get; set; }
        1|DwgExportUseCurrentWindow { get; set; }
        1|DwgExportViewNamingSetupName { get; set; }
        1|DwgExportSeparatePdfFolder { get; set; }
        1|DwgExportCopyMissingSetups { get; set; }
        1|DwgExportCreateZip { get; set; }
        1|DwgExportZipMode { get; set; }
        1|DwgExportDwgOff { get; set; }
        1|DwgExportPdfOn { get; set; }
        1|DwgExportFolders { get; set; }
        1|DwgExportPdfFolder { get; set; }
        1|DwgExportPdfFolders { get; set; }
        1|TopoPointSpacingCentimetres { get; set; }
        1|RoundPositionsWholeCentimetresOnly { get; set; }
        1|ModelKey { get; set; }
        1|string Folder { get; set; }
        1|<AddInClientId>D71D7480-4A21-474E-A47E-3E8DF8C1BDA5</AddInClientId>
        1|<AddInClientId>9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC</AddInClientId>
        0|RZ_UsageType
        0|f80b9154-f0e7-4f01-82d0-3ec259872e5c
        0|RZ Usage Key Schedule
        0|6d21f8b4-0c93-4e57-a8d6-b45f70e29c18
        0|RVTukTopoLineElevation
        0|a17c46e9-5b83-4d20-8f6a-9c4e2b70d135
        0|"TopoSpacing"
        """;

    [Fact]
    public void EveryFrozenIdentifier_IsInTheSourceExactlyAsOftenAsExpected()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "RVTuk.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var sources = SourceFiles(root!.FullName).Select(File.ReadAllText).ToList();

        var failures = new List<string>();
        foreach (var row in Table.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bar = row.IndexOf('|');
            var expected = int.Parse(row.Substring(0, bar));
            var snippet = row.Substring(bar + 1);
            var found = sources.Sum(text => Count(text, snippet));
            if (found != expected) failures.Add($"{snippet}: expected {expected}, found {found}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // Every *.cs, *.csproj and *.props below dir, skipping bin, obj, tests, dot-named entries and any
    // folder holding a .git entry (a worktree or another copy of the repository).
    private static IEnumerable<string> SourceFiles(string dir) =>
        Directory.EnumerateFiles(dir)
            .Where(f => !Path.GetFileName(f).StartsWith(".")
                     && (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                      || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                      || f.EndsWith(".props", StringComparison.OrdinalIgnoreCase)))
            .Concat(Directory.EnumerateDirectories(dir)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return !name.StartsWith(".")
                        && !name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("tests", StringComparison.OrdinalIgnoreCase)
                        && !File.Exists(Path.Combine(d, ".git"))
                        && !Directory.Exists(Path.Combine(d, ".git"));
                })
                .SelectMany(SourceFiles));

    private static int Count(string text, string snippet)
    {
        var n = 0;
        for (var i = text.IndexOf(snippet, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(snippet, i + snippet.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
