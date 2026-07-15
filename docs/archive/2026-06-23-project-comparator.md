# Project Comparator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the v1 Project Comparator / Template Builder — snapshot-based, report-only comparison of Revit View Templates, with an editable curated "Standard".

**Architecture:** Pure-Core comparison engine (snapshots → match → diff → score → curate Standard) with serializable `[DataContract]` DTOs and a SQLite store, fronted by a WPF MVVM window, with Revit-API extraction isolated in RVTuk.Revit behind `ExternalEvent` handlers. Categories plug in via `ICategoryExtractor` (Revit) + `ICategoryComparer`/`ICategoryMerger` (Core).

**Tech Stack:** C#, multi-target net48 (Revit 2023/24) + net8.0-windows (Revit 2025); SQLite (System.Data.SQLite / Microsoft.Data.Sqlite); WPF; xUnit for Core tests (net8).

## Global Constraints

- RVTuk.Core must NOT reference Revit API or WPF types (verbatim from spec §5).
- DTOs serialized for storage must be `[DataContract]`/`[DataMember]`; round-trip via `DataContractJsonSerializer` on net48 (`#if REVIT2024`) and `System.Text.Json` on net8. No `System.Text.Json` on net48 (clashes with Revit preloaded assemblies).
- SQLite: `journal_mode=DELETE`, `busy_timeout=5000`, `synchronous=NORMAL`; `#if REVIT2024` `SQLiteConnection`/`SQLiteCommand` aliases; one statement per `ExecuteScalar`; on-open `MigrateSchema`; dates UTC ISO-8601 via `DbConvert.ParseUtc`.
- New DB: `{LibraryFolderPath}\.Setup\RVTuk.Standards.db`.
- Never write to a `.rvt`/`.rte` in v1. `ItemDiff.FutureApplyToken` stays null; no `ICategoryApplier` registered.
- Recommendations rank on completeness + provenance; never assert "newer" as fact.
- View Templates match within `ViewType` buckets; inclusion-aware diff (controlled vs excluded before values).

---

## File structure

```
src/RVTuk.Core/
  Models/Comparison/   DiffKind, FieldDiff, ItemDiff, CategoryDiffResult, ComparisonResult,
                       DiffSummary, SnapshotMeta, CategorySnapshot, ViewTemplatesSnapshot (+ItemDto),
                       ItemProvenance, DependencyClosure, MergeResult, StandardSnapshot
  Comparison/          ICategoryComparer, ICategoryMerger, CategoryRegistry, ComparisonEngine,
                       Matcher, ViewTemplateComparer, ViewTemplateMerger, StandardCurator,
                       CompletenessScorer
  Serialization/       SnapshotJson
  Database/            SnapshotRepository
  Reporting/           HtmlReportWriter
tests/RVTuk.Core.Tests/ (new, net8 xUnit) — one test file per Core unit
src/RVTuk.Revit/
  Extraction/          ICategoryExtractor, ViewTemplateExtractor
  ExternalEvents/      CaptureSnapshotEventHandler, OpenModelSnapshotEventHandler
  Commands/            CompareProjectsCommand
src/LibraryBrowser/RVTuk.UI/
  ViewModels/          ComparatorViewModel, CategoryViewModelBase, ViewTemplatesCategoryViewModel,
                       PlaceholderCategoryViewModel, ItemDiffViewModel, FieldDiffViewModel, DecisionOption
  Views/               ComparatorWindow.xaml(.cs)
```

---

## PHASE A — Pure Core (TDD, runs here)

### Task 1: Test project

**Files:** Create `tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`; modify `RVTuk.sln`.

- [ ] Create an xUnit project targeting `net8.0-windows`, referencing `src/RVTuk.Core/RVTuk.Core.csproj` (it builds net8 under Release2025). Use `<DefineConstants>REVIT2025</DefineConstants>` path implicitly via the Core net8 TFM.
- [ ] Add to solution; `dotnet test` runs (0 tests OK).
- [ ] Commit: `test: add RVTuk.Core.Tests xUnit project`.

> Note: Core multi-targets; tests build the net8 target. net48-specific serialization is guarded by `#if REVIT2024` and verified by building Release2024 at the end (compile check), not by unit test.

### Task 2: Diff model DTOs

**Files:** Create `Models/Comparison/{DiffKind,FieldDiff,ItemDiff,CategoryDiffResult,ComparisonResult,DiffSummary}.cs`; Test `tests/.../DiffModelTests.cs`.

**Produces:** `enum DiffKind { Added, Removed, Changed, Unchanged }`; `FieldDiff(string FieldId, string Label, string? ValueA, string? ValueB)` with computed `bool IsEqual => ValueA==ValueB`; `ItemDiff{ string Key; string DisplayName; DiffKind Kind; List<FieldDiff> Fields; double CompletenessA; double CompletenessB; ItemProvenance? Provenance; string? FutureApplyToken; }`; `CategoryDiffResult{ string CategoryId; string DisplayName; List<ItemDiff> Items; DiffSummary Summary; }`; `DiffSummary{ int Added; int Removed; int Changed; int Unchanged; }`; `ComparisonResult{ SnapshotMeta SideA; SnapshotMeta SideB; List<CategoryDiffResult> Categories; }`.

- [ ] Test: `FieldDiff("Scale","Scale","1:100","1:50").IsEqual` is false; equal values → true.
- [ ] Implement DTOs (plain classes; `IsEqual` computed).
- [ ] Test passes; commit `feat(core): diff model DTOs`.

### Task 3: Snapshot DTOs

**Files:** Create `Models/Comparison/{SnapshotMeta,CategorySnapshot,ViewTemplatesSnapshot,ViewTemplateDto,ControlledParam,ItemProvenance,DependencyClosure,MergeResult,StandardSnapshot}.cs`; Test `SnapshotDtoTests.cs`.

**Produces:**
- `SnapshotMeta{ string SourceKind /*Template|Project|Standard*/; string SourceName; string? SourcePath; int RevitYear; string CapturedUtc; int SchemaVersion; bool IsMutable; int Revision; }`
- `abstract CategorySnapshot{ string CategoryId; }`
- `ViewTemplateDto{ string Name; string UniqueId; string ViewType; List<ControlledParam> Included; List<KeyValuePair<string,string>> Settings; string CategoryOverridesHash; List<string> FilterNames; }`
- `ControlledParam{ string Id; string Label; bool Controlled; }`
- `ViewTemplatesSnapshot : CategorySnapshot { List<ViewTemplateDto> Templates; }` (CategoryId="ViewTemplates")
- `ItemProvenance{ string ItemKey; string SourceName; string CapturedUtc; List<KeyValuePair<string,string>> FieldSources; }`
- `DependencyClosure{ List<string> Filters; List<string> ParameterGuids; List<string> Patterns; List<string> Subcategories; }`
- `MergeResult{ bool Applied; string? Conflict; List<string> ClosureGaps; }`
- `StandardSnapshot{ SnapshotMeta Meta; List<CategorySnapshot> Categories; }`

All `[DataContract]`/`[DataMember]`; flat; key/value lists not dictionaries.

- [ ] Test: construct a `ViewTemplatesSnapshot` with one template; assert fields hold.
- [ ] Implement DTOs with DataContract attrs.
- [ ] Test passes; commit `feat(core): snapshot + standard DTOs`.

### Task 4: SnapshotJson serializer

**Files:** Create `Serialization/SnapshotJson.cs`; Test `SnapshotJsonTests.cs`.

**Produces:** `static class SnapshotJson { string Serialize<T>(T value); T Deserialize<T>(string json); }` — `#if REVIT2024` DataContractJsonSerializer else System.Text.Json.

- [ ] Test: round-trip a `ViewTemplatesSnapshot` (2 templates, settings, filters) → Serialize → Deserialize → deep-equal.
- [ ] Implement with the `#if` split.
- [ ] Test passes; commit `feat(core): snapshot JSON serializer`.

### Task 5: Matcher + ComparisonEngine

**Files:** Create `Comparison/{Matcher,ComparisonEngine,ICategoryComparer,CategoryRegistry}.cs`; Test `ComparisonEngineTests.cs`.

**Produces:**
- `interface ICategoryComparer { string CategoryId; string DisplayName; CategorySnapshot LoadSnapshot(string payloadJson); CategoryDiffResult Compare(CategorySnapshot a, CategorySnapshot b); }`
- `class CategoryRegistry { void Register(ICategoryComparer c); ICategoryComparer Get(string categoryId); IEnumerable<ICategoryComparer> All; }`
- `static class Matcher { (List<(T a,T b)> matched, List<T> onlyA, List<T> onlyB) OuterJoin<T>(IEnumerable<T> a, IEnumerable<T> b, Func<T,string> key); }`
- `class ComparisonEngine { ComparisonEngine(CategoryRegistry reg); ComparisonResult Compare(SnapshotMeta a, SnapshotMeta b, IEnumerable<CategorySnapshot> snapsA, IEnumerable<CategorySnapshot> snapsB); }` — for each CategoryId present, dispatch to the registered comparer.

- [ ] Test `Matcher.OuterJoin`: keys {a,b,c} vs {b,c,d} → matched [b,c], onlyA [a], onlyB [d].
- [ ] Implement Matcher; test passes.
- [ ] Test `ComparisonEngine` with a fake comparer returning a known `CategoryDiffResult` → result aggregates it.
- [ ] Implement engine + registry; test passes; commit `feat(core): matcher + comparison engine`.

### Task 6: ViewTemplateComparer (inclusion-aware diff)

**Files:** Create `Comparison/ViewTemplateComparer.cs`; Test `ViewTemplateComparerTests.cs`.

**Consumes:** Matcher, ICategoryComparer, ViewTemplatesSnapshot, CompletenessScorer (Task 7 — write scorer first or stub then fill).
**Produces:** `class ViewTemplateComparer : ICategoryComparer` (CategoryId="ViewTemplates"). Matching key = `ViewType + "|" + Name`. For matched pairs, field diff over the union of `Settings` keys + the `Included` set, classifying per spec §3.2 (Identical / Differs / A-only-control / B-only-control / both-uncontrolled→skip). Adds a synthetic `FieldDiff` "V/G Overrides" when `CategoryOverridesHash` differs, and "Filters" when `FilterNames` sets differ.

- [ ] Test: two templates same name+type, one differing setting controlled in both → one `Changed` ItemDiff with one FieldDiff.
- [ ] Test: a setting excluded in B but controlled in A → field classified A-only-control (FieldDiff with ValueB=null, not equal).
- [ ] Test: setting excluded in both → no FieldDiff emitted.
- [ ] Test: template only in A → ItemDiff Kind=Added; only in B → Removed.
- [ ] Test: different ViewType same Name → treated as two one-sided items (not matched).
- [ ] Implement; tests pass; commit `feat(core): view template comparer`.

### Task 7: CompletenessScorer

**Files:** Create `Comparison/CompletenessScorer.cs`; Test `CompletenessScorerTests.cs`.

**Produces:** `static class CompletenessScorer { double Score(ViewTemplateDto t); }` → weighted fraction in [0,1]: weights — any V/G override (hash non-empty) 0.30, ≥1 filter 0.25, view-range/phase present 0.15, detail level non-default 0.10, scale set 0.10, discipline set 0.10. Sum of present weights.

- [ ] Test: empty template scores ~0; fully-populated scores 1.0; partial sums correctly.
- [ ] Implement; wire into ViewTemplateComparer (`CompletenessA/B`); tests pass; commit `feat(core): completeness scorer`.

### Task 8: StandardCurator + ICategoryMerger

**Files:** Create `Comparison/{ICategoryMerger,ViewTemplateMerger,StandardCurator}.cs`; Test `StandardCuratorTests.cs`.

**Produces:**
- `interface ICategoryMerger { string CategoryId; MergeResult AcceptIntoStandard(StandardSnapshot std, CategorySnapshot source, string itemKey, DependencyClosure deps); }`
- `class ViewTemplateMerger : ICategoryMerger` — copies the named `ViewTemplateDto` into the Standard's `ViewTemplatesSnapshot` (creating it if absent), records `ItemProvenance`, returns `MergeResult{Applied=true}`; on name conflict returns `MergeResult{Applied=false, Conflict="exists"}` unless `replace` (model conflict as: remove existing then add — expose `AcceptIntoStandard(..., bool replace)` overload). Records closure gaps if `deps` lists unresolved names (caller supplies; merger stores in provenance).
- `class StandardCurator { StandardSnapshot LoadOrCreate(...); MergeResult Accept(StandardSnapshot std, CategorySnapshot source, string categoryId, string itemKey, DependencyClosure deps, bool replace); }` — looks up merger by categoryId, bumps `Meta.Revision`.

- [ ] Test: accept a template into an empty Standard → Applied, template present, provenance recorded, Revision incremented.
- [ ] Test: accept same name again without replace → Conflict; with replace → replaced, single instance.
- [ ] Implement; tests pass; commit `feat(core): standard curator + merger`.

### Task 9: SnapshotRepository (SQLite)

**Files:** Create `Database/SnapshotRepository.cs`; Test `SnapshotRepositoryTests.cs`.

**Consumes:** SnapshotJson, existing `DbConvert`.
**Produces:** `class SnapshotRepository { SnapshotRepository(string dbPath); void EnsureSchema(); long SaveSnapshot(SnapshotMeta meta, IEnumerable<CategorySnapshot> cats); SnapshotMeta? GetMeta(long id); List<SnapshotMeta> ListSnapshots(); List<CategorySnapshot> LoadCategories(long snapshotId, Func<string,string,CategorySnapshot> deserialize); void LogStandardChange(long standardId, string categoryId, string itemKey, string action, long? sourceId, string provenanceJson); }` — schema from spec §5.4; same pragmas/aliases as `IndexRepository`.

- [ ] Test (net8, Microsoft.Data.Sqlite, temp file): EnsureSchema idempotent; SaveSnapshot then ListSnapshots returns it; LoadCategories round-trips a ViewTemplatesSnapshot via SnapshotJson.
- [ ] Implement; tests pass; commit `feat(core): snapshot repository`.

### Task 10: HtmlReportWriter

**Files:** Create `Reporting/HtmlReportWriter.cs`; Test `HtmlReportWriterTests.cs`.

**Produces:** `static class HtmlReportWriter { string Write(ComparisonResult result); }` — self-contained HTML (inline CSS), sections: header, summary table, per-template roster + detail, recommendations list, Standard changelog placeholder. No external deps.

- [ ] Test: output contains the model names, each template name, and a "differs"/"only in" marker; is valid single `<html>`.
- [ ] Implement; tests pass; commit `feat(core): html report writer`.

---

## PHASE B — Revit layer (compile-only here; runtime test in Revit later)

### Task 11: ICategoryExtractor + ViewTemplateExtractor

**Files:** Create `src/RVTuk.Revit/Extraction/{ICategoryExtractor,ViewTemplateExtractor}.cs`.

**Produces:** `interface ICategoryExtractor { string CategoryId; CategorySnapshot Extract(Document doc); }`; `ViewTemplateExtractor` walks `new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v=>v.IsTemplate)`, builds `ViewTemplateDto` (Name, UniqueId, ViewType.ToString(), controlled params via `GetTemplateParameterIds`/`GetNonControlledTemplateParameterIds`, selected `Settings`, `FilterNames` via `GetFilters()`+`GetName`, `CategoryOverridesHash` = SHA1 of a stable string of per-category overrides). Wrap each template in try/catch; skip+log on failure. Apply version-aware `#if` for any 2023→2025 API shifts.

- [ ] Implement; build Release2024 + Release2025 (compile only).
- [ ] Commit `feat(revit): view template extractor`.

### Task 12: ExternalEvent handlers

**Files:** Create `src/RVTuk.Revit/ExternalEvents/{CaptureSnapshotEventHandler,OpenModelSnapshotEventHandler}.cs`.

**Produces:** Capture handler (active/open doc → run registered extractors → produce snapshots, ping-pong via `ManualResetEventSlim` like `IndexingExternalEventHandler`). OpenModel handler: `Application.OpenDocumentFile` with `OpenOptions` (detach preserve worksets, `CloseAllWorksets`), dialog/failure suppression, version guard (skip files newer than running Revit), extract, `Document.Close(false)` in `finally`. Timeout on the wait.

- [ ] Implement; build; commit `feat(revit): snapshot external-event handlers`.

### Task 13: Ribbon command + button

**Files:** Create `src/RVTuk.Revit/Commands/CompareProjectsCommand.cs`; modify `src/RVTuk.Revit/Application.cs` (add PushButton to existing `RVTuk` panel; register the two ExternalEvents in OnStartup).

- [ ] Implement; build all configs; commit `feat(revit): comparator ribbon button`.

---

## PHASE C — UI layer (compile-only run here)

### Task 14: ViewModels

**Files:** Create `src/LibraryBrowser/RVTuk.UI/ViewModels/{ComparatorViewModel,CategoryViewModelBase,ViewTemplatesCategoryViewModel,PlaceholderCategoryViewModel,ItemDiffViewModel,FieldDiffViewModel,DecisionOption}.cs`.

**Produces:** `ComparatorViewModel` (delegate-injected Revit calls: `Func<...>` for capture/open/compare; mode, source A/B selection, category list, ObservableCollection of ItemDiffViewModel). Category VMs render the rail; ViewTemplates VM holds roster + selected detail. Reuse `ViewModelBase`, `RelayCommand`.

- [ ] Implement; build net8 + net48; commit `feat(ui): comparator view models`.

### Task 15: ComparatorWindow

**Files:** Create `src/LibraryBrowser/RVTuk.UI/Views/ComparatorWindow.xaml(.cs)`.

**Produces:** Window per ui-ux.md: source bar, category rail, roster DataGrid (status/A/scoreA/B/scoreB/action), detail tabs (Overview/V-G/Filters/All), status bar, export. Reuse DarkTheme; add diff-semantic brushes to `Themes/DarkTheme.xaml`.

- [ ] Implement; build; commit `feat(ui): comparator window`.

### Task 16: Wire delegates

**Files:** Modify `CompareProjectsCommand.cs` to construct `ComparatorViewModel` with delegates bound to the ExternalEvent handlers + `SnapshotRepository`; show the window (modeless, Topmost, like Family Browser).

- [ ] Implement; build all three configs; commit `feat: wire comparator UI to Revit + storage`.

---

## Done-when
- All Phase A unit tests pass (`dotnet test`).
- `dotnet build RVTuk.sln -c Release2024` and `-c Release2025` succeed (0 errors).
- Branch `project-comparator` pushed.
- In-Revit runtime test handed to the user (capture two models, compare view templates, accept into Standard, export HTML).

## Self-review notes
- Spec coverage: snapshot model (T3,4,9), engine/diff (T2,5,6,7), editable Standard (T8), report-only HTML (T10), Revit extract/open safeguards (T11,12), ribbon (T13), UI (T14–16). Families/other categories intentionally out of v1.
- Write-back seam: `FutureApplyToken` defined (T2) but unused; no applier — satisfied.
- net48 serialization verified by Release2024 compile + DataContract attrs (T3,4).
