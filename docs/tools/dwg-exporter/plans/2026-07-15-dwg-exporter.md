# DWG Exporter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** New RVTuk tool `DwgExporter` — batch-export a view/sheet set to DWG with filenames produced by the naming rules of the document's saved PDF export setups.

**Architecture:** Modal WPF dialog shown from an `IExternalCommand` (`ShowDialog()` keeps the command's API context alive via the nested message pump, so the Export button handler calls the Revit API directly — no external events). Core holds the pure filename composer/planner + config; UI holds the Revit-free window/viewmodel wired with delegates; Revit holds the command, naming-rule evaluator, and per-sheet export loop.

**Tech Stack:** C# (net48 + net8.0-windows multi-target), WPF, Revit API 2024/2025 (`ExportPDFSettings`/`PDFExportOptions.GetNamingRule()`, `ExportDWGSettings`, `Document.Export`), xunit.

**Spec:** `docs/tools/dwg-exporter/specs/2026-07-15-dwg-exporter-design.md`

## Global Constraints

- Namespace = root namespace + folder path, exactly (e.g. `RVTuk.Core.DwgExporter`, `RVTuk.UI.DwgExporter.ViewModels`, `RVTuk.Revit.DwgExporter.Commands`).
- `RVTuk.Core` must stay free of Revit API and WPF types. `RVTuk.UI` must not reference any Revit type — Revit interactions arrive as `Func<>`/`Action` delegates.
- No `System.Text.Json` in net48 code paths; `AppConfig` is serialized by `ConfigManager` with `DataContractJsonSerializer` on net48 and `System.Text.Json` on net8 — new fields must be plain public get/set properties with defaults.
- `KKarea.Revit` (2023) must NOT gain this tool and must keep compiling: `Release2023` builds Core + UI + KKarea, so all new Core/UI code must compile under net48.
- Revit 2024+ API: use `ElementId.Value` (long), never `IntegerValue`.
- Tool ids/names: code name `DwgExporter`, ribbon button internal id `DwgExport`, label `"DWG\nExport"`.
- Build configs are `Release2024` / `Release2025` (plus `Release2023` for Core/UI/KKarea) — there is no plain Debug/Release for the solution; the test project builds with default config.
- All solution builds must be warning-free for new files (`Nullable` is enabled in every project).

## File Structure

| File | Responsibility |
|------|----------------|
| Create `src/RVTuk.Core/DwgExporter/NamingRulePart.cs` | One resolved naming-rule field (prefix/value/suffix/separator). |
| Create `src/RVTuk.Core/DwgExporter/FileNameComposer.cs` | Join parts into a filename; sanitize illegal characters; find duplicates. |
| Create `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs` | POCOs crossing UI↔Revit: request, planned file, plan, result, setup/sheet-set descriptors, default-name constants. |
| Create `src/RVTuk.Core/DwgExporter/DwgExportPlanner.cs` | Pure pre-flight check: duplicates + already-on-disk files. |
| Modify `src/RVTuk.Core/Shared/Config/AppConfig.cs` | Add last-used DWG-export fields. |
| Create `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs` | All dialog state + export orchestration via delegates. |
| Create `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml(.cs)` | Modal window; wires MessageBox prompts, folder browser, dispatcher pump. |
| Create `src/RVTuk.Revit/DwgExporter/NamingRuleEvaluator.cs` | Describe a naming rule as a pattern string; resolve it against a sheet. |
| Create `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs` | Resolve the export range, plan filenames, run per-sheet `Document.Export`. |
| Create `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs` | Gather document data, build delegates, show the dialog modally. |
| Modify `src/RVTuk.Revit/Application.cs` | Ribbon button + icon. |
| Create `tests/RVTuk.Core.Tests/DwgExporter/FileNameComposerTests.cs` | Composer/sanitizer/duplicate tests. |
| Create `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs` | Planner tests. |
| Modify `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs` | New-field defaults + serializer round-trip. |
| Modify `CLAUDE.md`, `docs/tools/dwg-exporter/README.md`, `docs/tools/dwg-exporter/backlog.md` | Docs (final task). |

---

### Task 1: Core — `NamingRulePart` + `FileNameComposer`

**Files:**
- Create: `src/RVTuk.Core/DwgExporter/NamingRulePart.cs`
- Create: `src/RVTuk.Core/DwgExporter/FileNameComposer.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/FileNameComposerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `NamingRulePart { string Prefix, Value, Suffix, Separator }` (all default `""`); `static string FileNameComposer.Compose(IReadOnlyList<NamingRulePart> parts)`; `static string FileNameComposer.Sanitize(string name)`; `static IReadOnlyList<string> FileNameComposer.FindDuplicates(IEnumerable<string> names)`.

**Separator semantics (locked here, verified against Revit in Task 6):** each part renders as `Prefix + Value + Suffix`; part *i*'s `Separator` is inserted between part *i* and part *i+1*; the last part's separator is ignored. This matches Revit's combined-parameter behaviour where the last row's separator is unused. If manual verification in Task 6 shows a mismatch, `Compose` is the single place to fix (and these tests get updated to the observed behaviour).

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/DwgExporter/FileNameComposerTests.cs`:

```csharp
using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class FileNameComposerTests
{
    private static NamingRulePart Part(string value, string prefix = "", string suffix = "", string separator = "")
        => new NamingRulePart { Prefix = prefix, Value = value, Suffix = suffix, Separator = separator };

    [Fact]
    public void Compose_JoinsPrefixValueSuffix_AndSeparatorsBetweenParts()
    {
        // <Project Number>-A-BLD_<Building Number>-<Sheet Number>
        var name = FileNameComposer.Compose(new[]
        {
            Part("1234", separator: "-A-BLD_"),
            Part("2", separator: "-"),
            Part("A-101", separator: "---last separator must be ignored---"),
        });

        Assert.Equal("1234-A-BLD_2-A-101", name);
    }

    [Fact]
    public void Compose_SinglePart_NoSeparator()
    {
        Assert.Equal("pre-VAL-suf", FileNameComposer.Compose(new[] { Part("VAL", "pre-", "-suf", "|") }));
    }

    [Fact]
    public void Compose_EmptyValues_RenderAsEmpty_LikeNativePdfExport()
    {
        var name = FileNameComposer.Compose(new[]
        {
            Part("", separator: "-"),
            Part("A-101"),
        });

        Assert.Equal("-A-101", name);
    }

    [Fact]
    public void Compose_PreservesHebrew()
    {
        var name = FileNameComposer.Compose(new[] { Part("תכנית קומה", separator: " "), Part("א-101") });

        Assert.Equal("תכנית קומה א-101", name);
    }

    [Theory]
    [InlineData("A/101", "A-101")]                    // '/' is common in Israeli sheet numbers
    [InlineData("a\\b:c*d?e\"f<g>h|i", "a-b-c-d-e-f-g-h-i")]
    [InlineData("  name. ", "name")]                  // trailing dots/spaces are illegal on Windows
    public void Sanitize_ReplacesIllegalCharacters(string raw, string expected)
    {
        Assert.Equal(expected, FileNameComposer.Sanitize(raw));
    }

    [Fact]
    public void Sanitize_EmptyResult_FallsBackToSheet()
    {
        Assert.Equal("Sheet", FileNameComposer.Sanitize("  .. "));
        Assert.Equal("Sheet", FileNameComposer.Sanitize(""));
    }

    [Fact]
    public void FindDuplicates_IsCaseInsensitive_AndReturnsEachNameOnce()
    {
        var dupes = FileNameComposer.FindDuplicates(new[] { "A-101", "a-101", "A-102", "A-101", "B-1" });

        Assert.Equal(new[] { "A-101" }, dupes);
    }

    [Fact]
    public void FindDuplicates_Empty_WhenAllUnique()
    {
        Assert.Empty(FileNameComposer.FindDuplicates(new[] { "A-101", "A-102" }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter FullyQualifiedName~FileNameComposerTests`
Expected: build FAILURE — `NamingRulePart`/`FileNameComposer` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/DwgExporter/NamingRulePart.cs`:

```csharp
namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// One field of a PDF-export naming rule, resolved against a sheet: the filename is the
    /// concatenation of Prefix + Value + Suffix per part, with Separator inserted between a
    /// part and the next (the last part's separator is unused — Revit's combined-parameter
    /// convention).
    /// </summary>
    public class NamingRulePart
    {
        public string Prefix { get; set; } = "";
        public string Value { get; set; } = "";
        public string Suffix { get; set; } = "";
        public string Separator { get; set; } = "";
    }
}
```

Create `src/RVTuk.Core/DwgExporter/FileNameComposer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>Builds DWG filenames from resolved naming-rule parts.</summary>
    public static class FileNameComposer
    {
        public static string Compose(IReadOnlyList<NamingRulePart> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                sb.Append(parts[i].Prefix).Append(parts[i].Value).Append(parts[i].Suffix);
                if (i < parts.Count - 1) sb.Append(parts[i].Separator);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Makes a rule-produced name safe as a Windows filename: every invalid character
        /// becomes '-', trailing dots/spaces are trimmed, and a name that ends up empty
        /// falls back to "Sheet" so the export can still proceed.
        /// </summary>
        public static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '-' : c);
            var clean = sb.ToString().Trim().TrimEnd('.', ' ').Trim();
            return clean.Length == 0 ? "Sheet" : clean;
        }

        /// <summary>Names occurring more than once (case-insensitive), each reported once.</summary>
        public static IReadOnlyList<string> FindDuplicates(IEnumerable<string> names)
        {
            return names
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter FullyQualifiedName~FileNameComposerTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/RVTuk.Core/DwgExporter tests/RVTuk.Core.Tests/DwgExporter
git commit -m "feat(dwg-exporter): filename composer for PDF-setup naming rules"
```

---

### Task 2: Core — export types + `DwgExportPlanner`

**Files:**
- Create: `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs`
- Create: `src/RVTuk.Core/DwgExporter/DwgExportPlanner.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`

**Interfaces:**
- Consumes: `FileNameComposer.FindDuplicates` (Task 1).
- Produces (used verbatim by Tasks 3–5):
  - `DwgExportDefaults.FallbackPdfSetupName == "<Sheet Number> - <Sheet Name>"`, `DwgExportDefaults.DefaultDwgSetupName == "<Revit defaults>"`
  - `DwgExportRequest { bool CurrentWindow; string SheetSetName; string PdfSetupName; string DwgSetupName; string OutputFolder; }`
  - `PlannedExportFile { string ViewLabel; string FileName; }` — `FileName` is **without** the `.dwg` extension (what `Document.Export` expects).
  - `DwgExportPlan { IReadOnlyList<PlannedExportFile> Files; IReadOnlyList<string> DuplicateNames; IReadOnlyList<string> ExistingFileNames; }`
  - `DwgExportResult { int ExportedCount; List<string> Errors; }`
  - `PdfSetupItem { string Name; string Pattern; }`, `SheetSetItem { string Name; int SheetCount; string Display { get; } }`
  - `static DwgExportPlan DwgExportPlanner.Check(IReadOnlyList<PlannedExportFile> files, Func<string, bool> fileExists)` — `fileExists` receives the extension-less `FileName`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter FullyQualifiedName~DwgExportPlannerTests`
Expected: build FAILURE — types do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs`:

```csharp
using System.Collections.Generic;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>Sentinel dropdown entries used when the document has no saved setups.</summary>
    public static class DwgExportDefaults
    {
        /// <summary>Fallback naming "setup" offered when the document has no ExportPDFSettings.</summary>
        public const string FallbackPdfSetupName = "<Sheet Number> - <Sheet Name>";

        /// <summary>Fallback DWG setup entry meaning "stock DWGExportOptions".</summary>
        public const string DefaultDwgSetupName = "<Revit defaults>";
    }

    /// <summary>What the user picked in the dialog; handed to the Revit-side delegates.</summary>
    public class DwgExportRequest
    {
        public bool CurrentWindow { get; set; }
        public string SheetSetName { get; set; } = "";
        public string PdfSetupName { get; set; } = "";
        public string DwgSetupName { get; set; } = "";
        public string OutputFolder { get; set; } = "";
    }

    /// <summary>One view/sheet the export will produce. FileName has no ".dwg" extension.</summary>
    public class PlannedExportFile
    {
        public string ViewLabel { get; set; } = "";
        public string FileName { get; set; } = "";
    }

    /// <summary>Pre-flight result: everything the dialog needs to warn/abort before exporting.</summary>
    public class DwgExportPlan
    {
        public IReadOnlyList<PlannedExportFile> Files { get; set; } = new List<PlannedExportFile>();
        public IReadOnlyList<string> DuplicateNames { get; set; } = new List<string>();
        public IReadOnlyList<string> ExistingFileNames { get; set; } = new List<string>();
    }

    public class DwgExportResult
    {
        public int ExportedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>A saved PDF export setup as shown in the naming dropdown.</summary>
    public class PdfSetupItem
    {
        public string Name { get; set; } = "";
        /// <summary>Human-readable rule, e.g. "&lt;Project Number&gt;-A-BLD_&lt;Building Number&gt;-&lt;Sheet Number&gt;".</summary>
        public string Pattern { get; set; } = "";
    }

    /// <summary>A saved view/sheet set as shown in the range dropdown.</summary>
    public class SheetSetItem
    {
        public string Name { get; set; } = "";
        public int SheetCount { get; set; }
        public string Display => $"{Name} ({SheetCount} {(SheetCount == 1 ? "sheet" : "sheets")})";
    }
}
```

Create `src/RVTuk.Core/DwgExporter/DwgExportPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Pre-flight check before any file is written: duplicate filenames (sheets would
    /// overwrite each other — always an abort) and files already on disk (needs one
    /// overwrite confirmation). Pure: disk access is injected.
    /// </summary>
    public static class DwgExportPlanner
    {
        public static DwgExportPlan Check(IReadOnlyList<PlannedExportFile> files, Func<string, bool> fileExists)
        {
            return new DwgExportPlan
            {
                Files = files,
                DuplicateNames = FileNameComposer.FindDuplicates(files.Select(f => f.FileName)),
                ExistingFileNames = files.Select(f => f.FileName).Where(fileExists).ToList(),
            };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter FullyQualifiedName~DwgExportPlannerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/RVTuk.Core/DwgExporter tests/RVTuk.Core.Tests/DwgExporter
git commit -m "feat(dwg-exporter): export request/plan/result types + pre-flight planner"
```

---

### Task 3: Core — last-used settings on `AppConfig`

**Files:**
- Modify: `src/RVTuk.Core/Shared/Config/AppConfig.cs` (after the `AreaCalcMarkerForm` property, line ~22)
- Test: `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs` (append tests)

**Interfaces:**
- Consumes: existing `AppConfig` / `ConfigManager.LoadConfig()/SaveConfig()`.
- Produces: `AppConfig.DwgExportFolder`, `.DwgExportPdfSetupName`, `.DwgExportDwgSetupName`, `.DwgExportSheetSetName` (all `string`, default `""`), `.DwgExportUseCurrentWindow` (`bool`, default `false`). Task 4's view model reads/writes exactly these.

Persistence follows the existing precedent (`AreaCalcOutputFolder` lives directly on `AppConfig`), not a separate config file — the spec's "same pattern as RishuiZaminConfig" resolves to this, because that tool's remembered values are stored the same way.

- [ ] **Step 1: Write the failing test**

Append to `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs` (inside the existing `AppConfigTests` class):

```csharp
    [Fact]
    public void DwgExportSettings_DefaultEmpty_AndRoundTripThroughJson()
    {
        var fresh = new AppConfig();
        Assert.Equal("", fresh.DwgExportFolder);
        Assert.Equal("", fresh.DwgExportPdfSetupName);
        Assert.Equal("", fresh.DwgExportDwgSetupName);
        Assert.Equal("", fresh.DwgExportSheetSetName);
        Assert.False(fresh.DwgExportUseCurrentWindow);

        // The test project runs on net8, so this exercises the same serializer branch
        // ConfigManager uses there; net48's DataContractJsonSerializer handles plain
        // get/set string/bool properties identically.
        var config = new AppConfig
        {
            DwgExportFolder = @"D:\out",
            DwgExportPdfSetupName = "KKarc - Sheets (No Revision)",
            DwgExportDwgSetupName = "KKarc standard DWG",
            DwgExportSheetSetName = "Sheets for Publish",
            DwgExportUseCurrentWindow = true,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;

        Assert.Equal(@"D:\out", loaded.DwgExportFolder);
        Assert.Equal("KKarc - Sheets (No Revision)", loaded.DwgExportPdfSetupName);
        Assert.Equal("KKarc standard DWG", loaded.DwgExportDwgSetupName);
        Assert.Equal("Sheets for Publish", loaded.DwgExportSheetSetName);
        Assert.True(loaded.DwgExportUseCurrentWindow);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter FullyQualifiedName~AppConfigTests.DwgExportSettings`
Expected: build FAILURE — properties do not exist.

- [ ] **Step 3: Add the properties**

In `src/RVTuk.Core/Shared/Config/AppConfig.cs`, insert after the `AreaCalcMarkerForm` property:

```csharp
        /// <summary>Last-used values for the DWG Export dialog, remembered across sessions.
        /// Setup/set names are matched by name next time; a name that no longer exists in the
        /// open document silently falls back to the first available entry.</summary>
        public string DwgExportFolder { get; set; } = string.Empty;
        public string DwgExportPdfSetupName { get; set; } = string.Empty;
        public string DwgExportDwgSetupName { get; set; } = string.Empty;
        public string DwgExportSheetSetName { get; set; } = string.Empty;
        public bool DwgExportUseCurrentWindow { get; set; }
```

- [ ] **Step 4: Run the full Core suite**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 5: Commit**

```powershell
git add src/RVTuk.Core/Shared/Config/AppConfig.cs tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs
git commit -m "feat(dwg-exporter): remember last-used export settings in AppConfig"
```

---

### Task 4: UI — `DwgExportViewModel` + `DwgExportWindow`

**Files:**
- Create: `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`
- Create: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml`
- Create: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml.cs`

**Interfaces:**
- Consumes: Core types from Tasks 1–3; `RVTuk.UI.Shared.ViewModels.ViewModelBase` (`SetProperty`, `OnPropertyChanged`), `RelayCommand(Action, Func<bool>?)`; `DarkTheme.xaml` brushes.
- Produces (Task 5 constructs these):
  - `DwgExportViewModel(IReadOnlyList<PdfSetupItem> pdfSetups, IReadOnlyList<string> dwgSetupNames, IReadOnlyList<SheetSetItem> sheetSets, string currentViewLabel, Func<DwgExportRequest, string> evaluateExample, Func<DwgExportRequest, DwgExportPlan> planExport, Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport)`
  - VM callbacks the window (not the Revit layer) wires: `Func<string, bool>? ConfirmOverwrite`, `Action<string>? ShowError`, `Action? PumpUi`.
  - `DwgExportWindow(DwgExportViewModel vm)` — shown by Task 5 via `ShowDialog()`.

There are no UI unit tests in this repo (no test project references RVTuk.UI); the verification for this task is that all three configs compile.

- [ ] **Step 1: Write the view model**

Create `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.DwgExporter;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.DwgExporter.ViewModels
{
    /// <summary>
    /// State + orchestration for the modal DWG Export dialog. Revit-free: the three delegates
    /// run against the Revit API inside the command's context (the dialog is modal, so the
    /// nested message pump keeps that context alive while handlers run on the UI thread).
    /// </summary>
    public class DwgExportViewModel : ViewModelBase
    {
        private readonly Func<DwgExportRequest, string> _evaluateExample;
        private readonly Func<DwgExportRequest, DwgExportPlan> _planExport;
        private readonly Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> _runExport;

        /// <summary>Asks the user to confirm overwriting N existing files. Wired by the window.</summary>
        public Func<string, bool>? ConfirmOverwrite { get; set; }
        /// <summary>Shows a blocking error message. Wired by the window.</summary>
        public Action<string>? ShowError { get; set; }
        /// <summary>Lets the WPF dispatcher repaint during the synchronous export loop. Wired by the window.</summary>
        public Action? PumpUi { get; set; }

        public IReadOnlyList<PdfSetupItem> PdfSetups { get; }
        public IReadOnlyList<string> DwgSetupNames { get; }
        public IReadOnlyList<SheetSetItem> SheetSets { get; }
        public string CurrentViewLabel { get; }

        public RelayCommand ExportCommand { get; }

        public DwgExportViewModel(
            IReadOnlyList<PdfSetupItem> pdfSetups,
            IReadOnlyList<string> dwgSetupNames,
            IReadOnlyList<SheetSetItem> sheetSets,
            string currentViewLabel,
            Func<DwgExportRequest, string> evaluateExample,
            Func<DwgExportRequest, DwgExportPlan> planExport,
            Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport)
        {
            PdfSetups = pdfSetups;
            DwgSetupNames = dwgSetupNames;
            SheetSets = sheetSets;
            CurrentViewLabel = currentViewLabel;
            _evaluateExample = evaluateExample;
            _planExport = planExport;
            _runExport = runExport;

            ExportCommand = new RelayCommand(Export, () => CanExport);

            // Restore last-used choices; unknown names fall back to the first entry.
            var config = ConfigManager.LoadConfig();
            _outputFolder = config.DwgExportFolder;
            _useCurrentWindow = config.DwgExportUseCurrentWindow || sheetSets.Count == 0;
            _selectedPdfSetup =
                pdfSetups.FirstOrDefault(s => s.Name == config.DwgExportPdfSetupName) ?? pdfSetups.FirstOrDefault();
            _selectedDwgSetup =
                dwgSetupNames.FirstOrDefault(n => n == config.DwgExportDwgSetupName) ?? dwgSetupNames.FirstOrDefault();
            _selectedSheetSet =
                sheetSets.FirstOrDefault(s => s.Name == config.DwgExportSheetSetName) ?? sheetSets.FirstOrDefault();

            RefreshExample();
        }

        private bool _useCurrentWindow;
        public bool UseCurrentWindow
        {
            get => _useCurrentWindow;
            set
            {
                SetProperty(ref _useCurrentWindow, value);
                OnPropertyChanged(nameof(UseSheetSet)); // keep the inverse radio in sync
                RefreshExample();
            }
        }

        // Inverse binding target for the second radio button.
        public bool UseSheetSet
        {
            get => !_useCurrentWindow;
            set => UseCurrentWindow = !value;
        }

        private SheetSetItem? _selectedSheetSet;
        public SheetSetItem? SelectedSheetSet
        {
            get => _selectedSheetSet;
            set { SetProperty(ref _selectedSheetSet, value); RefreshExample(); }
        }

        private PdfSetupItem? _selectedPdfSetup;
        public PdfSetupItem? SelectedPdfSetup
        {
            get => _selectedPdfSetup;
            set
            {
                SetProperty(ref _selectedPdfSetup, value);
                OnPropertyChanged(nameof(PatternText));
                RefreshExample();
            }
        }

        public string PatternText => _selectedPdfSetup?.Pattern ?? "";

        private string? _selectedDwgSetup;
        public string? SelectedDwgSetup
        {
            get => _selectedDwgSetup;
            set => SetProperty(ref _selectedDwgSetup, value);
        }

        private string _outputFolder;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        private string _exampleText = "";
        public string ExampleText
        {
            get => _exampleText;
            private set => SetProperty(ref _exampleText, value);
        }

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            private set { SetProperty(ref _isExporting, value); OnPropertyChanged(nameof(CanExport)); }
        }

        private int _progressValue;
        public int ProgressValue { get => _progressValue; private set => SetProperty(ref _progressValue, value); }

        private int _progressMax = 1;
        public int ProgressMax { get => _progressMax; private set => SetProperty(ref _progressMax, value); }

        private string _statusText = "";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool CanExport =>
            !IsExporting
            && !string.IsNullOrWhiteSpace(OutputFolder)
            && SelectedPdfSetup != null
            && SelectedDwgSetup != null
            && (UseCurrentWindow || SelectedSheetSet != null);

        private DwgExportRequest BuildRequest() => new DwgExportRequest
        {
            CurrentWindow = UseCurrentWindow,
            SheetSetName = SelectedSheetSet?.Name ?? "",
            PdfSetupName = SelectedPdfSetup?.Name ?? "",
            DwgSetupName = SelectedDwgSetup ?? "",
            OutputFolder = OutputFolder.Trim(),
        };

        private void RefreshExample()
        {
            try
            {
                ExampleText = _evaluateExample(BuildRequest());
            }
            catch (Exception ex)
            {
                ExampleText = "(example unavailable: " + ex.Message + ")";
            }
        }

        private void Export()
        {
            var request = BuildRequest();

            DwgExportPlan plan;
            try
            {
                plan = _planExport(request);
            }
            catch (Exception ex)
            {
                ShowError?.Invoke("Could not prepare the export:\n\n" + ex.Message);
                return;
            }

            if (plan.Files.Count == 0)
            {
                ShowError?.Invoke("Nothing to export — the selected range contains no sheets or views.");
                return;
            }
            if (plan.DuplicateNames.Count > 0)
            {
                ShowError?.Invoke(
                    "These filenames would be produced by more than one sheet, so sheets would " +
                    "overwrite each other. Fix the naming rule or the sheet parameters:\n\n  " +
                    string.Join("\n  ", plan.DuplicateNames));
                return;
            }
            if (plan.ExistingFileNames.Count > 0)
            {
                var ok = ConfirmOverwrite?.Invoke(
                    plan.ExistingFileNames.Count + " file(s) already exist in the output folder — overwrite?") ?? true;
                if (!ok) return;
            }

            IsExporting = true;
            ProgressMax = plan.Files.Count;
            ProgressValue = 0;
            StatusText = "Exporting…";
            try
            {
                var result = _runExport(request, (done, total, label) =>
                {
                    ProgressValue = done;
                    StatusText = done + "/" + total + "  " + label;
                    PumpUi?.Invoke();
                });

                StatusText = result.Errors.Count == 0
                    ? "Exported " + result.ExportedCount + " file(s) to " + request.OutputFolder
                    : "Exported " + result.ExportedCount + ", failed " + result.Errors.Count + ":\n" +
                      string.Join("\n", result.Errors);

                SaveLastUsed();
            }
            catch (Exception ex)
            {
                StatusText = "Export failed: " + ex.Message;
            }
            finally
            {
                IsExporting = false;
            }
        }

        private void SaveLastUsed()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                config.DwgExportFolder = OutputFolder.Trim();
                config.DwgExportPdfSetupName = SelectedPdfSetup?.Name ?? "";
                config.DwgExportDwgSetupName = SelectedDwgSetup ?? "";
                config.DwgExportSheetSetName = SelectedSheetSet?.Name ?? "";
                config.DwgExportUseCurrentWindow = UseCurrentWindow;
                ConfigManager.SaveConfig(config);
            }
            catch
            {
                // Remembering settings is best-effort; never fail an export over it.
            }
        }
    }
}
```

- [ ] **Step 2: Write the window XAML**

Create `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml`:

```xml
<Window x:Class="RVTuk.UI.DwgExporter.Views.DwgExportWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="RVTuk — DWG Export"
        Width="520" SizeToContent="Height" MinWidth="440"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        ShowInTaskbar="False">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
            <Style x:Key="SectionHeader" TargetType="TextBlock">
                <Setter Property="FontWeight" Value="SemiBold"/>
                <Setter Property="Foreground" Value="{StaticResource Brush.Text}"/>
                <Setter Property="Margin" Value="0,0,0,6"/>
            </Style>
            <Style x:Key="Section" TargetType="Border">
                <Setter Property="Background" Value="{StaticResource Brush.Panel}"/>
                <Setter Property="BorderBrush" Value="{StaticResource Brush.Border}"/>
                <Setter Property="BorderThickness" Value="1"/>
                <Setter Property="CornerRadius" Value="3"/>
                <Setter Property="Padding" Value="10,8"/>
                <Setter Property="Margin" Value="0,0,0,10"/>
            </Style>
        </ResourceDictionary>
    </Window.Resources>

    <Window.Background><StaticResource ResourceKey="Brush.Bg"/></Window.Background>
    <Window.Foreground><StaticResource ResourceKey="Brush.Text"/></Window.Foreground>

    <StackPanel Margin="12">

        <!-- ── Export Range ─────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <StackPanel>
                <TextBlock Text="Export Range" Style="{StaticResource SectionHeader}"/>
                <RadioButton IsChecked="{Binding UseCurrentWindow}" Margin="0,2">
                    <TextBlock>
                        <Run Text="Current window"/>
                        <Run Text="{Binding CurrentViewLabel, Mode=OneWay, StringFormat=' — {0}'}"
                             Foreground="{StaticResource Brush.TextMuted}"/>
                    </TextBlock>
                </RadioButton>
                <DockPanel Margin="0,4,0,0">
                    <RadioButton IsChecked="{Binding UseSheetSet}" Content="Selected views/sheets"
                                 VerticalAlignment="Center"/>
                    <ComboBox ItemsSource="{Binding SheetSets}"
                              SelectedItem="{Binding SelectedSheetSet}"
                              DisplayMemberPath="Display"
                              IsEnabled="{Binding UseSheetSet}"
                              Margin="10,0,0,0" MinWidth="220"/>
                </DockPanel>
            </StackPanel>
        </Border>

        <!-- ── File Naming ──────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <StackPanel>
                <TextBlock Text="File Naming (from PDF export setup)" Style="{StaticResource SectionHeader}"/>
                <ComboBox ItemsSource="{Binding PdfSetups}"
                          SelectedItem="{Binding SelectedPdfSetup}"
                          DisplayMemberPath="Name"/>
                <Grid Margin="0,6,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition/>
                        <RowDefinition/>
                    </Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Pattern:  "
                               Foreground="{StaticResource Brush.TextMuted}"/>
                    <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding PatternText}"
                               TextWrapping="Wrap" Foreground="{StaticResource Brush.TextMuted}"/>
                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Example:  "/>
                    <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding ExampleText}"
                               TextWrapping="Wrap" FontWeight="SemiBold"/>
                </Grid>
            </StackPanel>
        </Border>

        <!-- ── DWG Export Setup ─────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <StackPanel>
                <TextBlock Text="DWG Export Setup" Style="{StaticResource SectionHeader}"/>
                <ComboBox ItemsSource="{Binding DwgSetupNames}"
                          SelectedItem="{Binding SelectedDwgSetup}"/>
            </StackPanel>
        </Border>

        <!-- ── Location ─────────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <DockPanel>
                <TextBlock Text="Location:  " VerticalAlignment="Center"/>
                <Button Content="Browse…" DockPanel.Dock="Right" Padding="10,3"
                        Margin="8,0,0,0" Click="Browse_Click"/>
                <TextBox Text="{Binding OutputFolder, UpdateSourceTrigger=PropertyChanged}"
                         VerticalAlignment="Center"/>
            </DockPanel>
        </Border>

        <!-- ── Progress / status ────────────────────────────────────────── -->
        <ProgressBar Height="8" Margin="0,0,0,6"
                     Minimum="0" Maximum="{Binding ProgressMax}" Value="{Binding ProgressValue, Mode=OneWay}"
                     Visibility="{Binding IsExporting, Converter={StaticResource BoolVis}}"/>
        <TextBlock Text="{Binding StatusText}" TextWrapping="Wrap" Margin="0,0,0,8"
                   Foreground="{StaticResource Brush.TextMuted}"/>

        <!-- ── Buttons ──────────────────────────────────────────────────── -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Export" Command="{Binding ExportCommand}" Padding="18,5"
                    Background="{StaticResource Brush.AccentDark}" BorderBrush="{StaticResource Brush.Accent}"/>
            <Button Content="Close" Click="Close_Click" Padding="18,5" Margin="8,0,0,0"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 3: Write the code-behind**

Create `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;
using RVTuk.UI.DwgExporter.ViewModels;

namespace RVTuk.UI.DwgExporter.Views
{
    public partial class DwgExportWindow : Window
    {
        private readonly DwgExportViewModel _vm;

        public DwgExportWindow(DwgExportViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            vm.ShowError = msg =>
                MessageBox.Show(this, msg, "DWG Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            vm.ConfirmOverwrite = msg =>
                MessageBox.Show(this, msg, "DWG Export", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes;
            // The export loop runs synchronously on this thread (it must — it needs the
            // command's Revit API context); an empty Background-priority dispatcher hop
            // lets pending layout/render work run so the progress bar actually moves.
            vm.PumpUi = () =>
                Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the DWG output folder",
                SelectedPath = _vm.OutputFolder,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                _vm.OutputFolder = dialog.SelectedPath;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
```

- [ ] **Step 4: Verify all three configs compile**

Run:
```powershell
dotnet build src\RVTuk.UI\RVTuk.UI.csproj -c Release2024
dotnet build src\RVTuk.UI\RVTuk.UI.csproj -c Release2025
dotnet build src\RVTuk.UI\RVTuk.UI.csproj -c Release2023
```
Expected: all three succeed with no new warnings.

- [ ] **Step 5: Commit**

```powershell
git add src/RVTuk.UI/DwgExporter
git commit -m "feat(dwg-exporter): modal export window + view model"
```

---

### Task 5: Revit — evaluator, exporter, command, ribbon button

**Files:**
- Create: `src/RVTuk.Revit/DwgExporter/NamingRuleEvaluator.cs`
- Create: `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs`
- Create: `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs` (usings ~line 11; `CreateRibbon` after the `panel.AddItem(areaBtn);` at ~line 169; new icon method next to the others)

**Interfaces:**
- Consumes: everything from Tasks 1, 2, 4. Revit API: `ExportPDFSettings.GetOptions().GetNamingRule()` → `IList<TableCellCombinedParameterData>` (properties `ParamId`, `CategoryId`, `Prefix`, `Suffix`, `Separator`, `SampleValue`); `ExportDWGSettings.GetDWGExportOptions()`; `ViewSheetSet.Views`; `Document.Export(string folder, string name, ICollection<ElementId> views, DWGExportOptions options)` (name is extension-less; returns `bool`).
- Produces: the registered ribbon tool. No later task consumes code from this one.

- [ ] **Step 1: Write `NamingRuleEvaluator`**

Create `src/RVTuk.Revit/DwgExporter/NamingRuleEvaluator.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Turns a PDF export setup's naming rule into (a) a display pattern like
    /// "&lt;Project Number&gt;-A-BLD_&lt;Building Number&gt;-&lt;Sheet Number&gt;" and
    /// (b) resolved parts for a concrete sheet, which Core's FileNameComposer joins.
    /// A null rule means the built-in fallback: "&lt;Sheet Number&gt; - &lt;Sheet Name&gt;".
    /// </summary>
    internal static class NamingRuleEvaluator
    {
        public static string DescribePattern(Document doc, IList<TableCellCombinedParameterData>? rule)
        {
            if (rule == null || rule.Count == 0) return DwgExportDefaults.FallbackPdfSetupName;

            var parts = rule.Select(entry => new NamingRulePart
            {
                Prefix = entry.Prefix ?? "",
                Value = "<" + ParamName(doc, entry) + ">",
                Suffix = entry.Suffix ?? "",
                Separator = entry.Separator ?? "",
            }).ToList();
            return FileNameComposer.Compose(parts);
        }

        public static IReadOnlyList<NamingRulePart> ResolveForSheet(
            Document doc, ViewSheet sheet, IList<TableCellCombinedParameterData>? rule)
        {
            if (rule == null || rule.Count == 0)
            {
                return new List<NamingRulePart>
                {
                    new NamingRulePart { Value = sheet.SheetNumber, Separator = " - " },
                    new NamingRulePart { Value = sheet.Name },
                };
            }

            return rule.Select(entry => new NamingRulePart
            {
                Prefix = entry.Prefix ?? "",
                Value = ResolveValue(doc, sheet, entry),
                Suffix = entry.Suffix ?? "",
                Separator = entry.Separator ?? "",
            }).ToList();
        }

        private static string ParamName(Document doc, TableCellCombinedParameterData entry)
        {
            if (entry.ParamId.Value < 0)
                return LabelUtils.GetLabelFor((BuiltInParameter)entry.ParamId.Value);
            return (doc.GetElement(entry.ParamId) as ParameterElement)?.Name ?? "?";
        }

        /// <summary>
        /// The rule stores which category each field comes from, but resolving is simpler and
        /// more robust by probing: try the sheet first, then Project Information (the only two
        /// sources the PDF naming rule offers for sheets).
        /// </summary>
        private static string ResolveValue(Document doc, ViewSheet sheet, TableCellCombinedParameterData entry)
        {
            var param = FindParameter(doc, sheet, entry.ParamId)
                        ?? FindParameter(doc, doc.ProjectInformation, entry.ParamId);
            if (param == null || !param.HasValue) return "";
            return (param.StorageType == StorageType.String ? param.AsString() : param.AsValueString()) ?? "";
        }

        private static Parameter? FindParameter(Document doc, Element element, ElementId paramId)
        {
            if (paramId.Value < 0)
                return element.get_Parameter((BuiltInParameter)paramId.Value);

            var paramElement = doc.GetElement(paramId) as ParameterElement;
            return paramElement == null ? null : element.LookupParameter(paramElement.Name);
        }
    }
}
```

- [ ] **Step 2: Write `SheetDwgExporter`**

Create `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Resolves the requested export range to concrete views + filenames, and runs the export
    /// one view per Document.Export call — the multi-view overload invents its own filenames,
    /// and per-view calls give us exact names, progress, and per-sheet error capture.
    /// </summary>
    internal static class SheetDwgExporter
    {
        /// <summary>The views/sheets the request resolves to, with evaluated filenames (no extension).</summary>
        public static List<(ElementId Id, PlannedExportFile File)> PlanFiles(
            UIDocument uidoc, DwgExportRequest request, out List<string> skipped)
        {
            var doc = uidoc.Document;
            var rule = GetNamingRule(doc, request.PdfSetupName);
            var result = new List<(ElementId, PlannedExportFile)>();
            skipped = new List<string>();

            if (request.CurrentWindow)
            {
                var view = uidoc.ActiveGraphicalView
                    ?? throw new InvalidOperationException("The active window is not an exportable graphical view.");
                result.Add((view.Id, new PlannedExportFile
                {
                    ViewLabel = Label(view),
                    FileName = FileNameFor(doc, view, rule),
                }));
                return result;
            }

            var set = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .FirstOrDefault(s => s.Name == request.SheetSetName)
                ?? throw new InvalidOperationException(
                    "View/sheet set '" + request.SheetSetName + "' no longer exists in this document.");

            foreach (View view in set.Views)
            {
                if (view is ViewSheet || view.CanBePrinted)
                {
                    result.Add((view.Id, new PlannedExportFile
                    {
                        ViewLabel = Label(view),
                        FileName = FileNameFor(doc, view, rule),
                    }));
                }
                else
                {
                    skipped.Add(view.Name);
                }
            }

            return result
                .OrderBy(x => x.Item2.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static DwgExportResult Export(
            Document doc,
            DwgExportRequest request,
            List<(ElementId Id, PlannedExportFile File)> files,
            Action<int, int, string> progress)
        {
            var options = GetDwgOptions(doc, request.DwgSetupName);
            var result = new DwgExportResult();

            for (int i = 0; i < files.Count; i++)
            {
                var (id, file) = files[i];
                try
                {
                    var ok = doc.Export(request.OutputFolder, file.FileName,
                        new List<ElementId> { id }, options);
                    if (ok) result.ExportedCount++;
                    else result.Errors.Add(file.ViewLabel + ": Revit reported the export failed.");
                }
                catch (Exception ex)
                {
                    result.Errors.Add(file.ViewLabel + ": " + ex.Message);
                }
                progress(i + 1, files.Count, file.ViewLabel);
            }

            return result;
        }

        /// <summary>Null when the fallback pseudo-setup is selected (document has no PDF setups).</summary>
        public static IList<TableCellCombinedParameterData>? GetNamingRule(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName) return null;

            var settings = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportPDFSettings))
                .Cast<ExportPDFSettings>()
                .FirstOrDefault(s => s.Name == pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            return settings.GetOptions().GetNamingRule();
        }

        private static DWGExportOptions GetDwgOptions(Document doc, string dwgSetupName)
        {
            if (dwgSetupName == DwgExportDefaults.DefaultDwgSetupName) return new DWGExportOptions();

            var settings = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportDWGSettings))
                .Cast<ExportDWGSettings>()
                .FirstOrDefault(s => s.Name == dwgSetupName)
                ?? throw new InvalidOperationException(
                    "DWG export setup '" + dwgSetupName + "' no longer exists in this document.");
            return settings.GetDWGExportOptions();
        }

        /// <summary>Sheets get the naming rule; non-sheet views (rule params don't apply) get their view name.</summary>
        private static string FileNameFor(Document doc, View view, IList<TableCellCombinedParameterData>? rule)
        {
            var raw = view is ViewSheet sheet
                ? FileNameComposer.Compose(NamingRuleEvaluator.ResolveForSheet(doc, sheet, rule))
                : view.Name;
            return FileNameComposer.Sanitize(raw);
        }

        private static string Label(View view)
            => view is ViewSheet sheet ? sheet.SheetNumber + " - " + sheet.Name : view.Name;

        public static bool DwgFileExists(string folder, string fileName)
            => File.Exists(Path.Combine(folder, fileName + ".dwg"));
    }
}
```

- [ ] **Step 3: Write the command**

Create `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.DwgExporter;
using RVTuk.UI.DwgExporter.ViewModels;
using RVTuk.UI.DwgExporter.Views;

namespace RVTuk.Revit.DwgExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DwgExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit never creates a WPF Application; make one we own (never auto-shutdown).
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                // ── Dropdown contents ────────────────────────────────────────────────
                var pdfSettings = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportPDFSettings))
                    .Cast<ExportPDFSettings>()
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var pdfItems = pdfSettings.Count > 0
                    ? pdfSettings.Select(s => new PdfSetupItem
                    {
                        Name = s.Name,
                        Pattern = NamingRuleEvaluator.DescribePattern(doc, s.GetOptions().GetNamingRule()),
                    }).ToList()
                    : new List<PdfSetupItem>
                    {
                        new PdfSetupItem
                        {
                            Name = DwgExportDefaults.FallbackPdfSetupName,
                            Pattern = DwgExportDefaults.FallbackPdfSetupName,
                        },
                    };

                var dwgNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportDWGSettings))
                    .Cast<ExportDWGSettings>()
                    .Select(s => s.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (dwgNames.Count == 0) dwgNames.Add(DwgExportDefaults.DefaultDwgSetupName);

                var sheetSets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => new SheetSetItem
                    {
                        Name = s.Name,
                        SheetCount = s.Views.OfType<ViewSheet>().Count(),
                    })
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var activeView = uidoc.ActiveGraphicalView;
                var currentViewLabel = activeView is ViewSheet vs
                    ? vs.SheetNumber + " - " + vs.Name
                    : activeView?.Name ?? "(no graphical view)";

                // ── Delegates (run on the UI thread inside this command's API context) ──
                Func<DwgExportRequest, string> evaluateExample = request =>
                {
                    var files = SheetDwgExporter.PlanFiles(uidoc, request, out _);
                    return files.Count == 0 ? "(no sheets in the selected set)" : files[0].File.FileName + ".dwg";
                };

                Func<DwgExportRequest, DwgExportPlan> planExport = request =>
                {
                    if (!Directory.Exists(request.OutputFolder))
                        Directory.CreateDirectory(request.OutputFolder);
                    var files = SheetDwgExporter.PlanFiles(uidoc, request, out _);
                    return DwgExportPlanner.Check(
                        files.Select(f => f.File).ToList(),
                        name => SheetDwgExporter.DwgFileExists(request.OutputFolder, name));
                };

                Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport =
                    (request, progress) =>
                    {
                        var files = SheetDwgExporter.PlanFiles(uidoc, request, out var skipped);
                        var result = SheetDwgExporter.Export(doc, request, files, progress);
                        result.Errors.AddRange(
                            skipped.Select(name => name + ": skipped (not an exportable view)"));
                        return result;
                    };

                var vm = new DwgExportViewModel(
                    pdfItems, dwgNames, sheetSets, currentViewLabel,
                    evaluateExample, planExport, runExport);
                var window = new DwgExportWindow(vm);
                new System.Windows.Interop.WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle,
                };
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RVTuk – DWG Export",
                    "Failed to open DWG Export:\n\n" + ex.GetType().Name + ": " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
```

- [ ] **Step 4: Register the ribbon button**

In `src/RVTuk.Revit/Application.cs`:

Add the using (with the other `RVTuk.Revit.*` usings):

```csharp
using RVTuk.Revit.DwgExporter.Commands;
```

In `CreateRibbon`, immediately after `panel.AddItem(areaBtn);`:

```csharp
            var dwgBtn = new PushButtonData(
                "DwgExport",
                "DWG\nExport",
                assemblyPath,
                typeof(DwgExportCommand).FullName!)
            {
                ToolTip = "Batch-export a sheet set to DWG, named by the PDF export setups' naming rules"
            };
            dwgBtn.LargeImage = CreateDwgExportIcon(32);
            dwgBtn.Image      = CreateDwgExportIcon(16);

            panel.AddItem(dwgBtn);
```

Add the icon method next to the other `Create*Icon` methods:

```csharp
        private static BitmapSource CreateDwgExportIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Document sheet with a folded corner.
                var sheetBrush = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xD4, 0xD4));
                var foldBrush  = new SolidColorBrush(WpfColor.FromRgb(0x9A, 0x9A, 0x9A));
                var sheetGeo = new StreamGeometry();
                using (var g = sheetGeo.Open())
                {
                    g.BeginFigure(new WpfPoint(s * 0.20, s * 0.10), true, true);
                    g.LineTo(new WpfPoint(s * 0.62, s * 0.10), true, false);
                    g.LineTo(new WpfPoint(s * 0.76, s * 0.24), true, false);
                    g.LineTo(new WpfPoint(s * 0.76, s * 0.62), true, false);
                    g.LineTo(new WpfPoint(s * 0.20, s * 0.62), true, false);
                }
                sheetGeo.Freeze();
                ctx.DrawGeometry(sheetBrush, null, sheetGeo);

                var foldGeo = new StreamGeometry();
                using (var g = foldGeo.Open())
                {
                    g.BeginFigure(new WpfPoint(s * 0.62, s * 0.10), true, true);
                    g.LineTo(new WpfPoint(s * 0.76, s * 0.24), true, false);
                    g.LineTo(new WpfPoint(s * 0.62, s * 0.24), true, false);
                }
                foldGeo.Freeze();
                ctx.DrawGeometry(foldBrush, null, foldGeo);

                // Orange export arrow under the sheet.
                var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)),
                    Math.Max(1, s * 0.09));
                pen.Freeze();
                ctx.DrawLine(pen, new WpfPoint(s * 0.24, s * 0.78), new WpfPoint(s * 0.72, s * 0.78));
                ctx.DrawLine(pen, new WpfPoint(s * 0.60, s * 0.67), new WpfPoint(s * 0.72, s * 0.78));
                ctx.DrawLine(pen, new WpfPoint(s * 0.60, s * 0.89), new WpfPoint(s * 0.72, s * 0.78));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
```

- [ ] **Step 5: Build both RVTuk configs + KKarea's config**

Run:
```powershell
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2023
```
Expected: all succeed. (Release2023 proves KKarea is untouched by the new code.)

- [ ] **Step 6: Commit**

```powershell
git add src/RVTuk.Revit/DwgExporter src/RVTuk.Revit/Application.cs
git commit -m "feat(dwg-exporter): Revit command, naming-rule evaluator, per-sheet export, ribbon button"
```

---

### Task 6: Deploy, manual verification, docs

**Files:**
- Modify: `CLAUDE.md` (Features list + v1 launch surface sentence)
- Modify: `docs/tools/dwg-exporter/README.md` (status line)
- Modify: `docs/tools/dwg-exporter/backlog.md` (Done entry)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: PASS, zero failures.

- [ ] **Step 2: Deploy and verify in Revit (requires the user / a Revit machine)**

From an elevated shell: `.\Deploy.ps1 2024` (or 2025), restart Revit, then walk this checklist in a real project that has KKarc PDF setups:

1. Ribbon shows **DWG Export** next to Area Calc; button opens the dialog centred on Revit.
2. Dropdowns list the document's PDF setups (with pattern preview), DWG setups, and view/sheet sets; last-used values are pre-selected on second open.
3. The Example line shows a real filename and updates when the PDF setup or range changes.
4. **Naming-rule separator check:** export one small set to an empty folder AND run the native PDF export with the same setup — the base filenames must match exactly. If separators land differently, fix `FileNameComposer.Compose` (single place) and update `FileNameComposerTests` to the observed behaviour.
5. Progress bar advances per sheet; summary reports the count; the `.dwg` files open in AutoCAD/DWG TrueView with the layers of the chosen DWG setup.
6. Duplicate-name abort (temporarily give two sheets naming-rule values that collide) and the overwrite prompt (re-export to the same folder) both behave as specced.
7. "Current window" exports the active sheet only.

- [ ] **Step 3: Update docs**

- `CLAUDE.md`: add a Features bullet after the Rishui Zamin one:
  ```markdown
  - **DWG Exporter** (ribbon "DWG Export") — batch-exports a view/sheet set to DWG with filenames produced by the naming rules of the document's saved PDF export setups, plus a native DWG export setup for layers/lines. Modal dialog, no external events. Revit 2024/25 only. See [`docs/tools/dwg-exporter/README.md`](docs/tools/dwg-exporter/README.md).
  ```
  and extend the "v1 launch surface" sentence to name three registered tools (Family Browser, Rishui Zamin, DWG Exporter). Update the `RegisterUnreleasedTools` doc-comment in `Application.cs` the same way if it enumerates the registered tools.
- `docs/tools/dwg-exporter/README.md`: change **Status:** to `shipped (registered on the ribbon)` once verification passes.
- `docs/tools/dwg-exporter/backlog.md`: add a Done entry for the implementation; move anything discovered during verification into bugs/ideas.

- [ ] **Step 4: Commit**

```powershell
git add CLAUDE.md docs/tools/dwg-exporter
git commit -m "docs(dwg-exporter): mark shipped; register in CLAUDE.md feature list"
```
