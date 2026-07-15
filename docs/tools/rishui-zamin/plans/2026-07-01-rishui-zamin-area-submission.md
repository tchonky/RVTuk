# Rishui Zamin Area Submission ("Area Calc") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A one-window Revit tool that reads Revit Areas on the open sheet and generates the Rishui Zamin `.dxf` + `.dat` submission files, with a pre-flight error check and a Setup command for the usage-code key schedule.

**Architecture:** Follows RVTuk's Core/UI/Revit split. Pure generation + validation logic lives in `RVTuk.Core` (unit-tested, no Revit/WPF). Revit area extraction, model selection, export, and Setup run on the main thread via `ExternalEvent`. A dark-themed WPF window (`RVTuk.UI`) drives it, receiving Revit behaviour as `Func`/`Action` delegates.

**Tech Stack:** C# multi-target (net48 / net8), WPF (MVVM), Revit API (Nice3point wrappers), xUnit.

## Global Constraints

- `RVTuk.Core` MUST NOT reference any Revit or WPF type. `RVTuk.UI` MUST NOT reference any Revit type; Revit behaviour is injected as `Func`/`Action`. Only `RVTuk.Revit` touches the Revit API.
- Build both configs: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024` (net48) and `-c Release2025` (net8). Core tests: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`.
- Revit API calls run only on the main thread via `ExternalEvent` + the existing ping-pong (see `IndexingExternalEventHandler` / `LoadFamilyEventHandler`). The tool is **read-only to the model** except `SetupRishuiZaminParams` (one Transaction).
- **DAT** is exactly `DWFX_SCALE<TAB>{value}<LF>` — a bare `\n`, no CRLF, no trailing content (sample = `DWFX_SCALE\t10\n`, 14 bytes).
- **DXF** is ASCII, group-code/value pairs, and MUST match the structure of `tests/Examples Autoarea/Garmoshka.dxf`. Layers `RZ_FRAME`/`RZ_FLOOR`/`RZ_AREA`; closed `LWPOLYLINE` (group 70 = 1); one `RZ_AREA_SYM` `INSERT` per area with `ATTRIB` children (group 2 = tag, group 1 = value). Tags: `USAGE_TYPE`, `USAGE_TYPE_OLD`, `AREA`, `ASSET`, `PAGE_NO`, `BUILDING_NO`, `FLOOR`, `LEVEL_ELEVATION`, `IS_UNDERGROUND`.
- Reference docs: spec `docs/superpowers/specs/2026-07-01-rishui-zamin-area-submission-design.md`; decoded formats + usage taxonomy `docs/autoarea/rishui-zamin-notes.md` (§3 taxonomy, §5 file formats); samples in `tests/Examples Autoarea/`.
- Commit after each task. Branch off `master` in the worktree.

---

### Task 1: UsageCatalog (Core)

**Files:**
- Create: `src/RVTuk.Core/AreaSubmission/UsageCatalog.cs`
- Test: `tests/RVTuk.Core.Tests/AreaSubmission/UsageCatalogTests.cs`

**Interfaces:**
- Produces: `enum UsageKind { Primary, Service, Other }`; `record UsageEntry(int Code, UsageKind Kind, string HebrewName)`; `static class UsageCatalog { IReadOnlyList<UsageEntry> All; UsageEntry? ByCode(int code); bool IsValidCode(int code); }`

- [ ] **Step 1: Write the failing test**
```csharp
using RVTuk.Core.AreaSubmission;
using Xunit;
public class UsageCatalogTests {
    [Fact] public void ByCode_ReturnsPrimary_For1() {
        var e = UsageCatalog.ByCode(1);
        Assert.NotNull(e); Assert.Equal(UsageKind.Primary, e!.Kind);
    }
    [Fact] public void Service101_IsService() => Assert.Equal(UsageKind.Service, UsageCatalog.ByCode(101)!.Kind);
    [Fact] public void UnknownCode_IsInvalid() => Assert.False(UsageCatalog.IsValidCode(9999));
}
```
- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj` → FAIL (type missing).
- [ ] **Step 3: Implement** — create the enum/record and `UsageCatalog` with the full entry list **transcribed from `docs/autoarea/rishui-zamin-notes.md` §3** (primary 1–33, service 101–130, other 250–257). Back it with a `List<UsageEntry>` and a `Dictionary<int,UsageEntry>` for `ByCode`. (Transcribe every row; do not summarise.)
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(area): usage-code catalog for Rishui Zamin"`.

---

### Task 2: Models + AreaError (Core)

**Files:**
- Create: `src/RVTuk.Core/AreaSubmission/AreaSubmissionConfig.cs`, `src/RVTuk.Core/AreaSubmission/AreaRecord.cs`
- Test: covered via Task 3/4 (no standalone test — pure data holders).

**Interfaces:**
- Produces:
  - `class AreaSubmissionConfig { int BuildingNo=1; string? Asset; int Scale=100; string OutputFolder=""; string FileBaseName=""; }`
  - `[Flags] enum AreaError { None=0, NoUsageCode=1, BadBoundary=2, ZeroArea=4 }`
  - `class Point2D { double X; double Y; }` (or reuse a tuple) — used for boundary loops.
  - `class AreaRecord { string Level=""; string? Number; string? Name; int? UsageCode; string Floor=""; int PageNo; bool IsUnderground; double AreaValue; List<List<Point2D>> BoundaryLoops = new(); AreaError Errors; }`

- [ ] **Step 1:** Create the three files exactly as above. No logic.
- [ ] **Step 2:** Build Core: `dotnet build src/RVTuk.Core/RVTuk.Core.csproj -c Release2025` → succeeds.
- [ ] **Step 3: Commit** — `git commit -m "feat(area): submission config + area record models"`.

---

### Task 3: DatWriter (Core)

**Files:**
- Create: `src/RVTuk.Core/AreaSubmission/DatWriter.cs`
- Test: `tests/RVTuk.Core.Tests/AreaSubmission/DatWriterTests.cs`

**Interfaces:**
- Produces: `static class DatWriter { byte[] Build(int scaleValue); }` where `scaleValue` is the DWFX_SCALE number (10 for 1:100 — see Open item; store the mapping in one place).

- [ ] **Step 1: Failing test**
```csharp
[Fact] public void Build_MatchesSampleBytes() {
    var bytes = DatWriter.Build(10);
    Assert.Equal(new byte[]{0x44,0x57,0x46,0x58,0x5F,0x53,0x43,0x41,0x4C,0x45,0x09,0x31,0x30,0x0A}, bytes);
}
```
- [ ] **Step 2: Verify fail.**
- [ ] **Step 3: Implement** — `Encoding.ASCII.GetBytes("DWFX_SCALE\t" + scaleValue + "\n")`.
- [ ] **Step 4: Verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(area): DAT writer (exact sample bytes)"`.

---

### Task 4: AreaValidator (Core)

**Files:**
- Create: `src/RVTuk.Core/AreaSubmission/AreaValidator.cs`
- Test: `tests/RVTuk.Core.Tests/AreaSubmission/AreaValidatorTests.cs`

**Interfaces:**
- Consumes: `AreaRecord`, `AreaSubmissionConfig`, `AreaError`.
- Produces: `record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`; `static class AreaValidator { AreaError CheckArea(AreaRecord a); IReadOnlyList<string> CheckConfig(AreaSubmissionConfig c); ValidationResult Validate(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig c); }`

- [ ] **Step 1: Failing tests** — per rule from the spec:
```csharp
[Fact] public void Area_NoUsageCode_IsError() {
    var a = new AreaRecord { UsageCode = null, AreaValue = 5, BoundaryLoops = { new(){ new(){X=0,Y=0}, new(){X=1,Y=0}, new(){X=1,Y=1} } } };
    Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.NoUsageCode));
}
[Fact] public void Area_ZeroArea_IsError() {
    var a = new AreaRecord { UsageCode = 1, AreaValue = 0 };
    Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.ZeroArea));
}
[Fact] public void Area_OpenLoop_IsBadBoundary() {
    var a = new AreaRecord { UsageCode = 1, AreaValue = 5, BoundaryLoops = { new(){ new(){X=0,Y=0} } } }; // <3 pts
    Assert.True(AreaValidator.CheckArea(a).HasFlag(AreaError.BadBoundary));
}
[Fact] public void Config_MissingOutputFolder_IsError() {
    var errs = AreaValidator.CheckConfig(new AreaSubmissionConfig { OutputFolder = "" });
    Assert.Contains(errs, e => e.Contains("output", System.StringComparison.OrdinalIgnoreCase));
}
```
- [ ] **Step 2: Verify fail.**
- [ ] **Step 3: Implement** — `CheckArea`: NoUsageCode if `UsageCode == null` or not `UsageCatalog.IsValidCode`; ZeroArea if `AreaValue <= 0`; BadBoundary if no loop has ≥3 points. `CheckConfig`: require `OutputFolder` non-empty, `FileBaseName` non-empty, `Scale > 0`, `BuildingNo >= 1`. `Validate`: aggregate area errors (with area Number/Name in the message) + config errors; warnings for areas missing Number or Name.
- [ ] **Step 4: Verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(area): validator (missing code / bad boundary / config)"`.

---

### Task 5: DxfWriter (Core) — golden-reference test

**Files:**
- Create: `src/RVTuk.Core/AreaSubmission/DxfWriter.cs`
- Test: `tests/RVTuk.Core.Tests/AreaSubmission/DxfWriterTests.cs`

**Interfaces:**
- Consumes: `AreaRecord`, `AreaSubmissionConfig`.
- Produces: `static class DxfWriter { string Build(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig config); }` (ASCII DXF text).

- [ ] **Step 1: Capture the golden reference.** Read `tests/Examples Autoarea/Garmoshka.dxf` and extract, verbatim, (a) the minimal HEADER/TABLES/BLOCKS boilerplate needed for a valid file with the `RZ_AREA_SYM`/`RZ_FRAME_SYM`/`RZ_FLOOR_SYM` block definitions, and (b) one complete `ENTITIES` unit for a single area: its `LWPOLYLINE` on `RZ_AREA` and its `RZ_AREA_SYM` `INSERT` + all `ATTRIB`s. Save that one-area expected text as a test fixture `tests/RVTuk.Core.Tests/AreaSubmission/Fixtures/one_area.dxf`. **This fixture is the spec for byte formatting.**
- [ ] **Step 2: Write the failing test** — build one `AreaRecord` matching the fixture's values (same loop points, usage code, floor, page, etc.) and assert `DxfWriter.Build([area], config)` contains the fixture's `ENTITIES` unit (normalise line endings). Also assert the DAT-independent invariants: `LWPOLYLINE` has `70`/`1` and `90`/`{pointCount}`; each tag appears as `2`/`{TAG}` then `1`/`{value}`.
- [ ] **Step 3: Verify fail.**
- [ ] **Step 4: Implement** `DxfWriter.Build`: emit the fixed HEADER/TABLES/BLOCKS preamble (constant string mirroring Garmoshka), then per floor emit the `RZ_FRAME`/`RZ_FLOOR` entities (see Open items — frame = bounding rectangle of the floor's areas; floor outline = union or bounding loop), then per area a closed `LWPOLYLINE` on `RZ_AREA` and an `RZ_AREA_SYM` `INSERT` (insertion point inside the polygon — use the first loop's centroid) with the nine `ATTRIB`s (values from the `AreaRecord`/config; `USAGE_TYPE_OLD` per Open item), then the closing `ENDSEC`/`EOF`. Coordinate units per Open item (match fixture magnitudes).
- [ ] **Step 5: Verify pass** and eyeball the produced DXF opens in a DXF viewer if available.
- [ ] **Step 6: Commit** — `git commit -m "feat(area): DXF writer matching Garmoshka reference"`.

---

### Task 6: AreaExtractor (Revit)

**Files:**
- Create: `src/RVTuk.Revit/AreaSubmission/AreaExtractor.cs`

**Interfaces:**
- Consumes: `Autodesk.Revit.DB.Document`, an active `ViewSheet`.
- Produces: `class AreaExtractor { IReadOnlyList<AreaRecord> FromOpenSheet(UIDocument uidoc); IReadOnlyList<(long ElementId, AreaRecord Record)> ... }` — return records **and** a parallel map from `AreaRecord` to its Revit `ElementId` (for selection). Suggest `record ExtractedArea(long ElementId, AreaRecord Record)` and `IReadOnlyList<ExtractedArea> FromOpenSheet(UIDocument uidoc)`.

- [ ] **Step 1:** Implement: from `uidoc.ActiveView` if it's a `ViewSheet`, get placed views (`Viewport` → `ViewId`) that are `ViewPlan` with `ViewType.AreaPlan`; for each, `FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType()`. For each `Area`: read `Area.Area` (convert ft² → m²), `Area.Level.Name` (→ Floor), `Number`/`Name` params, `RZ_UsageType` (LookupParameter) → `UsageCode`, `IsUnderground` from level elevation `< 0` (Open item), `PageNo` from the sheet (Open item: sheet number or a running index), and boundary via `Area.GetBoundarySegments(new SpatialElementBoundaryOptions())` → each loop's `Curve.GetEndPoint(0)` XY into `List<Point2D>`. Populate `AreaError` via `AreaValidator.CheckArea`.
- [ ] **Step 2:** Build `RVTuk.Revit` both configs → succeed.
- [ ] **Step 3: Commit** — `git commit -m "feat(area): extract Areas from the open sheet"`. *(Behaviour verified in Revit in Task 11.)*

---

### Task 7: ExternalEvent handlers + Application wiring (Revit)

**Files:**
- Create: `src/RVTuk.Revit/ExternalEvents/AreaExtractEventHandler.cs`, `src/RVTuk.Revit/ExternalEvents/SelectAreaEventHandler.cs`
- Modify: `src/RVTuk.Revit/Application.cs` (register the two handlers + `ExternalEvent`s, following the existing `IndexingHandler`/`IndexingEvent` pattern).

**Interfaces:**
- `AreaExtractEventHandler` — `Prepare(UIDocument)`, runs `AreaExtractor.FromOpenSheet`, exposes `Result : IReadOnlyList<ExtractedArea>` + `WaitForCompletion()` (mirror `GetProjectFamiliesEventHandler`).
- `SelectAreaEventHandler` — `Prepare(long elementId)`, sets `uidoc.Selection.SetElementIds`.

- [ ] **Step 1:** Implement both handlers mirroring existing handlers; add static `AreaExtractHandler/Event`, `SelectAreaHandler/Event` to `Application`.
- [ ] **Step 2:** Build both configs.
- [ ] **Step 3: Commit** — `git commit -m "feat(area): external-event handlers for extract + select"`.

---

### Task 8: Export orchestration (Revit)

**Files:**
- Create: `src/RVTuk.Revit/AreaSubmission/AreaSubmissionExporter.cs`

**Interfaces:**
- Produces: `static class AreaSubmissionExporter { (bool ok, string message) Export(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig cfg); }` — runs `AreaValidator.Validate`; if errors, return `(false, joined errors)`; else write `Path.Combine(cfg.OutputFolder, cfg.FileBaseName + ".dxf")` (`DxfWriter.Build`) and `+ ".dat"` (`DatWriter.Build`, scale-value from `cfg.Scale`), return `(true, "...")`. Catch IO exceptions → `(false, ex.Message)`.

- [ ] **Step 1:** Implement. (Pure enough to unit-test optionally with a temp folder.)
- [ ] **Step 2:** Build both configs.
- [ ] **Step 3: Commit** — `git commit -m "feat(area): export DXF+DAT with validation"`.

---

### Task 9: Setup command + shared parameter file (Revit)

**Files:**
- Create: `src/RVTuk.Revit/AreaSubmission/SetupRishuiZaminParamsCommand.cs`, `src/RVTuk.Revit/Resources/RZ_AreaParams.txt`
- Modify: `Deploy.ps1` if the shared-param file must be copied to the add-in folder.

**Interfaces:**
- `IExternalCommand` that, in one Transaction: binds `RZ_UsageType` (from `RZ_AreaParams.txt`) to `OST_Areas`, then creates an Area **key schedule** (`ViewSchedule.CreateKeySchedule` for `OST_Areas`) with one key row per `UsageCatalog.All` entry, setting `RZ_UsageType` = code and the key name = Hebrew name.

- [ ] **Step 1:** Author `RZ_AreaParams.txt` (a valid Revit shared-parameter file defining `RZ_UsageType`, an Integer). 
- [ ] **Step 2:** Implement the command (bind param via `Document.ParameterBindings`; build the key schedule; fill rows from `UsageCatalog`).
- [ ] **Step 3:** Build both configs.
- [ ] **Step 4: Commit** — `git commit -m "feat(area): setup command binds RZ_UsageType + key schedule"`.

---

### Task 10: AreaSubmissionViewModel (UI)

**Files:**
- Create: `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs`, `.../ViewModels/AreaLevelGroupViewModel.cs`, `.../ViewModels/AreaRowViewModel.cs`

**Interfaces:**
- Constructor injects delegates (no Revit types): `Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract`, `Action<long> selectInModel`, `Func<AreaSubmissionConfig,(bool ok,string msg)> export`.
- Exposes: `AreaSubmissionConfig Config`; `enum Pane { Config, Areas }` + `Pane CurrentPane`; commands `ShowConfigCommand`, `RefreshCommand`, `ExportCommand`; `ObservableCollection<AreaLevelGroupViewModel> Levels`; `SelectedRow` → invokes `selectInModel`.
- `RefreshCommand` calls `extract`, groups rows by `Level` (sorted), each group's rows sorted by `Number`; sets `HasError` per row from `AreaRecord.Errors`; switches `CurrentPane = Areas`.
- `ExportCommand` calls `export(Config)` and raises a result event the window shows as a dialog.

- [ ] **Step 1:** Implement the three VMs (RelayCommand pattern already in the project).
- [ ] **Step 2:** Build `RVTuk.UI` both configs.
- [ ] **Step 3: Commit** — `git commit -m "feat(area): submission view models"`.

---

### Task 11: AreaSubmissionWindow (UI) + ribbon button + in-Revit verification

**Files:**
- Create: `src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml` (+ `.cs`)
- Create: `src/RVTuk.Revit/Commands/AreaCalcCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs` (ribbon button "Area Calc" + icon; wire the delegates to the ExternalEvents like `BrowseLibraryCommand` does)

**Interfaces:** `AreaCalcCommand` builds the three delegates (extract via `AreaExtractEventHandler` ping-pong; select via `SelectAreaEventHandler`; export via `AreaSubmissionExporter`) and opens `AreaSubmissionWindow` (single-instance like `ConfigWindow`).

- [ ] **Step 1:** XAML: dark theme; top toolbar (Config / Refresh / Export); bottom `ContentControl` swapping between a Config form (bound to `Config` fields) and a `TreeView` of `Levels` → rows, error rows styled red / with an icon, row-select bound to `SelectedRow`.
- [ ] **Step 2:** `AreaCalcCommand` + ribbon button + `Application` static handlers wiring.
- [ ] **Step 3:** Build both configs.
- [ ] **Step 4: In-Revit verification** — `.\Deploy.ps1`; run Setup once (key schedule appears); assign codes to Areas; open a sheet; **Area Calc** → Config fill → Refresh (areas grouped by level, errors flagged, click selects in model) → Export (writes `.dxf`+`.dat`; error dialog when a code is missing). Confirm the `.dxf`+`.dat` are accepted by the robot.
- [ ] **Step 5: Commit** — `git commit -m "feat(area): submission window + Area Calc ribbon button"`.

---

## Self-review

- **Spec coverage:** ribbon (T11), Core `UsageCatalog`/models/`AreaValidator`/`DxfWriter`/`DatWriter` (T1–5), Revit `AreaExtractor`/handlers/exporter/Setup (T6–9), UI window+VMs (T10–11), pre-flight errors (T4 + T10 flags + T11 dialog), no-georef/single-building (baked into models T2 + writer T5). Covered.
- **Placeholders:** the DXF byte format and the `Open items` (units, `RZ_FRAME`/`RZ_FLOOR` derivation, `IS_UNDERGROUND`, `USAGE_TYPE_OLD`, `DWFX_SCALE` value) are resolved against the golden `Garmoshka.dxf` fixture in T5/T6 — captured from the real sample, not invented.
- **Type consistency:** `AreaRecord`, `AreaSubmissionConfig`, `AreaError`, `ExtractedArea`, `UsageEntry` names are used consistently across tasks.
