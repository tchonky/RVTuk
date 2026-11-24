# Area Calc: area table and plot limits — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A **Table** tab in the Area Calc window: floors × used usage codes with group sums, % of plot, above/below/building rows, and a strip checking Primary / Service / Total against the plan's allowed % of plot. The plot area and limits are kept in the Revit project.

**Architecture:** The table and the limit rules are pure Core code (`AreaTable`, `PlotLimits`, `PlotLimitCheck`) with xunit tests on three runtimes. The Revit side adds an Extensible Storage store on Project Information: it is read by the existing extract handler and written by a new handler. Both are linked into KKarea. The view model gets two optional delegates and the window a new `AreaTableView` user control.

**Tech Stack:** C# (net48 / net8.0-windows / net10.0-windows), WPF, Revit API 2023–2027, Extensible Storage, xunit.

**Spec:** [`docs/tools/rishui-zamin/specs/2026-11-23-area-table-design.md`](../specs/2026-11-23-area-table-design.md). Read it alongside this plan. §-numbers below refer to it.

**Branch:** `rishui-zamin` in `D:\Coding\RVTuk`. No worktrees, no other branches.

## Global Constraints

- Layers: Core has no Revit/WPF types; UI has no Revit types; Revit work reaches the UI only as delegates. Namespace = root namespace + folder path.
- Revit API only on Revit's main thread: `ExternalEvent` + `ManualResetEventSlim`, called from the thread pool by the view model.
- Every linked Revit source must build under `Release2023` (KKarea) as well as 2024–2027.
- Effective code = `UsageGroups.Of`'s rule: the New code if it is in `UsageCatalog`, else the Old code if it is, else none. Table, floor totals and sheet totals must agree.
- **Total = Primary + Service.** Other and Removed (300–302) count toward nothing limited. No code shows in its own column.
- **Limits are checked against the Building row** (above + below ground). Status: Ok when used < 0.95 × allowed; Close when 0.95 × allowed ≤ used ≤ allowed; Over when used > allowed. Compare unrounded values. An empty limit is NotChecked.
- Numbers: m² `"F2"`, % `"F1"`, both `CultureInfo.InvariantCulture`. A zero m² cell is empty.
- UI no `record`s (net48 RVTuk.UI has no `IsExternalInit`); Core may use them.
- Hebrew next to numbers goes in separate `TextBlock`s (bidi). Test text with mixed samples (`"קומת קרקע Ground"`), never Hebrew-only.
- Colours: Primary `Brush.UsagePrimary`, Service `Brush.UsageService`, Other `Brush.UsageOther`, Removed and Close `Brush.UsageProcess`, No code and Over `Brush.Warning`, Ok `Brush.UsagePrimary`, NotChecked `Brush.TextMuted`.
- Before every commit touching Core or UI: `.\build\Test-All.ps1`. Before every commit touching the Revit side: `dotnet build RVTuk.sln -c Release2023` and `-c Release2024` (plus `-c Release2027` in Task 3).

## Review Focus

1. **A decimal comma** ("35,5"): .NET's `AllowThousands` reads it as 355. A comma is accepted only as a thousands separator in groups of three; otherwise the box is invalid. (Task 2 test `TryParse_RejectsCommaAsDecimal`.)
2. **Undo in Revit after a save**, then Refresh: the stored value (now the old one) must replace the box when nothing is unsaved. (Task 4 test `Refresh_SameDocument_Saved_TakesStoredValues`.)
3. **Refresh while on the Table tab** (button, or the re-read after setting a usage) must not jump to the Areas tab. (Task 4 test `Refresh_FromTableTab_StaysOnTable`.)
4. **An area whose level has no name / no level** (`Floor == ""`) gets a row labelled `(no level)`, not a blank row. (Task 1 test `Rows_EmptyFloorName_ShowsNoLevel`.)
5. **Project Information borrowed by someone else in a workshared model:** the save fails with a message naming why; the typed value stays, marked unsaved. (Task 3 manual check 3, Task 4 test `FailedSave_SurvivesRefreshOfSameDocument`.)

---

### Task 1: Core — effective code and the area table

**Files:**
- Modify: `src/RVTuk.Core/RishuiZamin/UsageTotals.cs` (add `EffectiveCode`; `Of` uses it)
- Create: `src/RVTuk.Core/RishuiZamin/AreaTable.cs`
- Test: `tests/RVTuk.Core.Tests/RishuiZamin/AreaTableTests.cs`, add to `UsageTotalsTests.cs`

**Interfaces:**
- Produces (namespace `RVTuk.Core.RishuiZamin`):
  - `UsageGroups.EffectiveCode(AreaRecord a) -> int?`
  - `enum AreaTableColumnKind { Code, Sum, Percent }`
  - `sealed record AreaTableColumn(UsageGroup Group, AreaTableColumnKind Kind, int? Code)` (`Code` set only for `Kind == Code`)
  - `enum AreaTableRowKind { Floor, AboveGround, BelowGround, Building }`
  - `sealed class AreaTableRow { AreaTableRowKind Kind; string Label; double? Elevation; bool IsUnderground; int AreaCount; IReadOnlyDictionary<int,double> ByCode; UsageTotals Totals; }`
  - `sealed class AreaTable { IReadOnlyList<AreaTableColumn> Columns; IReadOnlyList<AreaTableRow> Rows; static AreaTable Build(IEnumerable<AreaRecord> areas); IReadOnlyList<string> Format(AreaTableRow row, double? plotAreaM2); static readonly AreaTable Empty; }`

- [ ] **Step 1: Write the failing tests**

`UsageTotalsTests`:
- `EffectiveCode_NewElseOldElseNull`: (1,105)→1; (999,30)→30; (null,105)→105; (3,null)→null; (null,null)→null.

`AreaTableTests` (helper `Area(string floor, double elev, bool under, int? code, double m2, int? old = null)`):
- `Columns_UsedCodesInCatalogOrder_WithSumsAndPercents`: codes 105, 1, 104, 252, 300 → `[P Code 1, P Sum, P Percent, S Code 104, S Code 105, S Sum, S Percent, O Code 252, O Sum, Process Code 300]`.
- `Columns_EmptyGroup_KeepsSumAndPercent`: only code 105 → Primary has `Sum, Percent` and no code column; Other has `Sum` only; no Process columns.
- `Columns_NoCodeColumn_OnlyWhenSomeAreaHasNone`: with an uncoded area there is `(NoCode, Sum, null)` between Other's Sum and the Process columns; without, there is none.
- `Columns_UseEffectiveCode`: New 999, Old 30 → a `(Primary, Code, 30)` column, no 999.
- `Rows_FloorsByElevationAscending_ThenSummaries`: floors at 4.00, -4.90, 0.00 → labels in order -4.90, 0.00, 4.00, then kinds `AboveGround, BelowGround, Building`.
- `Rows_SummariesSplitByUnderground`: basement (under) service 24.40, ground primary 100, first floor service 50 → AboveGround Totals (100, 50), BelowGround (0, 24.40), Building (100, 74.40); AreaCount 2/1/3.
- `Building_MatchesUsageTotalsSum`: Building row `Totals` equals `UsageTotals.Sum` of the same areas (record equality), with Other, Process and uncoded areas in the mix.
- `Rows_EmptyFloorName_ShowsNoLevel`: `Floor = ""` → label `"(no level)"`.
- `Format_M2TwoDecimals_EmptyWhenZero`: a floor with 1437.94 under code 12 and nothing under 104 → cell for 12 is `"1437.94"`, cell for 104 is `""`.
- `Format_Percent_OneDecimal_DashWithoutPlot`: Primary Sum 760.77, plot 2370 → Percent cell `"32.1"`; plot `null` or `0` → `"—"`.
- `Format_EmptySummaryRow_IsDashes`: no underground areas → every cell of BelowGround is `"—"`.
- `Format_LengthMatchesColumns`: `Format(row, …).Count == Columns.Count` for every row.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter "FullyQualifiedName~AreaTable|FullyQualifiedName~UsageTotalsTests"`
Expected: build errors (`AreaTable`, `EffectiveCode` not defined).

- [ ] **Step 3: Implement**

`EffectiveCode` in `UsageTotals.cs`; `Of(a) => OfCode(EffectiveCode(a))`. `AreaTable.Build`:
- Groups floors by `Floor` (as `RishuiZaminViewModel.Populate` does), ordered by the first area's `LevelElevation`. A floor's `Elevation` and `IsUnderground` come from its first area. Summary rows are sums of the floor rows. `ByCode` is keyed by effective code; uncoded areas go only into `Totals.NoCode`.
- Column order: Primary codes, Sum, Percent; Service codes, Sum, Percent; Other codes, Sum; NoCode Sum (only if any); Process codes. Code order follows `UsageCatalog.All` order.
- `Format`: Code → `ByCode` m²; Sum → that group's `Totals` field (NoCode → `Totals.NoCode`); Percent → group m² ÷ plot × 100, or `"—"` when the plot is null or ≤ 0. Summary rows with `AreaCount == 0` → all `"—"`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the command from Step 2.
Expected: all pass; existing `UsageTotalsTests` still pass.

- [ ] **Step 5: Run `.\build\Test-All.ps1`, then commit**

Expected: all green on net48, net8 and net10.
`git commit -m "feat(rishui-zamin): area table model — floors × used codes, group sums, % of plot"`

---

### Task 2: Core — plot limits, parsing and the limit check

**Files:**
- Create: `src/RVTuk.Core/RishuiZamin/PlotLimits.cs`
- Test: `tests/RVTuk.Core.Tests/RishuiZamin/PlotLimitsTests.cs`

**Interfaces:**
- Consumes: `AreaTable`, `AreaTableRowKind.Building` (Task 1).
- Produces:
  - `sealed record PlotLimits(double? PlotAreaM2, double? PrimaryPercent, double? ServicePercent, double? TotalPercent) { static readonly PlotLimits Empty; static bool TryParseValue(string? text, out double? value); }`
  - `enum LimitStatus { NotChecked, Ok, Close, Over }`
  - `sealed record PlotLimitChip(string Text, LimitStatus Status)`
  - `static class PlotLimitCheck { static double? UsedPercent(double m2, double? plotAreaM2); static LimitStatus Status(double? usedPercent, double? allowedPercent); static IReadOnlyList<PlotLimitChip> Chips(AreaTable table, PlotLimits limits); const string NoPlotHint = "Type the plot area to see % of plot"; }`

- [ ] **Step 1: Write the failing tests**

- `TryParse_EmptyIsNull`: `""`, `"  "`, `null` → true, `null`.
- `TryParse_AcceptsThousandsAndPercentSign`: `"2,370.00"` → 2370; `"2370"` → 2370; `"35%"` → 35; `" 35.5 "` → 35.5.
- `TryParse_RejectsCommaAsDecimal`: `"35,5"`, `"1,23"`, `"1,2345"` → false.
- `TryParse_RejectsNegativeAndJunk`: `"-1"`, `"abc"`, `"35%%"` → false.
- `UsedPercent_NullWithoutPositivePlot`: plot null / 0 / -5 → null; (760.77, 2370) → ≈ 32.0999 (precision 3).
- `Status_Thresholds`: allowed 35: used 30 → Ok; 33.25 → Close; 35 → Close; 35.01 → Over. Allowed null → NotChecked. Used null → NotChecked.
- `Chips_TextForms`: plot 2370, Primary 760.77 with limit 35 → `"Primary 32.1% / 35%"`; Service 388.68 m² with no limit → `"Service 16.4%"` (status NotChecked); no plot → `"Total —"`. Always three chips, order Primary, Service, Total.
- `Chips_UseBuildingRow_BasementCounts`: a basement-only service area of 474 m², plot 2370, Service limit 20 → Service chip shows `20.0%` and status Close; with 480 m² → Over.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter FullyQualifiedName~PlotLimits`
Expected: build errors.

- [ ] **Step 3: Implement** in `PlotLimits.cs`. `TryParseValue`: trim, strip one trailing `%`. If the text contains a comma it must match `^\d{1,3}(,\d{3})+(\.\d+)?$`. Parse with `NumberStyles.Float | NumberStyles.AllowThousands`, invariant, and reject < 0. Chip limit text uses `"0.##"`.

- [ ] **Step 4: Run the tests to verify they pass** (Step 2 command). Expected: all pass.

- [ ] **Step 5: `.\build\Test-All.ps1`, then commit** `feat(rishui-zamin): plot limits — parsing, % of plot and the close/over check`

---

### Task 3: Revit — store the plot limits on Project Information

**Files:**
- Create: `src/RVTuk.Revit/RishuiZamin/PlotLimitsStore.cs`
- Create: `src/RVTuk.Revit/RishuiZamin/ExternalEvents/SavePlotLimitsEventHandler.cs`
- Modify: `src/RVTuk.Revit/RishuiZamin/ExternalEvents/AreaExtractEventHandler.cs`
- Modify: `src/RVTuk.Revit/Application.cs`, `src/KKarea.Revit/Application.cs` (register handler + event)
- Modify: `src/KKarea.Revit/KKarea.Revit.csproj` (link the two new files under `Shared\`)
- Modify: both `RishuiZaminCommand.cs` (RVTuk.Revit and KKarea.Revit): two new delegates

**Interfaces:**
- Consumes: `PlotLimits` (Task 2).
- Produces:
  - `static class PlotLimitsStore { static PlotLimits Read(Document doc); static void Write(Document doc, PlotLimits limits); }`. Write must be called inside a transaction. Schema GUID `e6d292f7-e823-45c0-a5a6-b95108884f31`, name `RVTukRishuiZaminPlotLimits`, vendor `KnafoKlimor`, public read/write. Four `double` fields `PlotAreaM2`, `PrimaryPercent`, `ServicePercent`, `TotalPercent`, spec `SpecTypeId.Number`, read and written with `UnitTypeId.General`. A negative value means empty (write −1 for null). An element with no entity reads as `PlotLimits.Empty`.
  - `AreaExtractEventHandler`: new `PlotLimits PlotLimits` and `string DocumentKey` (`doc.PathName` when not empty, else `doc.Title`). Both are set in `Execute`. A failed `Read` gives `PlotLimits.Empty`; it never fails the extraction.
  - `SavePlotLimitsEventHandler { void Prepare(PlotLimits limits, Document? readFrom); void WaitForCompletion(); (bool ok, string msg) Result; }`. Same shape as `SetUsageKeyEventHandler`.
  - View-model delegates (wired in both commands): `Func<(string DocumentKey, PlotLimits Limits)> readPlotLimits` returns the extract handler's two fields, with no extra event. `Func<PlotLimits, (bool ok, string msg)> savePlotLimits` does Prepare → Raise → Wait → Result, passing `Application.AreaExtractHandler.Document`.

- [ ] **Step 1: Implement the store and both handlers.** The save handler's refusals, each returning `(false, message)` with nothing written:
  - `readFrom` null or not the active document → "The active project is not the one the window last read. Click Refresh and try again."
  - `doc.IsReadOnly` → "The project is read-only."
  - Workshared and `WorksharingUtils.GetCheckoutStatus(doc, doc.ProjectInformation.Id) == CheckoutStatus.OwnedByOtherUser` → "Project Information is being edited by " + owner + ". Ask them to sync, then try again." (owner from `WorksharingUtils.GetWorksharingTooltipInfo(...).Owner`).
  - Otherwise a transaction "Area Calc — plot area and limits". If `Commit()` is not `Committed`, return failure. On success return `(true, "")`.

- [ ] **Step 2: Wire it.** Register `SavePlotLimitsHandler` / `SavePlotLimitsEvent` in both `Application.cs`, next to `SetUsageKeyHandler`. Link both new files in `KKarea.Revit.csproj`. Pass the two delegates to `RishuiZaminViewModel` in both commands (constructor order from Task 4).

- [ ] **Step 3: Build**

Run: `dotnet build RVTuk.sln -c Release2023`, `-c Release2024`, `-c Release2027`
Expected: 0 errors in each. (This step runs after Task 4's constructor change if the delegates do not compile yet; then commit Tasks 3 and 4 together.)

- [ ] **Step 4: In-Revit checks (Daniel, after deploy) — add them to `backlog.md` now:**
  1. Type plot area and limits, close and reopen the project → the values come back.
  2. Each commit is one Ctrl+Z, named "Area Calc — plot area and limits".
  3. Workshared, with Project Information borrowed by another user → the message names them, the value stays marked unsaved.
  4. Two projects open: switch, Refresh → the other project's values (or empty) show.
  5. KKarea (2023): checks 1 and 2.

- [ ] **Step 5: Commit** `feat(rishui-zamin): keep plot area and limits on Project Information`

---

### Task 4: UI view model — the table, the boxes and saving

**Files:**
- Modify: `src/RVTuk.UI/RishuiZamin/ViewModels/RishuiZaminViewModel.cs`
- Create: `src/RVTuk.UI/RishuiZamin/ViewModels/PlotLimitBoxViewModel.cs`
- Test: `tests/RVTuk.UI.Tests/RishuiZamin/RishuiZaminViewModelTests.cs` (extend `Stubs`)

**Interfaces:**
- Consumes: `AreaTable`, `PlotLimits`, `PlotLimitCheck`, `PlotLimitChip` (Tasks 1–2).
- Produces:
  - Constructor gains two trailing optional parameters: `Func<(string DocumentKey, PlotLimits Limits)>? readPlotLimits = null, Func<PlotLimits, (bool ok, string msg)>? savePlotLimits = null`.
  - `enum SubmissionPane { Areas, Table, Config }` (the tab order). `Refresh` switches to `Areas` only when the current pane is `Config`.
  - `AreaTable Table` (rebuilt in `Populate`); `double? PlotAreaM2` (the parsed plot box, even when unsaved).
  - `PlotLimitBoxViewModel PlotAreaBox, PrimaryLimitBox, ServiceLimitBox, TotalLimitBox`. Each box is `: ViewModelBase { string Text; bool IsInvalid; double? Value; }`. `IsInvalid` is updated when `Text` changes, through `PlotLimits.TryParseValue`.
  - `IReadOnlyList<PlotLimitChip> LimitChips`, `string? PlotHint` (`PlotLimitCheck.NoPlotHint` when there is no plot area), `string? TableStatusText`, `bool LimitsUnsaved`.
  - `void CommitPlotLimits()`, called by the view on Enter or when a box loses focus.
  - `event Action? TableChanged`, raised after `Table`, the plot area or the chips change, so the view rebuilds its grid.

- [ ] **Step 1: Write the failing tests** (extend `Stubs` with `DocKey = "A.rvt"`, `Stored = PlotLimits.Empty`, `SaveResult = (true, "")`, `List<PlotLimits> Saved`, and pass both delegates):
- `Populate_BuildsTable`: with `Five`, `Table.Rows` has the two floors plus three summary rows.
- `Refresh_LoadsStoredLimitsIntoBoxes`: `Stored = (2370, 35, 20, 55)` → after Refresh, the boxes read `"2370"`, `"35"`, `"20"`, `"55"`, and there are three chips.
- `CommitLimits_Changed_SavesOnce_AndSaysSo`: type 2370 in the plot box, commit → `Saved` has one entry with `PlotAreaM2 == 2370`; `TableStatusText` is `"Plot area and limits saved in the project · one undo step in Revit"`; `LimitsUnsaved` is false.
- `CommitLimits_Unchanged_DoesNotSave`: Refresh loads `Stored`, commit with no edit → no save; edit, commit, commit again → exactly one save.
- `CommitLimits_Invalid_MarksBox_DoesNotSave`: `"35,5"` in Primary → `PrimaryLimitBox.IsInvalid`, nothing saved.
- `CommitLimits_RecomputesChips`: after commit, the Primary chip text matches `PlotLimitCheck.Chips` for the typed values.
- `FailedSave_SurvivesRefreshOfSameDocument`: `SaveResult = (false, "borrowed")` → `LimitsUnsaved`, `TableStatusText` = `"Not saved to the project: borrowed"`; Refresh with the same key and other stored values → the box still shows the typed value.
- `Refresh_OtherDocument_ReplacesUnsavedValues`: after a failed save, set `DocKey = "B.rvt"` and Refresh → the boxes show B's stored values, and `LimitsUnsaved` is false.
- `Refresh_SameDocument_Saved_TakesStoredValues`: save OK, change `Stored` (as after an Undo in Revit), Refresh → the boxes show the new stored values.
- `Refresh_FromTableTab_StaysOnTable`: `CurrentPane = Table`, Refresh → still `Table`. From `Config` → `Areas`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter FullyQualifiedName~RishuiZaminViewModelTests`
Expected: build errors.

- [ ] **Step 3: Implement.**
  - In `Refresh`'s worker, call `readPlotLimits` right after `_extract` and pass the result to the dispatcher along with the areas.
  - Applying a read: if the key differs from the last key, or nothing is unsaved, set all four boxes from the stored values and clear `LimitsUnsaved`. Otherwise keep the boxes.
  - Saving runs on the thread pool and is part of `IsBusy`. On success, remember the saved `PlotLimits` as the last-known values.
  - Format box values with `"0.##"` invariant.

- [ ] **Step 4: Run the tests to verify they pass** (Step 2 command). Expected: all pass, including the earlier view-model tests.

- [ ] **Step 5: `.\build\Test-All.ps1`; build `-c Release2023` and `-c Release2024`; then commit** `feat(rishui-zamin): area table and plot limits in the Area Calc view model` (together with Task 3 if it waited on this constructor).

---

### Task 5: UI view — the Table tab

**Files:**
- Create: `src/RVTuk.UI/RishuiZamin/Views/AreaTableView.xaml` / `.xaml.cs` (a `UserControl`; DataContext is the window's view model; merges `DarkThemeDictionary` like the other views)
- Create: `src/RVTuk.UI/RishuiZamin/Views/LimitStatusBrushConverter.cs`
- Modify: `src/RVTuk.UI/RishuiZamin/Views/RishuiZaminWindow.xaml` (a "Table" `TabItem` between Areas and Settings, holding `AreaTableView`)
- Modify: `tests/RVTuk.UI.Tests/Shared/ViewSmokeTests.cs` (expected count 11 → 12)

**Interfaces:**
- Consumes: Task 4's members.

- [ ] **Step 1: Update the smoke test's count to 12 and run it to see it fail**

Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter FullyQualifiedName~ViewSmokeTests`
Expected: FAIL, 11 ≠ 12.

- [ ] **Step 2: Build the view** (§4 sketch):
  - Top row: a Refresh button bound to `RefreshCommand`, then the four boxes with labels (plot area in m², three limits in %). Each box gets a red border when `IsInvalid`. Enter and LostFocus call `CommitPlotLimits()`.
  - The chip strip comes from `LimitChips`, coloured through `LimitStatusBrushConverter` (see Global Constraints). `PlotHint` and `TableStatusText` show under it.
  - The grid is a `DataGrid` (read-only, no auto columns, `FrozenColumnCount = 3`), rebuilt in code-behind on `TableChanged`. The first columns are elevation (`"F2"`), below ground (`"yes"` or empty) and floor label. Then one column per `Table.Columns` entry, taking its text from `Table.Format(row, PlotAreaM2)`.
  - Each header is three `TextBlock`s: the group name in the group's colour on the group's first column only, then the code, `Σ` or `%`, then the Hebrew name with the full name as its tooltip. Summary rows are bold with a top border.

- [ ] **Step 3: Run the smoke test, then `.\build\Test-All.ps1`**

Expected: 12 views construct; everything green.

- [ ] **Step 4: Render check.** Render `RishuiZaminWindow` off-screen on the Table tab with the `Five`-style sample plus a basement and a plot of 2370. Write a PNG to the scratchpad and look at it: frozen columns, colours, chips, and Hebrew headers in the right order. Send it to Daniel.

- [ ] **Step 5: Commit** `feat(rishui-zamin): Table tab in the Area Calc window`

---

### Task 6: Docs and the full check

**Files:**
- Modify: `docs/tools/rishui-zamin/README.md` (the Table tab, plot limits stored in the project)
- Modify: `docs/tools/rishui-zamin/backlog.md` (Task 3's in-Revit checks; nothing else)
- Modify: `CLAUDE.md` (the Rishui Zamin bullet: "…sets usage codes on areas and shows an area table checked against the plan's allowed % of plot")

- [ ] **Step 1: Write the docs.**
- [ ] **Step 2: Run `.\build\Test-All.ps1` and build `RVTuk.sln` for Release2023, 2024, 2025, 2026 and 2027.** Expected: all green, 0 errors.
- [ ] **Step 3: Commit** `docs(rishui-zamin): area table and plot limits`
