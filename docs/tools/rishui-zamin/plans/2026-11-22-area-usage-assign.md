# Area Calc: assign usage codes and show areas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Area Calc window set or clear an area's New/Old usage key for one or several areas, and show m² per area, per floor and for the sheet.

**Architecture:** Pure logic (groups, totals, the selection summary, search, suggestions, result types) goes in `RVTuk.Core.RishuiZamin` with unit tests. The window's list becomes a grouped, multi-select `ListBox` with a detail strip and a picker popup (`RVTuk.UI`). One new Revit handler sets the key parameter on the areas in one transaction. It is linked into KKarea and wired in both hosts through a new view-model delegate.

**Tech Stack:** C# (net48 / net8.0-windows / net10.0-windows), WPF, Revit API 2023–2027, xunit.

**Spec:** [`docs/tools/rishui-zamin/specs/2026-11-22-area-usage-assign-design.md`](../specs/2026-11-22-area-usage-assign-design.md). Read it alongside this plan. Section numbers below (§5.x) refer to it.

**Branch:** `rishui-zamin` in `D:\Coding\RVTuk`. No worktrees, no other branches (CLAUDE.md "Branches").

## Global Constraints

- Layers: Core has no Revit/WPF types; UI has no Revit types; Revit work reaches the UI only as delegates.
- Namespace = root namespace + folder path, exactly. New files go in the tool folders named below.
- The Revit API runs only on Revit's main thread: an `ExternalEvent` + `ManualResetEventSlim` ping-pong, called from the thread pool by the view model.
- Every linked handler must build under `Release2023` (KKarea): `#if REVIT2023` → `new ElementId((int)id)`, else `new ElementId(id)`.
- Group colours (add to `src/RVTuk.UI/Shared/Themes/DarkTheme.xaml`): `Brush.UsagePrimary` `#7CC67C`, `Brush.UsageService` `#45AFAF`, `Brush.UsageOther` `#A3A3A3`, `Brush.UsageProcess` `#E2C35C`. "No code" uses the existing `Brush.Warning`.
- **Total = Primary + Service.** Tooltip on the total: "Primary + Service".
- m² is shown with 2 decimals using `CultureInfo.InvariantCulture` (`"F2"`).
- Old usage never offers or accepts codes 300–302.
- One click = one undo step, including Run setup + assign. On any failure nothing changes.
- Assigning a New usage renames the area (the key does it). Clearing leaves the Name as it is.
- Every text test uses mixed Hebrew/Latin samples, never Hebrew-only ones (e.g. `"דירה 1 - Apt"`, `"Lobby מבואה"`).
- Run `.\build\Test-All.ps1` before every commit that touches Core or UI, and build `-c Release2023` and `-c Release2024` before every commit that touches the Revit side.

## Review Focus

1. **Typed gershayim:** office names write ממ״ד with U+05F4 (״) as often as with `"`. Suggestions must match both. (Task 2 test `Suggest_MatchesMamadWithGershayim`.)
2. **Invalid or out-of-catalog codes** (e.g. 3, 999 from an old template): the area groups as "No code", counts in the red box, not in Total. (Task 1 test `GroupOf_UnknownNewCode_FallsBackToOld_ThenNoCode`.)
3. **An area deleted in Revit between refresh and click:** nothing is written, the message says which area, and the sheet refreshes. (Task 3 manual check 4, Task 5 test `Assign_Failed_ShowsMessageAndRefreshes`.)
4. **Selection survives the post-assignment refresh**, also when the user is on a different floor and when a selected area no longer exists. (Task 5 test `Populate_RestoresSelectionById_DropsMissingIds`.)
5. **A legacy "Usage Type" stored as a number, not text:** blanking must write 0, not "", or the old code comes back. (Task 3 manual check 6.)

---

### Task 1: Core — usage groups, totals and the selection summary

**Files:**
- Create: `src/RVTuk.Core/RishuiZamin/UsageTotals.cs`
- Create: `src/RVTuk.Core/RishuiZamin/AreaSelectionSummary.cs`
- Test: `tests/RVTuk.Core.Tests/RishuiZamin/UsageTotalsTests.cs`
- Test: `tests/RVTuk.Core.Tests/RishuiZamin/AreaSelectionSummaryTests.cs`

**Interfaces:**
- Produces (namespace `RVTuk.Core.RishuiZamin`):
  - `enum UsageGroup { Primary, Service, Other, Process, NoCode }`
  - `static class UsageGroups { static UsageGroup Of(AreaRecord a); static UsageGroup OfCode(int? code); }`. A code that is null or not in `UsageCatalog` gives `NoCode`. `Of` uses the New code's group, else the Old code's group, else `NoCode`.
  - `sealed record UsageTotals(double Primary, double Service, double Other, double Process, double NoCode, int NoCodeCount) { double Total => Primary + Service; static UsageTotals Sum(IEnumerable<AreaRecord> areas); }`
  - `enum UsageValueKind { None, Code, Mixed }`, `sealed record UsageValue(UsageValueKind Kind, int? Code)`
  - `sealed class AreaSelectionSummary { string Title; double AreaM2; string? Detail; UsageValue New; UsageValue Old; static AreaSelectionSummary From(IReadOnlyList<AreaRecord> areas); }`

- [ ] **Step 1: Write the failing tests**

`UsageTotalsTests`:
- `GroupOf_UsesNewCode`: New 1, Old 105 → `Primary`.
- `GroupOf_OldOnly_UsesOld`: New null, Old 105 → `Service`.
- `GroupOf_UnknownNewCode_FallsBackToOld_ThenNoCode`: New 999, Old 30 → `Primary`. New 3, Old null → `NoCode`.
- `GroupOf_ProcessWithOld_IsProcess`: New 301, Old 1 → `Process`.
- `Sum_SplitsByGroup_TotalIsPrimaryPlusService`: areas (1, 84.30), (105, 22.00), (250, 10.00), (301→1, 12.50), (none, 5.25) → Primary 84.30, Service 22.00, Other 10.00, Process 12.50, NoCode 5.25, NoCodeCount 1, Total 106.30 (compare with tolerance 1e-9).
- `Sum_Empty_IsAllZero`.

`AreaSelectionSummaryTests`:
- `None_Selected`: Title `"Select one or more areas to set their usage."`, AreaM2 0, Detail null, both values `None`.
- `One_ShowsNumberNameAndErrorsInWords`: Number "002", Name "דירה 1 - Apt", Errors `NoUsageCode | ZeroArea` → Title `"002 — דירה 1 - Apt"`, Detail `"No usage code · Area is zero"`. `BadBoundary` → `"Boundary is not closed"`. No Number → Title starts with `"(no #)"`.
- `One_NoErrors_DetailNull`.
- `Many_SumsAndCountsUncoded`: 3 areas of 10.00, 20.50 and 1.25 m², one with `NoUsageCode` → Title `"3 areas selected"`, AreaM2 31.75, Detail `"⚠ 1 without a usage code"`.
- `Many_SameNew_IsCode_DifferentOld_IsMixed`: New 1,1 and Old 105,null → New `(Code, 1)`, Old `(Mixed, null)`.
- `Many_AllNull_IsNone`.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter "FullyQualifiedName~UsageTotals|FullyQualifiedName~AreaSelectionSummary"`
Expected: build error — types not defined.

- [ ] **Step 3: Implement `UsageGroups`, `UsageTotals`, `AreaSelectionSummary`**

Use `UsageCatalog.ByCode` for the kind (`UsageKind` maps 1:1 onto the first four `UsageGroup` values). The error words are joined with `" · "` in flag order: NoUsageCode, BadBoundary, ZeroArea. The single-area Title is `"(no #)"` or the Number, then `" — " + Name` when Name is not blank (the same rule as `AreaRowViewModel.NumberAndName`, which Task 5 switches to call this).

- [ ] **Step 4: Run them and see them pass** (same command). Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/RVTuk.Core/RishuiZamin/UsageTotals.cs src/RVTuk.Core/RishuiZamin/AreaSelectionSummary.cs tests/RVTuk.Core.Tests/RishuiZamin/UsageTotalsTests.cs tests/RVTuk.Core.Tests/RishuiZamin/AreaSelectionSummaryTests.cs
git commit -m "feat(rishui-zamin): usage groups, m² totals and selection summary in Core"
```

---

### Task 2: Core — picker search, suggestions and assignment result types

**Files:**
- Create: `src/RVTuk.Core/RishuiZamin/UsageSearch.cs`
- Create: `src/RVTuk.Core/RishuiZamin/UsageSuggester.cs`
- Create: `src/RVTuk.Core/RishuiZamin/UsageAssignResult.cs`
- Test: `tests/RVTuk.Core.Tests/RishuiZamin/UsageSearchTests.cs`
- Test: `tests/RVTuk.Core.Tests/RishuiZamin/UsageSuggesterTests.cs`

**Interfaces:**
- Produces (namespace `RVTuk.Core.RishuiZamin`):
  - `enum UsageKeySlot { New, Old }`
  - `enum UsageAssignStatus { Done, KeysMissing, Failed }`, `sealed record UsageAssignResult(UsageAssignStatus Status, string Message)`
  - `static class UsageSearch { static IReadOnlyList<UsageEntry> Filter(string? query, UsageKeySlot slot); }`: results are in `UsageCatalog.All` order. `Old` drops `UsageKind.Process`.
  - `static class UsageSuggester { static IReadOnlyList<int> Suggest(IEnumerable<string?> names, UsageKeySlot slot, int max = 4); }`

- [ ] **Step 1: Write the failing tests**

`UsageSearchTests`:
- `Empty_ReturnsAll_New` (count == `UsageCatalog.All.Count`). `Empty_Old_HasNoProcess`.
- `Digits_FilterByCodePrefix`: `"11"` → exactly the codes in the catalog that start with "11" (11, 110–119 present in the catalog), in catalog order.
- `Text_MatchesHebrewSubstring`: `"מרפסת"` → contains 30. `"  מרפסת "` is trimmed.
- `GroupName_MatchesByPrefix_CaseInsensitive`: `"serv"` → every Service entry and nothing else. `"PROC"` with `Old` → empty.

`UsageSuggesterTests` (keyword table from spec §5.6, in that order; matching is case-insensitive substring):
- `Suggest_Balcony`: `["מרפסת 3 - Balcony"]` → `[30, 110, 250]`.
- `Suggest_LatinOnly`: `["Lobby"]` → `[105]`.
- `Suggest_MatchesMamadWithGershayim`: `["ממ״ד"]` (U+05F4) and `["ממ\"ד"]` → both `[101]`.
- `Suggest_ManyNames_DistinctAndCappedAt4`: `["מרפסת", "חניה", "מחסן"]` → `[30, 110, 250, 33]`.
- `Suggest_NoMatch_Empty`. `Suggest_NullNames_Ignored`.

- [ ] **Step 2: Run them and see them fail.** Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter "FullyQualifiedName~UsageSearch|FullyQualifiedName~UsageSuggester"`. Expected: build error.

- [ ] **Step 3: Implement.** Search: trim. All-digit query → `Code.ToString(Invariant).StartsWith(query)`. Otherwise `HebrewName.Contains(query)` (ordinal) or `Kind.ToString().StartsWith(query, OrdinalIgnoreCase)`. Suggester: a private static list of `(string[] keywords, int[] codes)`, the spec's table, with `ממ"ד` written in both quote forms (`"` and `״`).

- [ ] **Step 4: Run them and see them pass.**

- [ ] **Step 5: Commit** — `feat(rishui-zamin): usage search, name suggestions and assign result types`.

---

### Task 3: Revit — set the usage key on areas (both hosts)

**Files:**
- Create: `src/RVTuk.Revit/RishuiZamin/UsageKeyAssigner.cs`
- Create: `src/RVTuk.Revit/RishuiZamin/ExternalEvents/SetUsageKeyEventHandler.cs`
- Modify: `src/RVTuk.Revit/RishuiZamin/UsageKeyScheduleBuilder.cs` (make `KeyRowName` `internal`; expose the two schedule names as `internal const`)
- Modify: `src/RVTuk.Revit/Application.cs:51-58,153-158` and `src/KKarea.Revit/Application.cs:21-27,56-61` (add `SetUsageKeyHandler` / `SetUsageKeyEvent`, created next to the others)
- Modify: `src/KKarea.Revit/KKarea.Revit.csproj:45-49` (link both new files, like the others)

**Interfaces:**
- Consumes: `UsageKeySlot`, `UsageAssignResult`, `UsageAssignStatus` (Task 2); `UsageKeyScheduleBuilder.EnsureUsageKeySchedules(Document)`.
- Produces:
  - `static class UsageKeyAssigner { static UsageAssignResult Assign(Document doc, UsageKeySlot slot, int? code, IReadOnlyList<long> areaIds, bool runSetupFirst); }`
  - `class SetUsageKeyEventHandler : IExternalEventHandler { void Prepare(UsageKeySlot slot, int? code, IReadOnlyList<long> areaIds, bool runSetupFirst); void WaitForCompletion(); UsageAssignResult Result { get; } }`: same pattern as `SetupUsageKeysEventHandler`. An exception gives `Failed`. Name: `"RVTuk.SetUsageKey"`.
  - `Application.SetUsageKeyHandler`, `Application.SetUsageKeyEvent` in both hosts.

`Assign`, in this order (spec §5.7):
1. Guards → `Failed` with nothing changed: `doc.IsReadOnly`; `slot == Old` with a Process code; a code not in `UsageCatalog`; any id that is not an `Area` in `doc` (the message names the first missing id).
2. Find the key schedule: a `ViewSchedule` named "New Usage" / "Old Usage" with `Definition.IsKeySchedule` and category `OST_Areas`. Its key parameter name is `KeyScheduleParameterName`. Find the row for `code`: first a row whose `RZ_USAGE_TYPE` / `RZ_USAGE_TYPE_OLD` string, trimmed, equals the code; else a row whose `KeyRowName` is the code alone or the code followed by a non-digit. If the schedule is missing, or the row is missing for a non-null code:
   - with `runSetupFirst == false`: return `KeysMissing` and change nothing. Messages: `This project has no "New Usage" key schedule yet. Run Setup Usage Keys now, then set the code?` / `The "New Usage" key schedule has no row for code 119. Run Setup Usage Keys now to add it, then set the code?` (with "Old Usage" for the Old slot).
   - with `runSetupFirst == true`: inside a `TransactionGroup` with the same name as the transaction, call `EnsureUsageKeySchedules`. On `ok == false`, `RollBack` and return `Failed` with its message. Then look the schedule and row up again; if still missing, `RollBack` and return `Failed`. At the end, `Assimilate`.
3. One `Transaction` named `Area Calc — set New usage` / `Area Calc — set Old usage`. On each area:
   - set the key parameter to the row's id (`ElementId.InvalidElementId` for none);
   - for none, `doc.Regenerate()`, then if the area's `RZ_*` code parameter is writable and has a value, set it to `""`;
   - blank the legacy pair (New: "Usage Type", "Usage Type Name"; Old: "Usage Type Prev", "Usage Type Prev. Name") where the parameter exists and is writable: String → `""`, Integer → `0`, Double → `0.0`.
4. `Commit` → `Done` with message `""`. On any exception: roll back (transaction and group) and return `Failed` with `ex.Message`.

- [ ] **Step 1: Implement `UsageKeyAssigner` and `SetUsageKeyEventHandler`; register them in both `Application.cs`; link both files in `KKarea.Revit.csproj`.**

- [ ] **Step 2: Build every Revit year**

Run: `dotnet build RVTuk.sln -c Release2023`, then the same with `-c Release2024`, `-c Release2025`, `-c Release2027`.
Expected: `Build succeeded` with 0 errors each.

- [ ] **Step 3: Commit** — `feat(rishui-zamin): SetUsageKey handler sets an area's usage key in one undo step`.

Manual checks in Revit happen after Task 6, once the window can call this (listed there).

---

### Task 4: UI — the row, floor and picker view models

**Files:**
- Modify: `src/RVTuk.UI/RishuiZamin/ViewModels/AreaRowViewModel.cs`
- Modify: `src/RVTuk.UI/RishuiZamin/ViewModels/AreaLevelGroupViewModel.cs`
- Create: `src/RVTuk.UI/RishuiZamin/ViewModels/UsagePickerViewModel.cs`
- Test: `tests/RVTuk.UI.Tests/RishuiZamin/UsagePickerViewModelTests.cs`
- Test: `tests/RVTuk.UI.Tests/RishuiZamin/AreaRowViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 and Task 2 types.
- Produces:
  - `AreaRowViewModel : ViewModelBase` gains `bool IsSelected` (notifying), `string AreaText` (`"84.30"`), `string? NewCodeText`, `string? OldCodeText` (`"←1"`), `UsageGroup NewGroup`, `UsageGroup OldGroup`, `UsageGroup Group` (= `UsageGroups.Of`). `NumberAndName` now comes from the same rule as `AreaSelectionSummary`. `Tag` stays for the tooltip.
  - `AreaLevelGroupViewModel` gains `UsageTotals Totals` (computed from its rows when the group is built).
  - `enum PickerItemKind { Clear, Heading, Suggestion, Entry }`
  - `sealed record PickerItem(PickerItemKind Kind, string Text, UsageEntry? Entry, UsageGroup Group, bool IsCurrent)`
  - `class UsagePickerViewModel : ViewModelBase { UsagePickerViewModel(UsageKeySlot slot, IReadOnlyList<string?> selectedNames, int? currentCode); string Query { get; set; } ObservableCollection<PickerItem> Items { get; } int HighlightedIndex { get; } void Move(int delta); void PickHighlighted(); void Pick(PickerItem item); event Action<int?>? Picked; }`

Item list (spec §5.6):
- Empty query: `Clear` (`"— none — (clear)"`), then a `Heading` `"Suggested from the name"` and `Suggestion` items, only when there are suggestions. Then for each group in table order a `Heading` and its `Entry` items.
- Non-empty query: only the matching entries, under a `Heading` for each group that has matches. No Clear, no suggestions.
- Heading texts: `"PRIMARY  שטחים עיקריים"`, `"SERVICE  שטחי שירות"`, `"OTHER  אחר"`, `"PROCESS  סימוני תהליך"`.
- Entry and suggestion text: `"«code»  «HebrewName»"`. `IsCurrent` when the code equals `currentCode`.

`Move` skips headings and clamps at the ends. Highlighting starts on the first non-heading item. `Pick` of `Clear` raises `Picked(null)`. Picking a heading does nothing.

- [ ] **Step 1: Write the failing tests**

`UsagePickerViewModelTests`:
- `EmptyQuery_StartsWithClear_ThenSuggestions_ThenGroups`: names `["מרפסת - Balcony"]`, New → item 0 `Clear`, item 1 the suggestions heading, items 2–4 suggestions 30, 110, 250, then the `"PRIMARY  שטחים עיקריים"` heading.
- `NoSuggestions_NoSuggestionHeading`.
- `Old_HasNoProcessHeading`.
- `Query_ShowsOnlyMatchingGroups`: `Query = "11"` → no `Clear`, headings only for the groups of the matches.
- `Move_SkipsHeadings_AndClamps`.
- `PickClear_RaisesNull`. `PickHighlighted_RaisesCode`.
- `CurrentCode_IsMarked`: currentCode 105 → exactly one item with `IsCurrent`, and its Entry.Code is 105.

`AreaRowViewModelTests`: AreaValue 84.3 → `AreaText == "84.30"` (also under a `de-DE` current culture). New 301, Old 1 → `NewCodeText "301"`, `OldCodeText "←1"`, `NewGroup Process`, `OldGroup Primary`.

- [ ] **Step 2: Run them and see them fail.** Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter "FullyQualifiedName~RishuiZamin"`. Expected: build error.

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run them and see them pass**, then `.\build\Test-All.ps1`. Expected: all green.

- [ ] **Step 5: Commit** — `feat(rishui-zamin): row m² and groups, floor totals, usage picker view model`.

---

### Task 5: UI — main view model: selection, totals, assigning

**Files:**
- Modify: `src/RVTuk.UI/RishuiZamin/ViewModels/RishuiZaminViewModel.cs`
- Modify: `src/RVTuk.Revit/RishuiZamin/ExternalEvents/SelectAreaEventHandler.cs` (`Prepare(IReadOnlyList<long> ids)`, selects them all, `#if REVIT2023` per id)
- Modify: `src/RVTuk.Revit/RishuiZamin/Commands/RishuiZaminCommand.cs` and `src/KKarea.Revit/Commands/RishuiZaminCommand.cs` (new `selectInModel` type; the `assignUsage` delegate through `Application.SetUsageKeyHandler/Event`, the same pattern as `setupUsageKeys`)
- Test: `tests/RVTuk.UI.Tests/RishuiZamin/RishuiZaminViewModelTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 4. Task 3's `Application.SetUsageKeyHandler` / `SetUsageKeyEvent`.
- Produces (on `RishuiZaminViewModel`):
  - Constructor: `(Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract, Action<IReadOnlyList<long>> selectInModel, Func<IReadOnlyList<AreaRecord>, RishuiZaminConfig, (bool ok, string msg)> export, Func<(bool ok, string msg)>? setupUsageKeys = null, Func<UsageKeySlot, int?, IReadOnlyList<long>, bool, UsageAssignResult>? assignUsage = null)`. `ViewSmokeTests` keeps passing `null!` for the first three.
  - `void Populate(IReadOnlyList<(long Id, AreaRecord Rec)> extracted)`: what `Refresh` does today on the Dispatcher (grouping, scale), moved into a public method. It also restores `IsSelected` on rows whose id was selected before and drops ids that are gone.
  - `UsageTotals SheetTotals`, `bool HasUncoded` (NoCodeCount > 0), `AreaSelectionSummary Selection`, `IReadOnlyList<AreaRowViewModel> SelectedRows`.
  - `void OnSelectionChanged()`: reads the rows' `IsSelected`, updates `Selection`/`SelectedRows`/`CanAssign` and calls `selectInModel` with the selected ids (best effort, as today). Replaces `SelectedRow`.
  - `bool IsBusy` (true while a refresh or an assignment runs), `bool CanAssign` (`assignUsage != null && SelectedRows.Count > 0 && !IsBusy`).
  - `void Assign(UsageKeySlot slot, int? code)`: on the thread pool, call `assignUsage(slot, code, ids, false)`.
    - `KeysMissing`: on the Dispatcher, `ConfirmRunSetup?.Invoke(result.Message)`. On true, call again with `true` on the thread pool.
    - `Done`: `StatusText` = `"New usage 110 - מרפסת on 2 areas · one undo step in Revit"` (`"on 1 area"` for one, `"Old usage"` for Old, `"New usage cleared on 2 areas · one undo step in Revit"` for none), then `Refresh`.
    - `Failed`: `ExportCompleted(false, message)`, then `Refresh`.
  - `Func<string, bool>? ConfirmRunSetup { get; set; }` (set by the window), `string? StatusText`.

- [ ] **Step 1: Write the failing tests** (inside `Sta.Run`; build the VM with stub delegates and call `Populate` directly)
- `Populate_ComputesSheetTotals`: the five areas from Task 1 → `SheetTotals.Total` 106.30, `HasUncoded` true.
- `OnSelectionChanged_MirrorsIdsToModel_AndSummarises`: mark two rows selected → the stub `selectInModel` got both ids; `Selection.Title == "2 areas selected"`.
- `Populate_RestoresSelectionById_DropsMissingIds`: select ids 1 and 2, re-`Populate` with 2 and 3 only → only row 2 `IsSelected`, `SelectedRows.Count == 1`.
- `CanAssign_FalseWithoutDelegate_OrSelection`.
- `StatusText_Wording`: test the formatter `static string FormatAssignStatus(UsageKeySlot slot, int? code, int count)`. Expect `"New usage 110 - מרפסת on 2 areas · one undo step in Revit"`, `"Old usage 1 - מגורים on 1 area · one undo step in Revit"` and `"New usage cleared on 3 areas · one undo step in Revit"`.
- `Assign_Failed_ShowsMessageAndRefreshes`: the logic after a result comes back sits in `void HandleAssignResult(UsageAssignResult r, UsageKeySlot slot, int? code, int count)`, called on the Dispatcher; test that method directly. A `Failed` result raises `ExportCompleted(false, msg)` and calls `extract` again (count the stub's calls; the refresh runs on the thread pool, so wait up to 5 s on a `ManualResetEventSlim` set inside the stub).

- [ ] **Step 2: Run them and see them fail.** Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter "FullyQualifiedName~RishuiZaminViewModel"`.

- [ ] **Step 3: Implement the view model; change `SelectAreaEventHandler`; wire `assignUsage` and the new `selectInModel` in both commands.** Removing `SelectedRow` breaks `RishuiZaminWindow.xaml.cs`'s `AreaTree_SelectedItemChanged`. Until Task 6 replaces the tree, make that handler clear the other rows' `IsSelected`, set the clicked row's, and call `OnSelectionChanged()`, so the window still builds and works.

- [ ] **Step 4: Run `.\build\Test-All.ps1`, then build `-c Release2023` and `-c Release2024`.** Expected: all green, 0 errors.

- [ ] **Step 5: Commit** — `feat(rishui-zamin): multi-select, sheet totals and usage assignment in the view model`.

---

### Task 6: UI — the window: summary, grouped list, detail strip, picker

**Files:**
- Modify: `src/RVTuk.UI/Shared/Themes/DarkTheme.xaml` (the four brushes)
- Modify: `src/RVTuk.UI/RishuiZamin/Views/RishuiZaminWindow.xaml` and `.xaml.cs`
- Create: `src/RVTuk.UI/RishuiZamin/Views/UsageGroupBrushConverter.cs` (`UsageGroup` → the brush from the theme; `NoCode` → `Brush.Warning`)
- Create: `src/RVTuk.UI/RishuiZamin/Views/UsagePickerPopup.xaml` and `.xaml.cs` (a `UserControl` hosted in a `Popup`, DataContext a `UsagePickerViewModel`)

**Interfaces:**
- Consumes: Tasks 4 and 5.

The layout follows spec §5.1, top to bottom: toolbar; sheet summary (the four group boxes always shown, a zero muted; the red "No code" box bound to `HasUncoded`; "Total m²" with the tooltip); the existing flagged banner; the list; the detail strip; the status line.
- **List:** a `ListBox`, `SelectionMode="Extended"`, `VirtualizingPanel.IsVirtualizing="False"`, `ItemContainerStyle` binding `IsSelected` two-way, over a `CollectionViewSource` grouped by level. Each group header is an `Expander` showing the name, count, ⚠, the floor's non-zero group m² in group colours, the red "No code" m² when present, and `Σ` + the floor total. Each row shows name, m² (fixed-width, muted, right-aligned) and the code pill (each half in its group colour; red border and red name when `HasError`). `SelectionChanged` → `_vm.OnSelectionChanged()`.
- **Detail strip:** `Selection.Title`, m², `Selection.Detail`. There are two picker buttons, "New usage" and "Old usage", whose text shows `«code» - «name»` in the group colour, `— none —`, or `(mixed)` (muted, italic). They are disabled unless `CanAssign`.
- **Picker:** a button opens the popup with a new `UsagePickerViewModel(slot, selected names, shared code or null)`. The popup opens above the strip when there is no room below (`Placement="Top"` fallback through `CustomPopupPlacementCallback`, or Bottom/Top by available screen space). The search box takes focus when it opens. ↑/↓ call `Move`, Enter calls `PickHighlighted`, Esc and clicking outside close it (`StaysOpen="False"`). `Picked` closes it and calls `_vm.Assign(slot, code)`.
- `ConfirmRunSetup` = a Yes/No `MessageBox` titled "RVTuk – Area Calc", showing the result's message. Yes = Run setup. A `MessageBox` cannot rename its buttons; the spec's [Run setup] [Cancel] wording is fine to drop.

- [ ] **Step 1: Implement.**

- [ ] **Step 2: Run `.\build\Test-All.ps1`.** Expected: green, including `ViewSmokeTests.EveryView_ConstructsWithTheDarkTheme`.

- [ ] **Step 3: Deploy and check in Revit 2024** (`.\Deploy.ps1 2024` from an elevated shell, branch `rishui-zamin`). On a sheet with area plans:
  1. Floor and sheet totals match a Revit area schedule's sums; Total = Primary + Service.
  2. Click a red area, set New usage → it turns green/cyan, the Name follows, one Ctrl+Z undoes it.
  3. Ctrl/Shift-select three areas, set Old usage → all three change, one Ctrl+Z.
  4. Delete a selected area in Revit, then assign without refreshing → a message names it, nothing changes, the list refreshes.
  5. "— none —" on an area → it turns red and stays red after Refresh.
  6. An area with "Usage Type" filled in (text, and in a project where it is a number) → clear it → it stays red after Refresh.
  7. A project without key schedules → set a code → the Run setup question → Yes → schedules made and code set; one Ctrl+Z undoes both.
  8. Does clearing the key leave `RZ_USAGE_TYPE` with its old value? Note the answer in the spec's §7.
  9. Mixed Hebrew/Latin names read correctly in the rows, the strip and the picker.

- [ ] **Step 4: Check in Revit 2023 (KKarea)** (`.\Deploy.ps1 2023`): repeat 2, 3 and 7.

- [ ] **Step 5: Update the docs.** In `docs/tools/rishui-zamin/README.md`, add one paragraph under the tool's features: set usage from the window, the totals. Record the outcome of Step 3.8 in the spec's §7. Move any open checks to `docs/tools/rishui-zamin/backlog.md`.

- [ ] **Step 6: Commit** — `feat(rishui-zamin): Area Calc window shows m² totals and sets usage codes`.
