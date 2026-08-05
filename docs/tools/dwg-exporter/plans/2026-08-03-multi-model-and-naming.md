# DWG Exporter — multi-model export, per-kind naming rules, split folders — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one DWG Export run name sheets and views by different rules, write PDFs and DWGs to different folders, and export from several open models at once.

**Architecture:** All decision logic that doesn't need a live Revit document moves into `RVTuk.Core.DwgExporter` as pure, unit-tested code (`ModelSetupResolver`, `DwgExportSettingsStore`, the reshaped `DwgExportRequest`). The Revit layer gains two small services — `OpenModels` (enumerate/inspect open documents) and `SetupTransfer` (rebuild a naming rule against another document, create setups there) — and an orchestrator, `DwgExportRunner`, which the thin `DwgExportCommand` hands to the view-model as delegates.

**Tech Stack:** C# (multi-targeting `net48` for Revit 2024 and `net8.0-windows` for Revit 2025), WPF/MVVM, xunit, Revit API (`Nice3point.Revit.Api.RevitAPI`, compile-only).

**Spec:** [`../specs/2026-08-03-multi-model-and-naming-design.md`](../specs/2026-08-03-multi-model-and-naming-design.md)

## Global Constraints

- `RVTuk.Core` must never reference the Revit API or WPF. `RVTuk.UI` must never reference the Revit API.
- Namespace = root namespace + folder path, exactly. New Core files go in `RVTuk.Core.DwgExporter`, new Revit files in `RVTuk.Revit.DwgExporter`, new UI files in `RVTuk.UI.DwgExporter.ViewModels`.
- Source files under `src/` use **block** namespaces; test files under `tests/` use **file-scoped** namespaces. Match the file you are in.
- Nullable reference types are enabled across the solution.
- **Any new `AppConfig` key must have "absent from the file" and "the out-of-box default" be the same value.** net48's `DataContractJsonSerializer` builds `AppConfig` through `FormatterServices.GetUninitializedObject`, so property initializers never run: a `bool` defaulting to `true`, or a `List<>` with an initializer, arrives as `false` / `null` from any config written before that key existed.
- The DWG Exporter is Revit **2024/2025 only** — it is not compiled into `KKarea.Revit` (2023). `ElementId.Value` (not `.IntegerValue`) is therefore safe in its Revit-side files.
- Sentinel values, used verbatim: `DwgExportDefaults.FallbackPdfSetupName == "<Sheet Number> - <Sheet Name>"`, `DwgExportDefaults.DefaultDwgSetupName == "<Revit defaults>"`, and the new `DwgExportDefaults.ViewNameNamingName == "<View Name>"`.
- With only the active model ticked, one output folder, and `<View Name>` chosen for views, a run must behave exactly as it does today.

**Commands used throughout:**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

```bash
dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024
```

```bash
dotnet build RVTuk.sln -c Release2025
```

---

### Task 1: `AppConfig` — new keys and null-tolerant per-model folder lists

**Files:**
- Modify: `src/RVTuk.Core/Shared/Config/AppConfig.cs`
- Test: `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppConfig.DwgExportViewNamingSetupName` (string), `AppConfig.DwgExportSeparatePdfFolder` (bool), `AppConfig.DwgExportCopyMissingSetups` (bool), `AppConfig.DwgExportPdfFolder` (string), `AppConfig.DwgExportPdfFolders` (`List<DwgExportFolderEntry>`), `string GetDwgExportPdfFolder(string modelKey)`, `void SetDwgExportPdfFolder(string modelKey, string folder)`. `GetDwgExportFolder` / `SetDwgExportFolder` keep their signatures and become null-tolerant.

- [ ] **Step 1: Write the failing tests**

Append to `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs`, before the closing brace:

```csharp
    [Fact]
    public void DwgExportFolderLists_ArriveNull_AndStillWork()
    {
        // net48's DataContractJsonSerializer builds AppConfig via GetUninitializedObject, so
        // a list absent from an older config file arrives null rather than as the
        // initializer's empty list. Nothing here may throw.
        var config = new AppConfig
        {
            DwgExportFolders = null!,
            DwgExportPdfFolders = null!,
            DwgExportFolder = @"D:\global",
        };

        Assert.Equal(@"D:\global", config.GetDwgExportFolder(@"C:\Projects\Tower.rvt"));
        Assert.Equal(@"D:\global", config.GetDwgExportPdfFolder(@"C:\Projects\Tower.rvt"));

        config.SetDwgExportFolder(@"C:\Projects\Tower.rvt", @"D:\out\dwg");
        config.SetDwgExportPdfFolder(@"C:\Projects\Tower.rvt", @"D:\out\pdf");

        Assert.Equal(@"D:\out\dwg", config.GetDwgExportFolder(@"C:\Projects\Tower.rvt"));
        Assert.Equal(@"D:\out\pdf", config.GetDwgExportPdfFolder(@"C:\Projects\Tower.rvt"));
    }

    [Fact]
    public void GetDwgExportPdfFolder_FallsBackToTheDwgFolder_WhenNeverSetSeparately()
    {
        var config = new AppConfig();
        config.SetDwgExportFolder(@"C:\Projects\Tower.rvt", @"D:\out\tower");

        Assert.Equal(@"D:\out\tower", config.GetDwgExportPdfFolder(@"C:\Projects\Tower.rvt"));
    }

    [Fact]
    public void GetDwgExportPdfFolder_PrefersPerModel_ThenGlobalPdf()
    {
        var config = new AppConfig { DwgExportFolder = @"D:\dwg", DwgExportPdfFolder = @"D:\pdf" };
        config.SetDwgExportPdfFolder(@"C:\Projects\Tower.rvt", @"D:\pdf\tower");

        Assert.Equal(@"D:\pdf\tower", config.GetDwgExportPdfFolder(@"C:\Projects\Tower.rvt"));
        Assert.Equal(@"D:\pdf", config.GetDwgExportPdfFolder(@"C:\Projects\Other.rvt"));
    }

    [Fact]
    public void DwgExportNewKeys_AbsentFromFile_MeanTodaysBehaviour()
    {
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{}")!;

        Assert.Equal(string.Empty, loaded.DwgExportViewNamingSetupName); // => "<View Name>"
        Assert.False(loaded.DwgExportSeparatePdfFolder);                 // => one folder
        Assert.False(loaded.DwgExportCopyMissingSetups);                 // => skip the model
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~AppConfigTests"
```

Expected: FAIL — compile errors, `'AppConfig' does not contain a definition for 'DwgExportPdfFolders'` (and the other four new members).

- [ ] **Step 3: Add the new keys**

In `src/RVTuk.Core/Shared/Config/AppConfig.cs`, add `using System.Collections.Generic;` under `using System.IO;`, then insert after the `DwgExportUseCurrentWindow` property:

```csharp
        /// <summary>Last-used naming setup for non-sheet views. Empty means the built-in
        /// "&lt;View Name&gt;" entry (the view's own name) — the behaviour before views could
        /// take a rule, and still the default, so an absent key and the default agree.</summary>
        public string DwgExportViewNamingSetupName { get; set; } = string.Empty;

        /// <summary>False (the absent-key value) means PDFs and DWGs share one folder.</summary>
        public bool DwgExportSeparatePdfFolder { get; set; }

        /// <summary>False (the absent-key value) means an extra model missing a named export
        /// setup is skipped, rather than having the setup created in it.</summary>
        public bool DwgExportCopyMissingSetups { get; set; }
```

Then insert after the `DwgExportFolders` property and its `DwgExportFolderCap` constant:

```csharp
        /// <summary>Per-model PDF output folders, used only when
        /// <see cref="DwgExportSeparatePdfFolder"/> is on; same MRU/cap rules as
        /// <see cref="DwgExportFolders"/>, with <see cref="DwgExportPdfFolder"/> as the
        /// global fallback.</summary>
        public string DwgExportPdfFolder { get; set; } = string.Empty;

        public List<DwgExportFolderEntry> DwgExportPdfFolders { get; set; } =
            new List<DwgExportFolderEntry>();
```

- [ ] **Step 4: Make the accessors null-tolerant and add the PDF pair**

Replace the existing `GetDwgExportFolder` and `SetDwgExportFolder` methods (and their doc comments) with:

```csharp
        /// <summary>The remembered output folder for a model (key = document path, or title for
        /// unsaved documents), falling back to the global last-used folder.</summary>
        public string GetDwgExportFolder(string modelKey)
            => Lookup(DwgExportFolders, modelKey) ?? DwgExportFolder;

        /// <summary>Upserts the model's folder (MRU: entry moves to the end; oldest entries are
        /// dropped past the cap) and updates the global fallback.</summary>
        public void SetDwgExportFolder(string modelKey, string folder)
        {
            DwgExportFolder = folder;
            DwgExportFolders = Upsert(DwgExportFolders, modelKey, folder);
        }

        /// <summary>The remembered PDF folder for a model, falling back to the global PDF
        /// folder and then to the DWG folder — where PDFs went before they could have one of
        /// their own, so an upgraded config keeps behaving the same.</summary>
        public string GetDwgExportPdfFolder(string modelKey)
        {
            var perModel = Lookup(DwgExportPdfFolders, modelKey);
            if (perModel != null) return perModel;
            return string.IsNullOrWhiteSpace(DwgExportPdfFolder)
                ? GetDwgExportFolder(modelKey)
                : DwgExportPdfFolder;
        }

        public void SetDwgExportPdfFolder(string modelKey, string folder)
        {
            DwgExportPdfFolder = folder;
            DwgExportPdfFolders = Upsert(DwgExportPdfFolders, modelKey, folder);
        }

        private static string? Lookup(List<DwgExportFolderEntry>? entries, string modelKey)
        {
            var entry = entries?.Find(e =>
                string.Equals(e.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase));
            return entry != null && !string.IsNullOrWhiteSpace(entry.Folder) ? entry.Folder : null;
        }

        /// <summary>MRU upsert. Takes and returns the list because net48's
        /// DataContractJsonSerializer skips property initializers: a list absent from an older
        /// config file arrives null, and dereferencing it would throw before the dialog opens.</summary>
        private static List<DwgExportFolderEntry> Upsert(
            List<DwgExportFolderEntry>? entries, string modelKey, string folder)
        {
            var list = entries ?? new List<DwgExportFolderEntry>();
            if (string.IsNullOrWhiteSpace(modelKey)) return list;

            list.RemoveAll(e => string.Equals(e.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase));
            list.Add(new DwgExportFolderEntry { ModelKey = modelKey, Folder = folder });
            while (list.Count > DwgExportFolderCap) list.RemoveAt(0);
            return list;
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, all tests green (the pre-existing suite plus four new ones).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/Shared/Config/AppConfig.cs tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs
git commit -m "fix(dwg-exporter): a config file older than the folder list must not crash the dialog"
```

---

### Task 2: Core types — the reshaped request, per-file model/kind, run notes

**Files:**
- Modify: `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DwgExportDefaults.ViewNameNamingName`; `DwgExportRequest` with `SheetNamingSetupName`, `ViewNamingSetupName`, `PdfOutputFolder`, `SeparatePdfFolder`, `ExtraModelKeys` (`List<string>`), `CopyMissingSetups`, and the derived `PdfFolder`; `PlannedExportFile` with `ModelTitle`, `IsSheet`, `UsedViewNameFallback`, `Source`; `DwgExportResult.Notes` (`List<string>`).
- **Breaking:** `DwgExportRequest.PdfSetupName` is gone. Tasks 7–10 update every caller, so `RVTuk.UI` and `RVTuk.Revit` will not compile until Task 10. `RVTuk.Core` and its test project are unaffected and must stay green at every commit from here on — that is this task's gate.
- `DwgExportPlan.DuplicateNames` is deliberately left alone here; Task 3 replaces it.

- [ ] **Step 1: Write the failing tests**

Append to `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`, before the closing brace:

```csharp
    [Fact]
    public void DwgExportRequest_Defaults_ViewsUseTheViewName()
    {
        Assert.Equal("<View Name>", DwgExportDefaults.ViewNameNamingName);
        Assert.Equal(DwgExportDefaults.ViewNameNamingName, new DwgExportRequest().ViewNamingSetupName);
        Assert.False(new DwgExportRequest().SeparatePdfFolder);
        Assert.False(new DwgExportRequest().CopyMissingSetups);
        Assert.Empty(new DwgExportRequest().ExtraModelKeys);
    }

    [Fact]
    public void PdfFolder_IsTheOutputFolder_UnlessASeparateOneIsAskedForAndSet()
    {
        var shared = new DwgExportRequest { OutputFolder = @"D:\out", PdfOutputFolder = @"D:\pdf" };
        Assert.Equal(@"D:\out", shared.PdfFolder); // checkbox off: the second path is ignored

        var split = new DwgExportRequest
        {
            OutputFolder = @"D:\out", PdfOutputFolder = @"D:\pdf", SeparatePdfFolder = true,
        };
        Assert.Equal(@"D:\pdf", split.PdfFolder);

        var splitButBlank = new DwgExportRequest { OutputFolder = @"D:\out", SeparatePdfFolder = true };
        Assert.Equal(@"D:\out", splitButBlank.PdfFolder);
    }

    [Fact]
    public void PlannedExportFile_Source_NamesTheModelOnlyWhenThereIsOne()
    {
        Assert.Equal("A-101", new PlannedExportFile { ViewLabel = "A-101" }.Source);
        Assert.Equal(
            "Tower-A.rvt — A-101",
            new PlannedExportFile { ModelTitle = "Tower-A.rvt", ViewLabel = "A-101" }.Source);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~DwgExportPlannerTests"
```

Expected: FAIL — `'DwgExportDefaults' does not contain a definition for 'ViewNameNamingName'`.

- [ ] **Step 3: Replace the types**

Replace the whole body of `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs` between `namespace RVTuk.Core.DwgExporter` `{` and its closing `}` with:

```csharp
    /// <summary>Sentinel dropdown entries used when the document has no saved setups, or when
    /// a rule is built in code rather than read from a setup.</summary>
    public static class DwgExportDefaults
    {
        /// <summary>Fallback naming "setup" offered when the document has no ExportPDFSettings.</summary>
        public const string FallbackPdfSetupName = "<Sheet Number> - <Sheet Name>";

        /// <summary>Fallback DWG setup entry meaning "stock DWGExportOptions".</summary>
        public const string DefaultDwgSetupName = "<Revit defaults>";

        /// <summary>Views-naming entry meaning "use the view's own name" — what non-sheet views
        /// did before they could take a rule, and still the default.</summary>
        public const string ViewNameNamingName = "<View Name>";
    }

    /// <summary>What the user picked in the dialog; handed to the Revit-side delegates.</summary>
    public class DwgExportRequest
    {
        public bool CurrentWindow { get; set; }
        public string SheetSetName { get; set; } = "";

        /// <summary>PDF setup whose naming rule names sheets.</summary>
        public string SheetNamingSetupName { get; set; } = "";

        /// <summary>PDF setup whose naming rule names non-sheet views, or
        /// <see cref="DwgExportDefaults.ViewNameNamingName"/> for the view's own name.</summary>
        public string ViewNamingSetupName { get; set; } = DwgExportDefaults.ViewNameNamingName;

        public string DwgSetupName { get; set; } = "";
        public string OutputFolder { get; set; } = "";

        /// <summary>Only consulted when <see cref="SeparatePdfFolder"/> is on.</summary>
        public string PdfOutputFolder { get; set; } = "";
        public bool SeparatePdfFolder { get; set; }

        /// <summary>Formats to produce. At least one must be true (the dialog enforces it).</summary>
        public bool ExportDwg { get; set; } = true;
        public bool ExportPdf { get; set; }

        /// <summary>Keys of other open models to include; empty for an active-model-only run.
        /// Keys are document paths, or titles while unsaved.</summary>
        public List<string> ExtraModelKeys { get; set; } = new List<string>();

        /// <summary>When an extra model has no setup with the chosen name: true creates it
        /// there, false skips the model.</summary>
        public bool CopyMissingSetups { get; set; }

        /// <summary>Where PDFs go: the separate folder when asked for and filled in, else the
        /// DWG folder — so an unticked checkbox behaves exactly as before it existed.</summary>
        public string PdfFolder => SeparatePdfFolder && !string.IsNullOrWhiteSpace(PdfOutputFolder)
            ? PdfOutputFolder
            : OutputFolder;
    }

    /// <summary>One view/sheet the export will produce. FileName has no ".dwg" extension.</summary>
    public class PlannedExportFile
    {
        /// <summary>Title of the model this came from; blank on a single-model run.</summary>
        public string ModelTitle { get; set; } = "";
        public string ViewLabel { get; set; } = "";
        public string FileName { get; set; } = "";

        /// <summary>Sheets take the sheets rule, everything else the views rule.</summary>
        public bool IsSheet { get; set; }

        /// <summary>The views rule resolved to nothing here, so the view's own name was used.</summary>
        public bool UsedViewNameFallback { get; set; }

        /// <summary>"Tower-A.rvt — A-101" when the run spans models, else just the label.</summary>
        public string Source => string.IsNullOrEmpty(ModelTitle) ? ViewLabel : ModelTitle + " — " + ViewLabel;
    }

    /// <summary>Pre-flight result: everything the dialog needs to warn/abort before exporting.
    /// (Task 3 replaces DuplicateNames with a richer Duplicates list — leave it as-is here so
    /// Core and its tests keep compiling.)</summary>
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

        /// <summary>Things that went through but the user should know about — a skipped model,
        /// a views rule that resolved to nothing.</summary>
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>A saved PDF export setup as shown in a naming dropdown.</summary>
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
```

- [ ] **Step 4: Run the whole Core suite to verify it is green**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, 0 failed — the three new tests plus everything that was already there.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/DwgExporter/DwgExportTypes.cs tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs
git commit -m "feat(dwg-exporter): a request carries two naming rules, two folders and a model list"
```

---

### Task 3: Planner — duplicates report where each clashing file came from

**Files:**
- Modify: `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs`
- Modify: `src/RVTuk.Core/DwgExporter/DwgExportPlanner.cs`
- Modify: `src/RVTuk.Core/DwgExporter/FileNameComposer.cs` (remove `FindDuplicates`)
- Test: `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/FileNameComposerTests.cs:68-79` (move those two tests)

**Interfaces:**
- Consumes: `PlannedExportFile` and its `Source` property (Task 2).
- Produces: `DuplicateFileName` — `FileName` (string), `Sources` (`IReadOnlyList<string>`); `DwgExportPlan.Duplicates` (`IReadOnlyList<DuplicateFileName>`) replacing `DuplicateNames`; `DwgExportPlanner.Check(IReadOnlyList<PlannedExportFile> files, Func<string, bool> fileExists)` → `DwgExportPlan`, signature unchanged.

Duplicate detection moves out of `FileNameComposer` (which is about composing a name) into the planner (which is about vetting a plan), so there is one home for it and no dead code left behind.

- [ ] **Step 1: Rewrite the planner tests**

In `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`, replace the `File` helper and the first two tests with:

```csharp
    private static PlannedExportFile File(string name, string model = "")
        => new PlannedExportFile { ViewLabel = name, FileName = name, ModelTitle = model };

    [Fact]
    public void Check_CleanPlan_HasNoDuplicatesOrExisting()
    {
        var files = new List<PlannedExportFile> { File("A-101"), File("A-102") };

        var plan = DwgExportPlanner.Check(files, _ => false);

        Assert.Same(files, plan.Files);
        Assert.Empty(plan.Duplicates);
        Assert.Empty(plan.ExistingFileNames);
    }

    [Fact]
    public void Check_ReportsCaseInsensitiveDuplicates_UnderTheFirstSpelling()
    {
        var plan = DwgExportPlanner.Check(
            new List<PlannedExportFile> { File("A-101"), File("a-101"), File("A-102"), File("A-101") },
            _ => false);

        var duplicate = Assert.Single(plan.Duplicates);
        Assert.Equal("A-101", duplicate.FileName);
        Assert.Equal(3, duplicate.Sources.Count);
    }

    [Fact]
    public void Check_DuplicatesAcrossModels_NameTheModels()
    {
        var plan = DwgExportPlanner.Check(
            new List<PlannedExportFile> { File("A-101", "Tower-A.rvt"), File("A-101", "Tower-B.rvt") },
            _ => false);

        var duplicate = Assert.Single(plan.Duplicates);
        Assert.Equal(new[] { "Tower-A.rvt — A-101", "Tower-B.rvt — A-101" }, duplicate.Sources);
    }

    [Fact]
    public void Check_AllUnique_ReportsNoDuplicates()
    {
        Assert.Empty(
            DwgExportPlanner.Check(
                new List<PlannedExportFile> { File("A-101"), File("A-102") }, _ => false).Duplicates);
    }
```

- [ ] **Step 2: Delete the two moved tests**

In `tests/RVTuk.Core.Tests/DwgExporter/FileNameComposerTests.cs`, delete `FindDuplicates_IsCaseInsensitive_AndReturnsEachNameOnce` and `FindDuplicates_Empty_WhenAllUnique` (lines 67–79, including their `[Fact]` attributes). Their coverage now lives in the two planner tests above.

- [ ] **Step 3: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~DwgExporter"
```

Expected: FAIL — `'DwgExportPlan' does not contain a definition for 'Duplicates'`.

- [ ] **Step 4: Swap the plan's duplicate shape**

In `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs`, replace the `DwgExportPlan` class (and its doc comment) with:

```csharp
    /// <summary>One filename two or more views would both produce, and where each came from.</summary>
    public class DuplicateFileName
    {
        public string FileName { get; set; } = "";

        /// <summary>Each clashing view, as "Tower-A.rvt — A-101" once a run spans models.</summary>
        public IReadOnlyList<string> Sources { get; set; } = new List<string>();
    }

    /// <summary>Pre-flight result: everything the dialog needs to warn/abort before exporting.</summary>
    public class DwgExportPlan
    {
        public IReadOnlyList<PlannedExportFile> Files { get; set; } = new List<PlannedExportFile>();
        public IReadOnlyList<DuplicateFileName> Duplicates { get; set; } = new List<DuplicateFileName>();
        public IReadOnlyList<string> ExistingFileNames { get; set; } = new List<string>();
    }
```

- [ ] **Step 5: Move the logic into the planner**

Replace the whole of `src/RVTuk.Core/DwgExporter/DwgExportPlanner.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Pre-flight check before any file is written: duplicate filenames (views would
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
                Duplicates = FindDuplicates(files),
                ExistingFileNames = files.Select(f => f.FileName).Where(fileExists).ToList(),
            };
        }

        /// <summary>Names produced by more than one view, each reported once under the spelling
        /// that occurred first, listing every view — across models — that would write it.</summary>
        private static IReadOnlyList<DuplicateFileName> FindDuplicates(IReadOnlyList<PlannedExportFile> files)
        {
            return files
                .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateFileName
                {
                    FileName = g.First().FileName,
                    Sources = g.Select(f => f.Source).ToList(),
                })
                .ToList();
        }
    }
}
```

- [ ] **Step 6: Remove `FindDuplicates` from the composer**

In `src/RVTuk.Core/DwgExporter/FileNameComposer.cs`, delete the `FindDuplicates` method and its doc comment (lines 38–46), then remove the now-unused `using System.Linq;` from the top of the file. Keep `using System;` (used by `Array.IndexOf`), `System.Collections.Generic`, `System.IO` and `System.Text`.

- [ ] **Step 7: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/RVTuk.Core/DwgExporter tests/RVTuk.Core.Tests/DwgExporter
git commit -m "feat(dwg-exporter): a clashing filename says which model and view produced it"
```

---

### Task 4: `DwgExportSettingsStore` — last-used values out of the view-model

**Files:**
- Create: `src/RVTuk.Core/DwgExporter/DwgExportSettingsStore.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/DwgExportSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `AppConfig` (Task 1), `DwgExportDefaults` (Task 2).
- Produces: `DwgExportSettings` (a plain DTO with `OutputFolder`, `PdfOutputFolder`, `SeparatePdfFolder`, `SheetNamingSetupName`, `ViewNamingSetupName`, `DwgSetupName`, `SheetSetName`, `UseCurrentWindow`, `ExportDwg`, `ExportPdf`, `CopyMissingSetups`); `DwgExportSettingsStore.Read(AppConfig, string modelKey)` → `DwgExportSettings`; `DwgExportSettingsStore.Write(AppConfig, string modelKey, DwgExportSettings)`.

The view-model calls `ConfigManager.LoadConfig()` / `SaveConfig()` around these, so the store itself never touches disk and is testable directly.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/DwgExporter/DwgExportSettingsStoreTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~DwgExportSettingsStoreTests"
```

Expected: FAIL — `The name 'DwgExportSettingsStore' does not exist in the current context`.

- [ ] **Step 3: Write the store**

Create `src/RVTuk.Core/DwgExporter/DwgExportSettingsStore.cs`:

```csharp
using RVTuk.Core.Shared.Config;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>The dialog's remembered choices, in the shape the dialog wants them —
    /// free of the inverted/additive booleans AppConfig has to store them as.</summary>
    public class DwgExportSettings
    {
        public string OutputFolder { get; set; } = "";
        public string PdfOutputFolder { get; set; } = "";
        public bool SeparatePdfFolder { get; set; }
        public string SheetNamingSetupName { get; set; } = "";
        public string ViewNamingSetupName { get; set; } = DwgExportDefaults.ViewNameNamingName;
        public string DwgSetupName { get; set; } = "";
        public string SheetSetName { get; set; } = "";
        public bool UseCurrentWindow { get; set; }
        public bool ExportDwg { get; set; } = true;
        public bool ExportPdf { get; set; }
        public bool CopyMissingSetups { get; set; }
    }

    /// <summary>
    /// Reads and writes the DWG Export dialog's last-used values on an <see cref="AppConfig"/>.
    /// Kept out of the view-model so the storage quirks — inverted format booleans, per-model
    /// folder lists, the empty-means-default view naming key — are testable without WPF or disk.
    /// </summary>
    public static class DwgExportSettingsStore
    {
        public static DwgExportSettings Read(AppConfig config, string modelKey) => new DwgExportSettings
        {
            OutputFolder = config.GetDwgExportFolder(modelKey),
            PdfOutputFolder = config.GetDwgExportPdfFolder(modelKey),
            SeparatePdfFolder = config.DwgExportSeparatePdfFolder,
            // The pre-split key kept its name: it has always meant the sheets rule.
            SheetNamingSetupName = config.DwgExportPdfSetupName,
            ViewNamingSetupName = string.IsNullOrWhiteSpace(config.DwgExportViewNamingSetupName)
                ? DwgExportDefaults.ViewNameNamingName
                : config.DwgExportViewNamingSetupName,
            DwgSetupName = config.DwgExportDwgSetupName,
            SheetSetName = config.DwgExportSheetSetName,
            UseCurrentWindow = config.DwgExportUseCurrentWindow,
            ExportDwg = !config.DwgExportDwgOff,
            ExportPdf = config.DwgExportPdfOn,
            CopyMissingSetups = config.DwgExportCopyMissingSetups,
        };

        public static void Write(AppConfig config, string modelKey, DwgExportSettings settings)
        {
            config.SetDwgExportFolder(modelKey, settings.OutputFolder);
            // With one folder chosen, the PDF folder tracks it — otherwise a stale path would
            // reappear the moment the checkbox is ticked again.
            config.SetDwgExportPdfFolder(modelKey,
                settings.SeparatePdfFolder ? settings.PdfOutputFolder : settings.OutputFolder);
            config.DwgExportSeparatePdfFolder = settings.SeparatePdfFolder;
            config.DwgExportPdfSetupName = settings.SheetNamingSetupName;
            config.DwgExportViewNamingSetupName = settings.ViewNamingSetupName;
            config.DwgExportDwgSetupName = settings.DwgSetupName;
            config.DwgExportSheetSetName = settings.SheetSetName;
            config.DwgExportUseCurrentWindow = settings.UseCurrentWindow;
            config.DwgExportDwgOff = !settings.ExportDwg;
            config.DwgExportPdfOn = settings.ExportPdf;
            config.DwgExportCopyMissingSetups = settings.CopyMissingSetups;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~DwgExportSettingsStoreTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/DwgExporter/DwgExportSettingsStore.cs tests/RVTuk.Core.Tests/DwgExporter/DwgExportSettingsStoreTests.cs
git commit -m "refactor(dwg-exporter): last-used values live in a store, not in the view-model"
```

---

### Task 5: `ModelSetupResolver` — can this model take part, and what must be created first

**Files:**
- Create: `src/RVTuk.Core/DwgExporter/ModelSetupResolver.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/ModelSetupResolverTests.cs`

**Interfaces:**
- Consumes: `DwgExportRequest`, `DwgExportDefaults` (Task 2).
- Produces:
  - `ModelSetupInventory` — `Key`, `Title` (string), `IsReadOnly` (bool), `SheetSetNames`, `PdfSetupNames`, `DwgSetupNames` (all `IReadOnlyList<string>`).
  - `ModelExportPlan` — `Key`, `Title` (string), `CanRun` (bool), `SkipReason` (string), `PdfSetupsToCopy` (`IReadOnlyList<string>`), `CopyDwgSetup` (bool).
  - `ModelSetupResolver.Resolve(ModelSetupInventory model, DwgExportRequest request)` → `ModelExportPlan`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/DwgExporter/ModelSetupResolverTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ModelSetupResolverTests"
```

Expected: FAIL — `The name 'ModelSetupResolver' does not exist in the current context`.

- [ ] **Step 3: Write the resolver**

Create `src/RVTuk.Core/DwgExporter/ModelSetupResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>What one open model offers, by name — all the resolver needs to know about it.</summary>
    public class ModelSetupInventory
    {
        /// <summary>Document path, or title while unsaved.</summary>
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public IReadOnlyList<string> SheetSetNames { get; set; } = new List<string>();
        public IReadOnlyList<string> PdfSetupNames { get; set; } = new List<string>();
        public IReadOnlyList<string> DwgSetupNames { get; set; } = new List<string>();
    }

    /// <summary>Whether a model can take part in the run, and what has to be created in it first.</summary>
    public class ModelExportPlan
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public bool CanRun { get; set; }

        /// <summary>Empty when <see cref="CanRun"/>; otherwise a phrase that reads after
        /// "Tower-B.rvt: ".</summary>
        public string SkipReason { get; set; } = "";

        public IReadOnlyList<string> PdfSetupsToCopy { get; set; } = new List<string>();
        public bool CopyDwgSetup { get; set; }
    }

    /// <summary>
    /// Export setups and view/sheet sets are per-document elements with no cross-document
    /// identity, so an extra model resolves the active model's choices by name. This decides
    /// what that lookup means for one model: run as-is, run after copying setups in, or skip.
    /// Pure — the Revit layer supplies the inventory and performs any copies.
    /// </summary>
    public static class ModelSetupResolver
    {
        public static ModelExportPlan Resolve(ModelSetupInventory model, DwgExportRequest request)
        {
            var plan = new ModelExportPlan { Key = model.Key, Title = model.Title };

            // A ViewSheetSet holds references to views that don't exist in another document,
            // so a missing set can never be copied across — it always skips.
            if (!Has(model.SheetSetNames, request.SheetSetName))
            {
                plan.SkipReason = "no view/sheet set named '" + request.SheetSetName + "'";
                return plan;
            }

            var pdfMissing = RequiredPdfSetups(request)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !Has(model.PdfSetupNames, name))
                .ToList();
            var dwgMissing = RequiresDwgSetup(request) && !Has(model.DwgSetupNames, request.DwgSetupName);

            if (pdfMissing.Count == 0 && !dwgMissing)
            {
                plan.CanRun = true;
                return plan;
            }

            var missing = string.Join(", ",
                pdfMissing.Concat(dwgMissing ? new[] { request.DwgSetupName } : Array.Empty<string>())
                          .Select(n => "'" + n + "'"));

            if (!request.CopyMissingSetups)
            {
                plan.SkipReason = "no export setup named " + missing + " (copying is off)";
                return plan;
            }
            if (model.IsReadOnly)
            {
                plan.SkipReason = "the model is read-only, so " + missing + " cannot be created in it";
                return plan;
            }

            plan.CanRun = true;
            plan.PdfSetupsToCopy = pdfMissing;
            plan.CopyDwgSetup = dwgMissing;
            return plan;
        }

        /// <summary>The naming setups this run needs by name. Both formats need them — the DWG
        /// filenames are evaluated from the same rules — but the two sentinels are built in code
        /// and so need nothing from the model.</summary>
        private static IEnumerable<string> RequiredPdfSetups(DwgExportRequest request)
        {
            if (IsRealSetup(request.SheetNamingSetupName)) yield return request.SheetNamingSetupName;
            if (IsRealSetup(request.ViewNamingSetupName)) yield return request.ViewNamingSetupName;
        }

        private static bool IsRealSetup(string name)
            => !string.IsNullOrWhiteSpace(name)
            && name != DwgExportDefaults.FallbackPdfSetupName
            && name != DwgExportDefaults.ViewNameNamingName;

        private static bool RequiresDwgSetup(DwgExportRequest request)
            => request.ExportDwg
            && !string.IsNullOrWhiteSpace(request.DwgSetupName)
            && request.DwgSetupName != DwgExportDefaults.DefaultDwgSetupName;

        private static bool Has(IReadOnlyList<string> names, string name)
            => names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS — the whole Core suite green (11 new resolver tests included).

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/DwgExporter/ModelSetupResolver.cs tests/RVTuk.Core.Tests/DwgExporter/ModelSetupResolverTests.cs
git commit -m "feat(dwg-exporter): decide per model whether its setups match, can be copied, or must skip"
```

---

### Task 6: `NamingRuleEvaluator` — resolve a rule against any view, and a `<View Name>` rule

**Files:**
- Modify: `src/RVTuk.Revit/DwgExporter/NamingRuleEvaluator.cs`

**Interfaces:**
- Consumes: `NamingRulePart`, `DwgExportDefaults` (Core).
- Produces: `NamingRuleEvaluator.ResolveForView(Document doc, View view, IList<TableCellCombinedParameterData>? rule)` → `IReadOnlyList<NamingRulePart>` (replaces `ResolveForSheet`); `NamingRuleEvaluator.ViewNameRule()` → `IList<TableCellCombinedParameterData>`. `DescribePattern` is unchanged.

There is no Revit in CI, so this task's gate is a compile of both year configurations. Its behaviour is covered by the in-Revit checklist in Task 11.

- [ ] **Step 1: Replace `ResolveForSheet` with `ResolveForView`**

In `src/RVTuk.Revit/DwgExporter/NamingRuleEvaluator.cs`, replace the `ResolveForSheet` method with:

```csharp
        /// <summary>
        /// Resolves the rule against one view. A null rule means the built-in fallback, which
        /// differs by kind: sheets get "&lt;Sheet Number&gt; - &lt;Sheet Name&gt;", other views
        /// their own name.
        /// </summary>
        public static IReadOnlyList<NamingRulePart> ResolveForView(
            Document doc, View view, IList<TableCellCombinedParameterData>? rule)
        {
            if (rule == null || rule.Count == 0)
            {
                if (view is ViewSheet sheet)
                {
                    return new List<NamingRulePart>
                    {
                        new NamingRulePart { Value = sheet.SheetNumber, Separator = " - " },
                        new NamingRulePart { Value = sheet.Name },
                    };
                }
                return new List<NamingRulePart> { new NamingRulePart { Value = view.Name } };
            }

            return rule.Select(entry => new NamingRulePart
            {
                Prefix = entry.Prefix ?? "",
                Value = ResolveValue(doc, view, entry),
                Suffix = entry.Suffix ?? "",
                Separator = entry.Separator ?? "",
            }).ToList();
        }

        /// <summary>
        /// A one-field rule over the view's own Name. The "&lt;View Name&gt;" naming entry is
        /// expressed as a real rule rather than a special case in the export loop, so Revit
        /// names the PDFs exactly the way we name the DWGs.
        /// </summary>
        public static IList<TableCellCombinedParameterData> ViewNameRule()
        {
            var field = TableCellCombinedParameterData.Create();
            field.ParamId = new ElementId(BuiltInParameter.VIEW_NAME);
            return new List<TableCellCombinedParameterData> { field };
        }
```

- [ ] **Step 2: Widen `ResolveValue` and `FindParameter` from sheets to views**

In the same file, replace the `ResolveValue` method with:

```csharp
        /// <summary>
        /// The rule stores which category each field comes from, but resolving is simpler and
        /// more robust by probing: try the view (or sheet) first, then Project Information —
        /// the two sources the PDF naming rule offers.
        /// </summary>
        private static string ResolveValue(Document doc, View view, TableCellCombinedParameterData entry)
        {
            var param = FindParameter(doc, view, entry.ParamId)
                        ?? FindParameter(doc, doc.ProjectInformation, entry.ParamId);
            if (param == null || !param.HasValue) return "";
            return (param.StorageType == StorageType.String ? param.AsString() : param.AsValueString()) ?? "";
        }
```

`FindParameter` already takes an `Element` and needs no change.

- [ ] **Step 3: Verify both year configurations compile**

Run:

```bash
dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024
```

Expected: FAIL with exactly one error, in `SheetDwgExporter.cs`: `'NamingRuleEvaluator' does not contain a definition for 'ResolveForSheet'`. Task 7 fixes it. **Any other error means this task is wrong — fix it before continuing.** Commit anyway: `RVTuk.Revit` is mid-refactor from here until Task 10, and `RVTuk.Core` (the only project with tests) stays green throughout.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/DwgExporter/NamingRuleEvaluator.cs
git commit -m "feat(dwg-exporter): a naming rule can resolve against any view, not just a sheet"
```

---

### Task 7: `SheetDwgExporter` — two rules, two folders, any open document

**Files:**
- Modify: `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs`

**Interfaces:**
- Consumes: `NamingRuleEvaluator.ResolveForView` / `ViewNameRule` (Task 6); `DwgExportRequest`, `PlannedExportFile` (Task 2).
- Produces:
  - `NamingRules` — `Sheet` (`IList<TableCellCombinedParameterData>?`), `View` (same), `UseViewName` (bool).
  - `SheetDwgExporter.ReadNamingRules(Document doc, DwgExportRequest request)` → `NamingRules`.
  - `SheetDwgExporter.PlanFiles(Document doc, DwgExportRequest request, NamingRules rules, string modelTitle, out List<string> skipped)` → `List<(ElementId Id, PlannedExportFile File)>`.
  - `SheetDwgExporter.PlanCurrentWindow(UIDocument uidoc, NamingRules rules)` → the same list type.
  - `SheetDwgExporter.GetDwgOptions(Document doc, string dwgSetupName)` → `DWGExportOptions` (was private).
  - `SheetDwgExporter.GetPdfOptions(Document doc, string namingSetupName, NamingRules rules, bool forViews)` → `PDFExportOptions`.
  - `SheetDwgExporter.Export(Document doc, DwgExportRequest request, List<(ElementId Id, PlannedExportFile File)> files, DWGExportOptions? dwgOptions, PDFExportOptions? sheetPdfOptions, PDFExportOptions? viewPdfOptions, Action<int,int,string> progress)` → `DwgExportResult`.
  - `SheetDwgExporter.OutputFileExists(DwgExportRequest request, string fileName)` → `bool`.

- [ ] **Step 1: Replace the file**

Replace the whole of `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs` with:

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
    /// <summary>The two naming rules a run uses, already read from the chosen setups. A null
    /// rule means the built-in fallback; UseViewName means non-sheet views take their own name.</summary>
    internal class NamingRules
    {
        public IList<TableCellCombinedParameterData>? Sheet { get; set; }
        public IList<TableCellCombinedParameterData>? View { get; set; }
        public bool UseViewName { get; set; } = true;
    }

    /// <summary>
    /// Resolves the requested export range to concrete views + filenames, and runs the export
    /// one view per Document.Export call — the multi-view overload invents its own filenames,
    /// and per-view calls give us exact names, progress, and per-view error capture. Every
    /// method takes the Document explicitly so any open model can be planned and exported,
    /// not only the active one.
    /// </summary>
    internal static class SheetDwgExporter
    {
        /// <summary>Reads both chosen setups' naming rules out of one document.</summary>
        public static NamingRules ReadNamingRules(Document doc, DwgExportRequest request) => new NamingRules
        {
            Sheet = GetNamingRule(doc, request.SheetNamingSetupName),
            View = request.ViewNamingSetupName == DwgExportDefaults.ViewNameNamingName
                ? null
                : GetNamingRule(doc, request.ViewNamingSetupName),
            UseViewName = request.ViewNamingSetupName == DwgExportDefaults.ViewNameNamingName,
        };

        /// <summary>The views/sheets the named set resolves to, with evaluated filenames (no extension).</summary>
        public static List<(ElementId Id, PlannedExportFile File)> PlanFiles(
            Document doc, DwgExportRequest request, NamingRules rules, string modelTitle,
            out List<string> skipped)
        {
            var result = new List<(ElementId, PlannedExportFile)>();
            skipped = new List<string>();

            var set = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .FirstOrDefault(s => s.Name == request.SheetSetName)
                ?? throw new InvalidOperationException(
                    "View/sheet set '" + request.SheetSetName + "' no longer exists in this document.");

            foreach (View view in set.Views)
            {
                if (view is ViewSheet || view.CanBePrinted)
                    result.Add((view.Id, Plan(doc, view, rules, modelTitle)));
                else
                    skipped.Add(view.Name);
            }

            return result
                .OrderBy(x => x.Item2.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The active window's view — only ever the active document, so no model title.</summary>
        public static List<(ElementId Id, PlannedExportFile File)> PlanCurrentWindow(
            UIDocument uidoc, NamingRules rules)
        {
            var view = uidoc.ActiveGraphicalView
                ?? throw new InvalidOperationException("The active window is not an exportable graphical view.");
            return new List<(ElementId, PlannedExportFile)> { (view.Id, Plan(uidoc.Document, view, rules, "")) };
        }

        private static PlannedExportFile Plan(Document doc, View view, NamingRules rules, string modelTitle)
        {
            var isSheet = view is ViewSheet;
            var usedFallback = false;
            string name;

            if (isSheet)
            {
                name = FileNameComposer.Compose(NamingRuleEvaluator.ResolveForView(doc, view, rules.Sheet));
            }
            else if (rules.UseViewName)
            {
                name = view.Name;
            }
            else
            {
                name = FileNameComposer.Compose(NamingRuleEvaluator.ResolveForView(doc, view, rules.View));
                // A rule written for sheets can resolve to nothing on a view. Falling back to
                // the view's own name keeps the files apart; Sanitize's generic "Sheet" would
                // collapse every such view onto one name and abort the run as a duplicate.
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = view.Name;
                    usedFallback = true;
                }
            }

            return new PlannedExportFile
            {
                ModelTitle = modelTitle,
                ViewLabel = Label(view),
                FileName = FileNameComposer.Sanitize(name),
                IsSheet = isSheet,
                UsedViewNameFallback = usedFallback,
            };
        }

        public static DwgExportResult Export(
            Document doc,
            DwgExportRequest request,
            List<(ElementId Id, PlannedExportFile File)> files,
            DWGExportOptions? dwgOptions,
            PDFExportOptions? sheetPdfOptions,
            PDFExportOptions? viewPdfOptions,
            Action<int, int, string> progress)
        {
            var tag = request.ExportDwg && request.ExportPdf ? " (DWG+PDF)"
                : request.ExportPdf ? " (PDF)" : " (DWG)";
            var result = new DwgExportResult();

            for (int i = 0; i < files.Count; i++)
            {
                var (id, file) = files[i];
                if (dwgOptions != null)
                {
                    try
                    {
                        var ok = doc.Export(request.OutputFolder, file.FileName,
                            new List<ElementId> { id }, dwgOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.Source + " (DWG): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.Source + " (DWG): " + ex.Message);
                    }
                }

                var pdfOptions = file.IsSheet ? sheetPdfOptions : viewPdfOptions;
                if (pdfOptions != null)
                {
                    try
                    {
                        // Revit evaluates the setup's naming rule itself, so the .pdf
                        // basename matches the native PDF export byte for byte.
                        var ok = doc.Export(request.PdfFolder, new List<ElementId> { id }, pdfOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.Source + " (PDF): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.Source + " (PDF): " + ex.Message);
                    }
                }

                if (file.UsedViewNameFallback)
                {
                    result.Notes.Add(file.Source +
                        ": the views naming rule produced nothing here, so the view name was used.");
                }

                progress(i + 1, files.Count, file.Source + tag);
            }

            return result;
        }

        /// <summary>Null when the fallback pseudo-setup is selected (document has no PDF setups).</summary>
        public static IList<TableCellCombinedParameterData>? GetNamingRule(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName) return null;

            var settings = ExportPDFSettings.FindByName(doc, pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            return settings.GetOptions().GetNamingRule();
        }

        /// <summary>
        /// Options for the per-view PDF calls: the chosen setup's own options with Combine
        /// forced off (one PDF per view, names mirror the DWGs 1:1).
        /// <paramref name="forViews"/> with the "&lt;View Name&gt;" entry chosen borrows the
        /// sheets setup's page settings and swaps in the view-name rule — the user's paper and
        /// quality choices shouldn't change just because an item is a view.
        /// </summary>
        public static PDFExportOptions GetPdfOptions(
            Document doc, string namingSetupName, NamingRules rules, bool forViews)
        {
            if (forViews && rules.UseViewName)
            {
                var borrowed = BaseOptions(doc, namingSetupName);
                borrowed.SetNamingRule(NamingRuleEvaluator.ViewNameRule());
                return borrowed;
            }
            return BaseOptions(doc, namingSetupName);
        }

        private static PDFExportOptions BaseOptions(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName)
            {
                var number = TableCellCombinedParameterData.Create();
                number.ParamId = new ElementId(BuiltInParameter.SHEET_NUMBER);
                number.Separator = " - ";
                var name = TableCellCombinedParameterData.Create();
                name.ParamId = new ElementId(BuiltInParameter.SHEET_NAME);

                var fallback = new PDFExportOptions { Combine = false };
                fallback.SetNamingRule(new List<TableCellCombinedParameterData> { number, name });
                return fallback;
            }

            var settings = ExportPDFSettings.FindByName(doc, pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            var options = settings.GetOptions();
            options.Combine = false;
            return options;
        }

        public static DWGExportOptions GetDwgOptions(Document doc, string dwgSetupName)
        {
            if (dwgSetupName == DwgExportDefaults.DefaultDwgSetupName) return new DWGExportOptions();

            var settings = ExportDWGSettings.FindByName(doc, dwgSetupName)
                ?? throw new InvalidOperationException(
                    "DWG export setup '" + dwgSetupName + "' no longer exists in this document.");
            return settings.GetDWGExportOptions();
        }

        private static string Label(View view)
            => view is ViewSheet sheet ? sheet.SheetNumber + " - " + sheet.Name : view.Name;

        /// <summary>Whether any output of the requested formats already exists — each format
        /// checked in its own folder, which may or may not be the same one.</summary>
        public static bool OutputFileExists(DwgExportRequest request, string fileName)
            => (request.ExportDwg && File.Exists(Path.Combine(request.OutputFolder, fileName + ".dwg")))
            || (request.ExportPdf && File.Exists(Path.Combine(request.PdfFolder, fileName + ".pdf")));
    }
}
```

- [ ] **Step 2: Verify the file's own errors are gone**

Run:

```bash
dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024
```

Expected: FAIL, but only in `Commands/DwgExportCommand.cs` (it still calls the old `PlanFiles`/`Export` signatures and `request.PdfSetupName`). No errors reported in `SheetDwgExporter.cs` or `NamingRuleEvaluator.cs`. Tasks 8–9 clear the rest.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs
git commit -m "feat(dwg-exporter): sheets and views take their own naming rule and their own folder"
```

---

### Task 8: `OpenModels` and `SetupTransfer`

**Files:**
- Create: `src/RVTuk.Revit/DwgExporter/OpenModels.cs`
- Create: `src/RVTuk.Revit/DwgExporter/SetupTransfer.cs`

**Interfaces:**
- Consumes: `ModelSetupInventory` (Task 5).
- Produces:
  - `OpenModels.Enumerate(Autodesk.Revit.ApplicationServices.Application app)` → `List<Document>`.
  - `OpenModels.KeyOf(Document doc)` → `string`.
  - `OpenModels.ReadInventory(Document doc)` → `ModelSetupInventory`.
  - `SetupTransfer.Remap(IList<TableCellCombinedParameterData> rule, Document source, Document target, out List<string> unresolved)` → `IList<TableCellCombinedParameterData>?` (null when anything was unresolved).
  - `SetupTransfer.CopyPdfSetup(Document source, Document target, string name)` → void, throws `InvalidOperationException` with a reason phrase.
  - `SetupTransfer.CopyDwgSetup(Document source, Document target, string name)` → void, same.

- [ ] **Step 1: Write `OpenModels`**

Create `src/RVTuk.Revit/DwgExporter/OpenModels.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>The other models a run can reach, and what each of them offers by name.</summary>
    internal static class OpenModels
    {
        /// <summary>Project documents open in this Revit session. Links and families are
        /// excluded — neither has view/sheet sets to export — as are handles Revit has already
        /// invalidated.</summary>
        public static List<Document> Enumerate(Autodesk.Revit.ApplicationServices.Application app)
        {
            var docs = new List<Document>();
            foreach (Document doc in app.Documents)
            {
                if (doc == null || !doc.IsValidObject) continue;
                if (doc.IsLinked || doc.IsFamilyDocument) continue;
                docs.Add(doc);
            }
            return docs;
        }

        /// <summary>The same key the config uses to remember folders: the document path, or the
        /// title while the document is unsaved.</summary>
        public static string KeyOf(Document doc)
            => string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;

        public static ModelSetupInventory ReadInventory(Document doc) => new ModelSetupInventory
        {
            Key = KeyOf(doc),
            Title = doc.Title,
            IsReadOnly = doc.IsReadOnly,
            SheetSetNames = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .Select(s => s.Name)
                .ToList(),
            PdfSetupNames = ExportPDFSettings.ListNames(doc).ToList(),
            DwgSetupNames = ExportDWGSettings.ListNames(doc).ToList(),
        };
    }
}
```

- [ ] **Step 2: Write `SetupTransfer`**

Create `src/RVTuk.Revit/DwgExporter/SetupTransfer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Reproduces one model's export setups in another. Used when a run spans models and the
    /// user asked for missing setups to be created rather than the model skipped — this writes
    /// into that model, so every caller must have checked <c>Document.IsReadOnly</c> first.
    /// </summary>
    internal static class SetupTransfer
    {
        /// <summary>
        /// Rebuilds a naming rule against another document. Built-in parameters have negative
        /// ids that mean the same thing in every document; a shared or project parameter's id
        /// is per-document, so it is matched by name instead. Returns null when any field has
        /// no counterpart in the target — a rule that would silently resolve to blanks is worse
        /// than skipping the model. The source's own field objects are never mutated.
        /// </summary>
        public static IList<TableCellCombinedParameterData>? Remap(
            IList<TableCellCombinedParameterData> rule,
            Document source,
            Document target,
            out List<string> unresolved)
        {
            unresolved = new List<string>();
            var rebuilt = new List<TableCellCombinedParameterData>();

            foreach (var entry in rule)
            {
                var field = TableCellCombinedParameterData.Create();
                field.Prefix = entry.Prefix ?? "";
                field.Suffix = entry.Suffix ?? "";
                field.Separator = entry.Separator ?? "";
                // CategoryId is a BuiltInCategory: negative, and the same in every document.
                field.CategoryId = entry.CategoryId;

                if (entry.ParamId.Value < 0)
                {
                    field.ParamId = entry.ParamId;
                }
                else
                {
                    var name = (source.GetElement(entry.ParamId) as ParameterElement)?.Name;
                    var match = name == null ? null : FindParameterElement(target, name);
                    if (match == null)
                    {
                        unresolved.Add(name ?? "parameter " + entry.ParamId.Value);
                        continue;
                    }
                    field.ParamId = match.Id;
                }

                rebuilt.Add(field);
            }

            return unresolved.Count > 0 ? null : rebuilt;
        }

        /// <summary>Creates the named PDF setup in the target document, naming rule and all.
        /// No-op when it is already there.</summary>
        public static void CopyPdfSetup(Document source, Document target, string name)
        {
            if (ExportPDFSettings.FindByName(target, name) != null) return;

            var from = ExportPDFSettings.FindByName(source, name)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + name + "' no longer exists in the active model.");
            if (!ExportPDFSettings.IsValidName(target, name))
                throw new InvalidOperationException("'" + name + "' is not a valid setup name in this model.");

            var options = from.GetOptions();
            var rule = options.GetNamingRule();
            if (rule != null && rule.Count > 0)
            {
                var remapped = Remap(rule, source, target, out var unresolved);
                if (remapped == null)
                {
                    throw new InvalidOperationException(
                        "'" + name + "' uses parameter(s) this model doesn't have: " +
                        string.Join(", ", unresolved));
                }
                options.SetNamingRule(remapped);
            }

            using var tx = new Transaction(target, "RVTuk – copy PDF export setup");
            tx.Start();
            ExportPDFSettings.Create(target, name, options);
            tx.Commit();
        }

        /// <summary>Creates the named DWG setup in the target document. Its layer table is keyed
        /// by category and subcategory name, not by element id, so it carries over as-is.</summary>
        public static void CopyDwgSetup(Document source, Document target, string name)
        {
            if (ExportDWGSettings.FindByName(target, name) != null) return;

            var from = ExportDWGSettings.FindByName(source, name)
                ?? throw new InvalidOperationException(
                    "DWG export setup '" + name + "' no longer exists in the active model.");

            var options = from.GetDWGExportOptions();

            using var tx = new Transaction(target, "RVTuk – copy DWG export setup");
            tx.Start();
            ExportDWGSettings.Create(target, name, options);
            tx.Commit();
        }

        private static ParameterElement? FindParameterElement(Document doc, string name)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterElement))
                .Cast<ParameterElement>()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3: Verify both new files compile**

Run:

```bash
dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024
```

Expected: FAIL, but with no errors reported in `OpenModels.cs` or `SetupTransfer.cs` — only the remaining ones in `Commands/DwgExportCommand.cs`.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/DwgExporter/OpenModels.cs src/RVTuk.Revit/DwgExporter/SetupTransfer.cs
git commit -m "feat(dwg-exporter): read what each open model offers, and reproduce a setup in another one"
```

---

### Task 9: `DwgExportRunner` and a thin command

**Files:**
- Create: `src/RVTuk.Revit/DwgExporter/DwgExportRunner.cs`
- Modify: `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs`

**Interfaces:**
- Consumes: everything from Tasks 5–8, plus `DwgExportPlanner.Check` (Task 3).
- Produces:
  - `DwgExportRunner(UIDocument uidoc)`.
  - `DwgExportRunner.EvaluateExample(DwgExportRequest request)` → `string`.
  - `DwgExportRunner.Plan(DwgExportRequest request)` → `DwgExportPlan`.
  - `DwgExportRunner.Run(DwgExportRequest request, Action<int,int,string> progress)` → `DwgExportResult`.
- Produces for Task 10: the view-model constructor takes `IReadOnlyList<ModelSetupInventory> models` and `string activeModelKey` in place of `string modelKey`, and its three delegates keep their existing shapes.

- [ ] **Step 1: Write the runner**

Create `src/RVTuk.Revit/DwgExporter/DwgExportRunner.cs`:

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
    /// Turns one dialog request into work across one or more open models: resolve each ticked
    /// model's setups by name, create any the user approved copying, plan every model's files
    /// into one list (so duplicates are caught across models before anything is written), then
    /// export model by model. Runs inside the command's API context on the UI thread.
    /// </summary>
    internal class DwgExportRunner
    {
        private readonly UIDocument _uidoc;
        private readonly Document _active;

        public DwgExportRunner(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _active = uidoc.Document;
        }

        /// <summary>One model's share of a run: the document, its files, and its options.</summary>
        private class Job
        {
            public Document Doc { get; set; } = null!;
            public List<(ElementId Id, PlannedExportFile File)> Files { get; set; } =
                new List<(ElementId, PlannedExportFile)>();
            public NamingRules Rules { get; set; } = new NamingRules();
        }

        public string EvaluateExample(DwgExportRequest request)
        {
            var rules = SheetDwgExporter.ReadNamingRules(_active, request);
            var files = request.CurrentWindow
                ? SheetDwgExporter.PlanCurrentWindow(_uidoc, rules)
                : SheetDwgExporter.PlanFiles(_active, request, rules, "", out _);
            return files.Count == 0 ? "(no views in the selected set)" : files[0].File.FileName + ".dwg";
        }

        public DwgExportPlan Plan(DwgExportRequest request)
        {
            Directory.CreateDirectory(request.OutputFolder);
            if (request.ExportPdf) Directory.CreateDirectory(request.PdfFolder);

            var jobs = BuildJobs(request, out _, out _);
            var files = jobs.SelectMany(j => j.Files).Select(f => f.File).ToList();
            return DwgExportPlanner.Check(files, name => SheetDwgExporter.OutputFileExists(request, name));
        }

        public DwgExportResult Run(DwgExportRequest request, Action<int, int, string> progress)
        {
            var jobs = BuildJobs(request, out var skippedModels, out var skippedViews);
            var total = jobs.Sum(j => j.Files.Count);
            var done = 0;
            var result = new DwgExportResult();

            foreach (var job in jobs)
            {
                var dwgOptions = request.ExportDwg
                    ? SheetDwgExporter.GetDwgOptions(job.Doc, request.DwgSetupName)
                    : null;
                var sheetPdfOptions = request.ExportPdf
                    ? SheetDwgExporter.GetPdfOptions(job.Doc, request.SheetNamingSetupName, job.Rules, forViews: false)
                    : null;
                var viewPdfOptions = request.ExportPdf
                    ? SheetDwgExporter.GetPdfOptions(
                        job.Doc,
                        job.Rules.UseViewName ? request.SheetNamingSetupName : request.ViewNamingSetupName,
                        job.Rules,
                        forViews: true)
                    : null;

                var offset = done;
                var one = SheetDwgExporter.Export(
                    job.Doc, request, job.Files, dwgOptions, sheetPdfOptions, viewPdfOptions,
                    (i, _, label) => progress(offset + i, total, label));

                done += job.Files.Count;
                result.ExportedCount += one.ExportedCount;
                result.Errors.AddRange(one.Errors);
                result.Notes.AddRange(one.Notes);
            }

            result.Notes.AddRange(skippedModels);
            result.Notes.AddRange(skippedViews.Select(name => name + ": skipped (not an exportable view)"));
            return result;
        }

        /// <summary>
        /// The active model always runs. Each extra model is resolved by name; models the
        /// resolver rejects become notes rather than failures, and any approved copies are made
        /// before that model's files are planned (the rules must be readable from it afterwards).
        /// </summary>
        private List<Job> BuildJobs(
            DwgExportRequest request, out List<string> skippedModels, out List<string> skippedViews)
        {
            skippedModels = new List<string>();
            skippedViews = new List<string>();
            var jobs = new List<Job>();

            var activeRules = SheetDwgExporter.ReadNamingRules(_active, request);
            if (request.CurrentWindow)
            {
                jobs.Add(new Job
                {
                    Doc = _active,
                    Rules = activeRules,
                    Files = SheetDwgExporter.PlanCurrentWindow(_uidoc, activeRules),
                });
                return jobs; // the active window is only ever the active model
            }

            var extras = request.ExtraModelKeys ?? new List<string>();
            var multi = extras.Count > 0;
            jobs.Add(new Job
            {
                Doc = _active,
                Rules = activeRules,
                Files = SheetDwgExporter.PlanFiles(
                    _active, request, activeRules, multi ? _active.Title : "", out var activeSkipped),
            });
            skippedViews.AddRange(activeSkipped);

            if (!multi) return jobs;

            var byKey = OpenModels.Enumerate(_active.Application)
                .Where(d => !ReferenceEquals(d, _active))
                .ToDictionary(OpenModels.KeyOf, d => d, StringComparer.OrdinalIgnoreCase);

            foreach (var key in extras)
            {
                if (!byKey.TryGetValue(key, out var doc))
                {
                    skippedModels.Add(key + ": skipped (no longer open)");
                    continue;
                }

                var plan = ModelSetupResolver.Resolve(OpenModels.ReadInventory(doc), request);
                if (!plan.CanRun)
                {
                    skippedModels.Add(doc.Title + ": skipped — " + plan.SkipReason);
                    continue;
                }

                try
                {
                    foreach (var name in plan.PdfSetupsToCopy)
                        SetupTransfer.CopyPdfSetup(_active, doc, name);
                    if (plan.CopyDwgSetup)
                        SetupTransfer.CopyDwgSetup(_active, doc, request.DwgSetupName);
                }
                catch (Exception ex)
                {
                    skippedModels.Add(doc.Title + ": skipped — " + ex.Message);
                    continue;
                }

                try
                {
                    var rules = SheetDwgExporter.ReadNamingRules(doc, request);
                    jobs.Add(new Job
                    {
                        Doc = doc,
                        Rules = rules,
                        Files = SheetDwgExporter.PlanFiles(doc, request, rules, doc.Title, out var skipped),
                    });
                    skippedViews.AddRange(skipped.Select(n => doc.Title + " — " + n));
                }
                catch (Exception ex)
                {
                    skippedModels.Add(doc.Title + ": skipped — " + ex.Message);
                }
            }

            return jobs;
        }
    }
}
```

- [ ] **Step 2: Slim the command down to wiring**

Replace the whole of `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs` with:

```csharp
using System;
using System.Collections.Generic;
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
                // ── Dropdown contents (all from the active model) ────────────────────
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

                var dwgNames = ExportDWGSettings.ListNames(doc)
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

                // Active model first, so the dialog can lock its checkbox on.
                var models = OpenModels.Enumerate(commandData.Application.Application)
                    .OrderByDescending(d => ReferenceEquals(d, doc))
                    .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(OpenModels.ReadInventory)
                    .ToList();

                // ── Delegates (run on the UI thread inside this command's API context) ──
                var runner = new DwgExportRunner(uidoc);

                var vm = new DwgExportViewModel(
                    pdfItems, dwgNames, sheetSets, currentViewLabel,
                    models, OpenModels.KeyOf(doc),
                    runner.EvaluateExample, runner.Plan, runner.Run);

                vm.OpenNativeDialog = kind =>
                {
                    // "sets" prefers Publish Settings (a dedicated view/sheet-set manager);
                    // Revit greys it out for some model contexts, so the PDF Export dialog —
                    // whose pencil also edits sets — is the fallback.
                    var candidates = kind switch
                    {
                        "sets" => new[] { PostableCommand.PublishSettings, PostableCommand.ExportPDF },
                        "dwgsetups" => new[] { PostableCommand.ExportOptionsExportSetupsDWGOrDXF },
                        _ => new[] { PostableCommand.ExportPDF },
                    };
                    foreach (var postable in candidates)
                    {
                        try
                        {
                            var id = RevitCommandId.LookupPostableCommandId(postable);
                            if (id != null && commandData.Application.CanPostCommand(id))
                            {
                                commandData.Application.PostCommand(id);
                                return true;
                            }
                        }
                        catch
                        {
                            // another command already posted, or id unavailable — try next
                        }
                    }
                    return false;
                };

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

- [ ] **Step 3: Verify only the view-model errors remain**

Run:

```bash
dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024
```

Expected: FAIL with errors only about `DwgExportViewModel`'s constructor arity — no errors inside `src/RVTuk.Revit/DwgExporter/`. Task 10 clears them.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/DwgExporter
git commit -m "feat(dwg-exporter): one run can span several open models"
```

---

### Task 10: The dialog — two naming rows, a models section, a second folder

**Files:**
- Create: `src/RVTuk.UI/DwgExporter/ViewModels/ModelSelectionItem.cs`
- Modify: `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`
- Modify: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml`
- Modify: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml.cs`

**Interfaces:**
- Consumes: `DwgExportSettingsStore` (Task 4), `ModelSetupInventory` (Task 5), the reshaped `DwgExportRequest`/`DwgExportPlan` (Tasks 2–3), and the view-model constructor shape Task 9 defined.
- Produces: the completed feature.

- [ ] **Step 1: Add the model row view-model**

Create `src/RVTuk.UI/DwgExporter/ViewModels/ModelSelectionItem.cs`:

```csharp
using RVTuk.Core.DwgExporter;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.DwgExporter.ViewModels
{
    /// <summary>One row of the Models list: an open model and whether the run includes it.
    /// The active model is always included and its checkbox is locked on.</summary>
    public class ModelSelectionItem : ViewModelBase
    {
        public ModelSelectionItem(ModelSetupInventory model, bool isActive)
        {
            Key = model.Key;
            Title = model.Title;
            IsReadOnly = model.IsReadOnly;
            IsActive = isActive;
            _isSelected = isActive;
        }

        public string Key { get; }
        public string Title { get; }
        public bool IsReadOnly { get; }
        public bool IsActive { get; }

        /// <summary>The active model can't be unticked — it is the model the dialog read its
        /// sets and setups from.</summary>
        public bool CanUnselect => !IsActive;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, IsActive || value);
        }

        public string Display => IsActive
            ? Title + "  (active)"
            : IsReadOnly ? Title + "  (read-only)" : Title;
    }
}
```

- [ ] **Step 2: Rework the view-model**

In `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`:

Add `using System.Collections.ObjectModel;` to the usings.

Replace the `_modelKey` field and its doc comment with:

```csharp
        /// <summary>Identifies the active model (document path, or title while unsaved) so the
        /// output folders can be remembered per model.</summary>
        private readonly string _activeModelKey;
```

Replace the `PdfSetups` … `CurrentViewLabel` property block with:

```csharp
        public IReadOnlyList<PdfSetupItem> PdfSetups { get; }
        /// <summary>The sheets list plus a "&lt;View Name&gt;" entry at the top.</summary>
        public IReadOnlyList<PdfSetupItem> ViewNamingOptions { get; }
        public IReadOnlyList<string> DwgSetupNames { get; }
        public IReadOnlyList<SheetSetItem> SheetSets { get; }
        public string CurrentViewLabel { get; }
        public ObservableCollection<ModelSelectionItem> Models { get; }

        /// <summary>Only meaningful when more than one model is open.</summary>
        public bool HasOtherModels => Models.Count > 1;
```

Replace the constructor (signature and body up to `RefreshExample();`) with:

```csharp
        public DwgExportViewModel(
            IReadOnlyList<PdfSetupItem> pdfSetups,
            IReadOnlyList<string> dwgSetupNames,
            IReadOnlyList<SheetSetItem> sheetSets,
            string currentViewLabel,
            IReadOnlyList<ModelSetupInventory> models,
            string activeModelKey,
            Func<DwgExportRequest, string> evaluateExample,
            Func<DwgExportRequest, DwgExportPlan> planExport,
            Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport)
        {
            PdfSetups = pdfSetups;
            ViewNamingOptions = new[]
                {
                    new PdfSetupItem
                    {
                        Name = DwgExportDefaults.ViewNameNamingName,
                        Pattern = DwgExportDefaults.ViewNameNamingName,
                    },
                }
                .Concat(pdfSetups)
                .ToList();
            DwgSetupNames = dwgSetupNames;
            SheetSets = sheetSets;
            CurrentViewLabel = currentViewLabel;
            Models = new ObservableCollection<ModelSelectionItem>(
                models.Select(m => new ModelSelectionItem(m, isActive: m.Key == activeModelKey)));
            _activeModelKey = activeModelKey;
            _evaluateExample = evaluateExample;
            _planExport = planExport;
            _runExport = runExport;

            ExportCommand = new RelayCommand(Export, () => CanExport);

            // Restore last-used choices; unknown names fall back to the first entry.
            var settings = DwgExportSettingsStore.Read(ConfigManager.LoadConfig(), activeModelKey);
            _outputFolder = settings.OutputFolder;
            _pdfOutputFolder = settings.PdfOutputFolder;
            _separatePdfFolder = settings.SeparatePdfFolder;
            _copyMissingSetups = settings.CopyMissingSetups;
            _useCurrentWindow = settings.UseCurrentWindow || sheetSets.Count == 0;
            _selectedSheetNaming =
                pdfSetups.FirstOrDefault(s => s.Name == settings.SheetNamingSetupName) ?? pdfSetups.FirstOrDefault();
            _selectedViewNaming =
                ViewNamingOptions.FirstOrDefault(s => s.Name == settings.ViewNamingSetupName)
                ?? ViewNamingOptions.FirstOrDefault();
            _selectedDwgSetup =
                dwgSetupNames.FirstOrDefault(n => n == settings.DwgSetupName) ?? dwgSetupNames.FirstOrDefault();
            _selectedSheetSet =
                sheetSets.FirstOrDefault(s => s.Name == settings.SheetSetName) ?? sheetSets.FirstOrDefault();
            _exportDwgFormat = settings.ExportDwg;
            _exportPdfFormat = settings.ExportPdf;

            RefreshExample();
        }
```

Replace the `UseCurrentWindow` setter body so the Models section follows the range, and add the new properties. Replace the `UseCurrentWindow` property and the `SelectedPdfSetup` / `PatternText` properties with:

```csharp
        private bool _useCurrentWindow;
        public bool UseCurrentWindow
        {
            get => _useCurrentWindow;
            set
            {
                SetProperty(ref _useCurrentWindow, value);
                OnPropertyChanged(nameof(UseSheetSet)); // keep the inverse radio in sync
                OnPropertyChanged(nameof(MultiModelEnabled));
                RefreshExample();
            }
        }

        /// <summary>Other models can only join a saved-set run — "current window" is by
        /// definition the active model's.</summary>
        public bool MultiModelEnabled => !_useCurrentWindow && HasOtherModels;

        private PdfSetupItem? _selectedSheetNaming;
        public PdfSetupItem? SelectedSheetNaming
        {
            get => _selectedSheetNaming;
            set
            {
                SetProperty(ref _selectedSheetNaming, value);
                OnPropertyChanged(nameof(SheetPatternText));
                RefreshExample();
            }
        }

        private PdfSetupItem? _selectedViewNaming;
        public PdfSetupItem? SelectedViewNaming
        {
            get => _selectedViewNaming;
            set
            {
                SetProperty(ref _selectedViewNaming, value);
                OnPropertyChanged(nameof(ViewPatternText));
                RefreshExample();
            }
        }

        public string SheetPatternText => _selectedSheetNaming?.Pattern ?? "";
        public string ViewPatternText => _selectedViewNaming?.Pattern ?? "";

        private bool _separatePdfFolder;
        public bool SeparatePdfFolder
        {
            get => _separatePdfFolder;
            set => SetProperty(ref _separatePdfFolder, value);
        }

        private string _pdfOutputFolder = "";
        public string PdfOutputFolder
        {
            get => _pdfOutputFolder;
            set => SetProperty(ref _pdfOutputFolder, value);
        }

        private bool _copyMissingSetups;
        /// <summary>True creates a missing setup in the other model; false skips that model.</summary>
        public bool CopyMissingSetups
        {
            get => _copyMissingSetups;
            set { SetProperty(ref _copyMissingSetups, value); OnPropertyChanged(nameof(SkipMissingSetups)); }
        }

        // Inverse binding target for the "skip" radio button.
        public bool SkipMissingSetups
        {
            get => !_copyMissingSetups;
            set => CopyMissingSetups = !value;
        }
```

Replace `CanExport` and `BuildRequest` with:

```csharp
        public bool CanExport =>
            !IsExporting
            && !string.IsNullOrWhiteSpace(OutputFolder)
            && (!SeparatePdfFolder || !ExportPdfFormat || !string.IsNullOrWhiteSpace(PdfOutputFolder))
            && SelectedSheetNaming != null
            && SelectedViewNaming != null
            && SelectedDwgSetup != null
            && (ExportDwgFormat || ExportPdfFormat)
            && (UseCurrentWindow || SelectedSheetSet != null);

        private DwgExportRequest BuildRequest() => new DwgExportRequest
        {
            CurrentWindow = UseCurrentWindow,
            SheetSetName = SelectedSheetSet?.Name ?? "",
            SheetNamingSetupName = SelectedSheetNaming?.Name ?? "",
            ViewNamingSetupName = SelectedViewNaming?.Name ?? DwgExportDefaults.ViewNameNamingName,
            DwgSetupName = SelectedDwgSetup ?? "",
            OutputFolder = OutputFolder.Trim(),
            PdfOutputFolder = PdfOutputFolder.Trim(),
            SeparatePdfFolder = SeparatePdfFolder,
            ExportDwg = ExportDwgFormat,
            ExportPdf = ExportPdfFormat,
            CopyMissingSetups = CopyMissingSetups,
            ExtraModelKeys = MultiModelEnabled
                ? Models.Where(m => m.IsSelected && !m.IsActive).Select(m => m.Key).ToList()
                : new List<string>(),
        };
```

In `Export()`, replace the duplicate-name block with:

```csharp
            if (plan.Duplicates.Count > 0)
            {
                ShowError?.Invoke(
                    "These filenames would be produced by more than one view, so files would " +
                    "overwrite each other. Fix the naming rule or the view parameters:\n\n  " +
                    string.Join("\n  ", plan.Duplicates.Select(
                        d => d.FileName + "  ←  " + string.Join(", ", d.Sources))));
                return;
            }
```

Still in `Export()`, replace the `StatusText = result.Errors.Count == 0 …` assignment with:

```csharp
                var lines = new List<string>
                {
                    result.Errors.Count == 0
                        ? "Exported " + result.ExportedCount + " file(s) to " + request.OutputFolder
                        : "Exported " + result.ExportedCount + ", failed " + result.Errors.Count + ":",
                };
                lines.AddRange(result.Errors);
                lines.AddRange(result.Notes);
                StatusText = string.Join("\n", lines);
```

Finally, replace `SaveLastUsed` with:

```csharp
        private void SaveLastUsed()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                DwgExportSettingsStore.Write(config, _activeModelKey, new DwgExportSettings
                {
                    OutputFolder = OutputFolder.Trim(),
                    PdfOutputFolder = PdfOutputFolder.Trim(),
                    SeparatePdfFolder = SeparatePdfFolder,
                    SheetNamingSetupName = SelectedSheetNaming?.Name ?? "",
                    ViewNamingSetupName = SelectedViewNaming?.Name ?? DwgExportDefaults.ViewNameNamingName,
                    DwgSetupName = SelectedDwgSetup ?? "",
                    SheetSetName = SelectedSheetSet?.Name ?? "",
                    UseCurrentWindow = UseCurrentWindow,
                    ExportDwg = ExportDwgFormat,
                    ExportPdf = ExportPdfFormat,
                    CopyMissingSetups = CopyMissingSetups,
                });
                ConfigManager.SaveConfig(config);
            }
            catch
            {
                // Remembering settings is best-effort; never fail an export over it.
            }
        }
```

- [ ] **Step 3: Update the window markup**

In `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml`:

Change the window width so the two naming rows don't crowd:

```xml
        Width="560" SizeToContent="Height" MinWidth="480"
```

Replace the whole "File Naming" `<Border>` block (lines 63–92) with:

```xml
        <!-- ── File Naming ──────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <StackPanel>
                <TextBlock Text="File Naming (from PDF export setups)" Style="{StaticResource SectionHeader}"/>
                <DockPanel>
                    <TextBlock Text="Sheets:  " Width="56" VerticalAlignment="Center"/>
                    <Button Content="Edit…" DockPanel.Dock="Right" Padding="8,2" Margin="6,0,0,0"
                            Click="EditPdfDialog_Click"
                            ToolTip="Closes this window and opens Revit's PDF Export dialog — edit the setup's naming rule there (pencil button), then reopen DWG Export."/>
                    <ComboBox ItemsSource="{Binding PdfSetups}"
                              SelectedItem="{Binding SelectedSheetNaming}"
                              DisplayMemberPath="Name"/>
                </DockPanel>
                <TextBlock Text="{Binding SheetPatternText}" Margin="56,2,0,0"
                           TextWrapping="Wrap" Foreground="{StaticResource Brush.TextMuted}"/>
                <DockPanel Margin="0,8,0,0">
                    <TextBlock Text="Views:  " Width="56" VerticalAlignment="Center"/>
                    <ComboBox ItemsSource="{Binding ViewNamingOptions}"
                              SelectedItem="{Binding SelectedViewNaming}"
                              DisplayMemberPath="Name"
                              ToolTip="Applies to non-sheet views in the set. &lt;View Name&gt; uses the view's own name."/>
                </DockPanel>
                <TextBlock Text="{Binding ViewPatternText}" Margin="56,2,0,0"
                           TextWrapping="Wrap" Foreground="{StaticResource Brush.TextMuted}"/>
                <Grid Margin="0,8,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Example:  "/>
                    <TextBlock Grid.Column="1" Text="{Binding ExampleText}"
                               TextWrapping="Wrap" FontWeight="SemiBold"/>
                </Grid>
            </StackPanel>
        </Border>

        <!-- ── Models ───────────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}"
                Visibility="{Binding HasOtherModels, Converter={StaticResource BoolVis}}">
            <StackPanel IsEnabled="{Binding MultiModelEnabled}">
                <TextBlock Text="Models" Style="{StaticResource SectionHeader}"/>
                <ScrollViewer MaxHeight="110" VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Models}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <CheckBox Content="{Binding Display}"
                                          IsChecked="{Binding IsSelected}"
                                          IsEnabled="{Binding CanUnselect}"
                                          Margin="0,2"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
                <TextBlock Text="Other models match the set and setups above by name. If one is missing there:"
                           TextWrapping="Wrap" Margin="0,8,0,4"
                           Foreground="{StaticResource Brush.TextMuted}"/>
                <StackPanel Orientation="Horizontal">
                    <RadioButton Content="Skip that model" IsChecked="{Binding SkipMissingSetups}"/>
                    <RadioButton Content="Copy the setup into it" IsChecked="{Binding CopyMissingSetups}"
                                 Margin="18,0,0,0"
                                 ToolTip="Creates the export setup in that model — this modifies and dirties it. A read-only model is skipped either way."/>
                </StackPanel>
            </StackPanel>
        </Border>
```

Replace the "Location" `<Border>` block (lines 121–130) with:

```xml
        <!-- ── Location ─────────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <StackPanel>
                <DockPanel>
                    <TextBlock Text="Location:  " VerticalAlignment="Center"/>
                    <Button Content="Browse…" DockPanel.Dock="Right" Padding="10,3"
                            Margin="8,0,0,0" Click="Browse_Click"/>
                    <TextBox Text="{Binding OutputFolder, UpdateSourceTrigger=PropertyChanged}"
                             VerticalAlignment="Center"/>
                </DockPanel>
                <CheckBox Content="Separate folder for PDFs" IsChecked="{Binding SeparatePdfFolder}"
                          Margin="0,8,0,0"/>
                <DockPanel Margin="0,6,0,0"
                           Visibility="{Binding SeparatePdfFolder, Converter={StaticResource BoolVis}}">
                    <TextBlock Text="PDFs:  " VerticalAlignment="Center"/>
                    <Button Content="Browse…" DockPanel.Dock="Right" Padding="10,3"
                            Margin="8,0,0,0" Click="BrowsePdf_Click"/>
                    <TextBox Text="{Binding PdfOutputFolder, UpdateSourceTrigger=PropertyChanged}"
                             VerticalAlignment="Center"/>
                </DockPanel>
            </StackPanel>
        </Border>
```

- [ ] **Step 4: Add the second Browse handler**

In `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml.cs`, replace `Browse_Click` with:

```csharp
        private void Browse_Click(object sender, RoutedEventArgs e)
            => _vm.OutputFolder = PickFolder("Choose the output folder", _vm.OutputFolder) ?? _vm.OutputFolder;

        private void BrowsePdf_Click(object sender, RoutedEventArgs e)
            => _vm.PdfOutputFolder = PickFolder("Choose the PDF output folder", _vm.PdfOutputFolder)
                                     ?? _vm.PdfOutputFolder;

        private static string? PickFolder(string description, string current)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description,
                SelectedPath = current,
            };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }
```

- [ ] **Step 5: Build both year configurations**

Run:

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: `Build succeeded`, 0 errors.

Then run:

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Confirm 2023 still builds (Core is shared with KKarea)**

Run:

```bash
dotnet build RVTuk.sln -c Release2023
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Run the whole Core suite**

Run:

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/RVTuk.UI/DwgExporter
git commit -m "feat(dwg-exporter): the dialog names sheets and views apart, spans models, splits folders"
```

---

### Task 11: Docs and the in-Revit checklist

**Files:**
- Modify: `docs/tools/dwg-exporter/README.md`
- Modify: `docs/tools/dwg-exporter/backlog.md`
- Modify: `CLAUDE.md:` the DWG Exporter bullet in the Features list

**Interfaces:**
- Consumes: the finished feature.
- Produces: documentation only.

- [ ] **Step 1: Update the README**

In `docs/tools/dwg-exporter/README.md`, replace the "**What it is:**" paragraph with:

```markdown
**What it is:** batch-exports a view/sheet set to DWG with filenames produced by the
naming rules of the document's saved **PDF** export setups (`ExportPDFSettings`) — so the
DWGs come out named exactly like the PDFs (e.g.
`<Project Number>-A-BLD_<Building Number>-<Sheet Number>.dwg`), instead of the native
exporter's `Sheet - A101 - ….dwg`. Sheets and non-sheet views take **separate naming
rules**, chosen per item, so a rule written for sheets is never applied to a view;
`<View Name>` (the default for views) uses the view's own name. Layers/lines/colors come
from a native DWG export setup (`ExportDWGSettings`) picked in the same window. Format
checkboxes let one run also (or only) produce **PDFs** — one per view, named by Revit's own
evaluation of the same rule, so `.dwg`/`.pdf` basenames pair exactly — optionally into
their own folder. When more than one model is open, a **Models** list lets the run span
them: each extra model matches the active model's set and setups **by name**, and a model
where one is missing is either skipped or has the setup created in it, your choice.
"Edit…" buttons hand off to the native dialogs (PDF Export for naming rules and view/sheet
sets, Modify DWG/DXF Export Setup for DWG setups) — the window closes, you edit, reopen,
and everything is re-read with your selections restored.
```

- [ ] **Step 2: Document the multi-model caveats in the README**

In the same file, insert before the "## Code" heading:

```markdown
## Multi-model runs

Export setups and view/sheet sets are per-document elements with no cross-document
identity, so an extra model resolves the active model's choices **by name**. Two misses
can't be rescued by copying and always skip, with the reason reported:

- **No view/sheet set of that name** — a `ViewSheetSet` references views that don't exist
  in the other document, so there is nothing to copy.
- **A read-only model** — no transaction is possible.

Copying a naming rule remaps its parameters: built-in ones carry over by their negative
id, shared and project parameters are looked up by name in the target. A rule parameter
with no counterpart there skips that model rather than creating a setup that would
silently produce blank fields.

Choosing **Copy the setup into it** writes into and dirties a model you did not open for
editing. That is intended — the copied setup then appears in that model's own Revit
dialogs — but it does mean the model needs saving afterwards.

All models write into the same destination, so a filename produced by two models aborts
the run before anything is written, naming both.
```

- [ ] **Step 3: Tick the backlog items**

In `docs/tools/dwg-exporter/backlog.md`, replace the "🚀 New features (requested 2026-08-03)" section with:

```markdown
## 🚀 New features (requested 2026-08-03)

- [x] **Different File Naming for different input** — shipped 2026-08-03: separate
  "Sheets" and "Views" naming dropdowns, applied per item, with `<View Name>` (the view's
  own name, the previous behaviour) as the views default. Also closes the 2026-07-16
  caveat where a non-sheet current view got a rule-based PDF name and a view-name DWG name.
- [x] **Export from two or more models** — shipped 2026-08-03: a Models list of the open
  models; extra models match the active model's set and setups by name, and a missing
  setup either skips that model or is created in it (a missing *set*, and a read-only
  model, always skip). Unticking the extras is the "don't export from other models" option.
- [x] **Hability to choose different paths for pdfs and dwgs files.** — shipped
  2026-08-03: a "Separate folder for PDFs" checkbox revealing a second Location row.

All three: [spec](specs/2026-08-03-multi-model-and-naming-design.md),
[plan](plans/2026-08-03-multi-model-and-naming.md).
```

- [ ] **Step 4: Add the in-Revit verification checklist**

In `docs/tools/dwg-exporter/backlog.md`, replace the "## To verify (in Revit, after deploy)" section with:

```markdown
## To verify (in Revit, after deploy)

- Run the manual checklist in [plans/2026-07-15-dwg-exporter.md](plans/2026-07-15-dwg-exporter.md)
  Task 6 — especially the naming-rule **separator placement** check against the native
  PDF export's filenames (fix lives in `FileNameComposer.Compose` if they differ).
- 2026-08-03 batch:
  - [ ] A set mixing sheets and views, two different naming setups picked — each file is
        named by the rule for its own kind.
  - [ ] Same run with PDF ticked — every `.dwg` has a `.pdf` of the same basename, for
        views as well as sheets.
  - [ ] Views left on `<View Name>` — filenames unchanged from before this batch.
  - [ ] A views rule made only of sheet parameters — files fall back to view names and the
        summary says so, instead of the run aborting on duplicates.
  - [ ] "Separate folder for PDFs" — DWGs and PDFs land in their two folders; unticking it
        puts both back in one.
  - [ ] A second model whose set and setups have the same names — both models export.
  - [ ] A second model missing a setup, "Skip that model" — it is skipped and the summary
        names the missing setup.
  - [ ] Same, "Copy the setup into it" — it exports, and the setup is afterwards present in
        that model's own PDF Export / DWG setup dialog with its rule intact.
  - [ ] A second model open read-only — skipped with the read-only reason, never attempted.
  - [ ] Two models producing the same filename — the run aborts before writing and names
        both models.
  - [x] "Current window" selected — the Models section is disabled.
```

- [ ] **Step 5: Update the CLAUDE.md feature bullet**

In `CLAUDE.md`, replace the `**DWG Exporter** (ribbon "DWG Export")` bullet with:

```markdown
- **DWG Exporter** (ribbon "DWG Export") — batch-exports a view/sheet set to DWG with filenames produced by the naming rules of the document's saved PDF export setups (so DWG names match the PDF export's names exactly), plus a native DWG export setup for layers/lines. Sheets and non-sheet views take separate naming rules, chosen per item. One run can span several open models (extra models match the active model's set and setups by name; a missing setup either skips that model or is created in it) and can write PDFs to their own folder. Modal dialog shown inside the command's API context — no external events. Revit 2024/25 only. See [`docs/tools/dwg-exporter/README.md`](docs/tools/dwg-exporter/README.md).
```

- [ ] **Step 6: Commit**

```bash
git add docs/tools/dwg-exporter CLAUDE.md
git commit -m "docs(dwg-exporter): two naming rules, multi-model runs, split folders"
```

---

## Deploy and verify

After Task 11, deploy and work through the 2026-08-03 checklist above.

```bash
powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoExit','-Command','cd D:\Coding\RVTuk; .\Deploy.ps1'"
```

Restart Revit before testing. Report any checklist item that fails against
`docs/tools/dwg-exporter/backlog.md` rather than marking the batch done.
