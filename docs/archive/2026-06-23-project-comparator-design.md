# Project Comparator / Template Builder — Design Spec

**Date:** 2026-06-23
**Status:** Approved design (v1), pending written-spec review
**Feature owner:** (solo dev / BIM manager)
**Process:** Multi-specialist brainstorm (BIM Manager, Software Architect, Product Developer, UI/UX) synthesized by PM.

Companion documents:
- [Guidelines (BIM domain rules)](../../comparator/guidelines.md)
- [Features (product / roadmap)](../../comparator/features.md)
- [UI/UX spec](../../comparator/ui-ux.md)

---

## 1. Summary

A new RVTuk ribbon button (peer to the Family Browser, on the same panel) that compares the **firm-standard content** of two Revit models and produces a readable, actionable report. The same engine serves two framings:

- **Build Template** — harvest the best-of-breed standards from several projects into a curated master **Standard**.
- **Audit Project** — find where a live project has drifted from the Standard.

**v1 is report-only with respect to live Revit models** — it never writes to a `.rvt`/`.rte`. v1 fully specifies one category, **View Templates**. All other categories are scaffolded in the architecture but not implemented.

---

## 2. Locked decisions (do not relitigate)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **v1 is report-only w.r.t. Revit models.** No write-back to `.rvt`/`.rte`. | Trust + scope. Write-back is a separate later phase. |
| D2 | **Both workflows, phased:** template-building first, then project audit. | Same engine, asymmetric report framing. |
| D3 | **View Templates is the one deeply-specified v1 category.** Others framework-level only. | Highest-leverage category; encodes the most manual labor. |
| D4 | **Snapshot model.** Extract a serializable "standards snapshot" per model; compare snapshots, not live files. | Compare-without-both-open, baselines/history, bounded memory. Mirrors the existing Family Indexer. |
| D5 | **Never assert "newer" as fact.** Recommendations rank on completeness + validity + the user's explicit canonical choice; capture-date is shown only as honest provenance. | Revit exposes no reliable per-element modified timestamp. |
| D6 | **The Standard is an editable, versioned artifact** built by accepting items (with their dependency closure) from project snapshots. Editing the Standard is a Core-only data operation; it never touches Revit. | Lets you build the master template inside the tool today; becomes the source that write-back later materializes. |
| D7 | **Families are not a comparison category in v1.** The category rail links out to the existing Family Browser; when eventually added, it adapts the existing family index, never re-scans `.rfa`. | No duplication. |

---

## 3. Core concepts

### 3.1 Snapshot
A **standards snapshot** is a serializable, Revit-free, per-model capture of one or more categories' standards content (e.g. all view templates and their settings). It is produced on Revit's main thread (it reads the API) but is shaped as pure-Core DTOs so it crosses the thread boundary, serializes, and diffs with no Revit dependency.

- **Captured project/template snapshots are immutable baselines**, timestamped at capture.
- Sources: the **active document** (read in-process, fast), another **open document**, or a **closed `.rvt`/`.rte`** background-opened detached/read-only, extracted, and immediately closed.

### 3.2 The Standard (editable master)
A **Standard** is a *mutable* snapshot the user curates — the master template, built inside the tool:

- **Accept-into-Standard**: copy a captured item (e.g. a view template) from a project snapshot into the Standard, **with its dependency closure** (the filters it uses → their parameters/shared-param GUIDs → line/fill patterns/subcategories).
- **Provenance**: every item (and, where merged, every field) records which source snapshot it came from — "Floor Plan–Working ← Project Gamma (2026-06-23); Structural filter ← Project Alpha".
- **Versioning**: the Standard carries a revision counter + changelog of accepted items; full diffable history with rollback is a Should (not v1-Must).
- **Conflict handling**: accepting an item whose name already exists, or a dependency whose name matches but whose definition differs, raises a conflict the user resolves (replace / keep / — field-merge is a Should).
- The Standard is comparable like any snapshot, so **Audit Project** = compare a live project snapshot against the Standard.

> **Two distinct "write" concepts, kept separate:**
> 1. **Editing the Standard** — pure-Core data mutation, safe, in v1.
> 2. **Writing into Revit models** — Revit transactions, risky, deferred to the write-back phase. The Standard is the bridge between them.

### 3.3 Comparison
For a category, the engine pairs items from snapshot A and snapshot B by a category-defined **matching key**, then produces a generic diff (Added / Removed / Changed with field-level detail) plus per-item **completeness** and provenance, and a recommendation the user can override.

---

## 4. View Templates (the v1 category)

See [guidelines.md](../../comparator/guidelines.md) for the full domain rules. Essentials the implementation must honor:

- A view template is a `View` with `IsTemplate == true`. **Match within view-type buckets** (`ViewType`); a floor-plan and a section template with the same name are not comparable.
- **Inclusion-awareness is the #1 correctness rule.** A template only governs the parameters whose "include" checkbox is ticked (`GetNonControlledTemplateParameterIds()` / controlled set). Always read *which fields are controlled* before reading values, or every uncontrolled field reads as a false difference. Each field classifies as: Identical / Differs(value) / A-only-control / B-only-control / Both-uncontrolled(ignore) / Unmatched-ref.
- Compared content: scale, detail level, discipline, display/visual style, phase + phase filter, view range (plan), V/G overrides **per category & subcategory** (visibility, projection/cut line + fill, transparency, halftone, detail-level override), and the ordered **filter** list with per-filter enabled/visibility/overrides.
- **Completeness, not fake recency.** Score = count of *meaningfully* controlled fields, weighted (V/G overrides & filters high; view range/phase/detail/scale medium; render cosmetics low). Present completeness as "controls N of M, here's which"; never auto-decide on completeness alone.
- **Dependency manifest is a v1 Must.** Each differing/recommended template lists everything that must travel with it and flags anything that won't resolve in the target. v1 also captures a lightweight **shared-parameter inventory** (GUID-keyed) as dependency data so "portable?" flags are honest.
- **Rename detection (Should):** a content-fingerprint secondary match surfaces "these two unmatched templates are ~90% identical — possible rename?" as a suggestion, never an auto-merge.

---

## 5. Architecture

Fits the existing three-project split (no circular refs): **RVTuk.Core** (pure logic + SQLite, no Revit/UI) ← **RVTuk.UI** (WPF MVVM, Core only, Revit calls injected as delegates) ← **RVTuk.Revit** (only project referencing the Revit API; ribbon + ExternalEvent handlers).

### 5.1 Category abstraction (split to respect the dependency rule)

Extraction touches the Revit API (must live in RVTuk.Revit); match/diff/score/merge are pure Core.

```
// RVTuk.Revit  — touches Revit API, runs inside ExternalEvent.Execute
interface ICategoryExtractor { string CategoryId; CategorySnapshot Extract(Document doc); }

// RVTuk.Core  — pure data; registered in a CategoryRegistry
interface ICategoryComparer {
    string CategoryId; string DisplayName;
    CategorySnapshot LoadSnapshot(string payloadJson);
    CategoryDiffResult Compare(CategorySnapshot a, CategorySnapshot b);
}

// RVTuk.Core  — edit the Standard (safe, in-tool, v1)
interface ICategoryMerger {
    string CategoryId;
    MergeResult AcceptIntoStandard(StandardSnapshot std, CategorySnapshot source,
                                   ItemKey item, DependencyClosure deps);
}

// RVTuk.Revit — FUTURE write-back phase ONLY; not implemented in v1
interface ICategoryApplier { string CategoryId; ApplyResult Apply(Document target, ItemDiff diff); }
```

- `ComparisonEngine` (Core) looks up the `ICategoryComparer` by `CategoryId` from a `CategoryRegistry`, never references a concrete category.
- View Templates implements `ViewTemplateExtractor` (Revit), `ViewTemplateComparer` (Core), `ViewTemplateMerger` (Core).
- Adding a category = new extractor + comparer (+ merger) + register. Engine, storage, and UI tree untouched.

### 5.2 Diff model (Core DTOs)

```
enum DiffKind { Added, Removed, Changed, Unchanged }
class FieldDiff   { string FieldId; string Label; string? ValueA; string? ValueB; bool IsEqual; }
class ItemDiff    { string Key; string DisplayName; DiffKind Kind; List<FieldDiff> Fields;
                    double CompletenessA; double CompletenessB;
                    ItemProvenance? Provenance; string? FutureApplyToken /* null in v1 */; }
class CategoryDiffResult { string CategoryId; List<ItemDiff> Items; DiffSummary Summary; }
class ComparisonResult   { SnapshotRef SideA; SnapshotRef SideB; List<CategoryDiffResult> Categories; }
```

`FutureApplyToken` is the write-back seam: v1 leaves it null and registers no `ICategoryApplier`.

### 5.3 Data acquisition & threading

- All Revit-API touches (read `IsTemplate`, `OpenDocumentFile`, `Document.Close`, `FilteredElementCollector`) run inside `IExternalEventHandler.Execute` on the main thread; the background orchestration thread raises the event and blocks on `ManualResetEventSlim` (the established ping-pong, as in `IndexingExternalEventHandler`). Diff/merge/SQLite/JSON run on the background thread (pure Core). UI updates via `Dispatcher`.
- **Background-open safeguards (mandatory):** `OpenOptions` with `DetachFromCentralOption.DetachAndPreserveWorksets` (or DiscardWorksets) for centrals; `WorksetConfigurationOption.CloseAllWorksets` (we need standards, not geometry — big load/memory savings); a dialog/failure-suppression scope so a modal can't hang the headless open; a **timeout fallback** on the `ManualResetEventSlim` wait; a **version guard** refusing files newer than the running Revit (reuse the Indexer precedent); `Document.Close(false)` in a `finally`. **Never background-open two large models at once.**
- Audit case (live project vs stored Standard snapshot) opens **no** second document.

### 5.4 Storage

- New SQLite DB **`{LibraryFolder}\.Setup\RVTuk.Standards.db`**, sibling to the family DB, reusing `IndexRepository` conventions: `journal_mode=DELETE` (UNC-safe), `busy_timeout`, `synchronous=NORMAL`, `#if REVIT2024` provider aliasing, one-statement-per-`ExecuteScalar`, on-open `MigrateSchema`. Dates UTC ISO-8601 via `DbConvert.ParseUtc`.
- **Per-category payload serialized to JSON** stored in a TEXT column → the schema never changes when a category is added.

```sql
CREATE TABLE IF NOT EXISTS Snapshot (
    Id INTEGER PRIMARY KEY, SourceKind TEXT NOT NULL /* Template|Project|Standard */,
    SourceName TEXT NOT NULL, SourcePath TEXT, RevitYear INTEGER NOT NULL,
    CapturedUtc TEXT NOT NULL, SchemaVersion INTEGER NOT NULL,
    IsMutable INTEGER NOT NULL DEFAULT 0 /* 1 for the Standard */, Revision INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS SnapshotCategory (
    Id INTEGER PRIMARY KEY, SnapshotId INTEGER NOT NULL, CategoryId TEXT NOT NULL,
    PayloadJson TEXT NOT NULL, ItemCount INTEGER NOT NULL,
    FOREIGN KEY (SnapshotId) REFERENCES Snapshot(Id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS StandardChangeLog (
    Id INTEGER PRIMARY KEY, SnapshotId INTEGER NOT NULL /* the Standard */,
    CategoryId TEXT NOT NULL, ItemKey TEXT NOT NULL, Action TEXT NOT NULL /* Accept|Replace|Remove */,
    SourceSnapshotId INTEGER, ProvenanceJson TEXT, AppliedUtc TEXT NOT NULL
);
```

- **Multi-target JSON:** net48 has **no `System.Text.Json`** (clashes with Revit's preloaded assemblies — see `ConfigManager`). Snapshot DTOs must be `[DataContract]`/`[DataMember]`-annotated and round-trip through a single `SnapshotJson.Serialize/Deserialize<T>` helper with the `#if REVIT2024` split (DataContractJsonSerializer on net48 / System.Text.Json on net8). Keep DTOs flat; use lists of key/value pairs, not raw `Dictionary`; unit-test round-trip on both targets.

### 5.5 Proposed file/folder layout

```
src/RVTuk.Core/
  Comparison/
    ICategoryComparer.cs   ICategoryMerger.cs   CategoryRegistry.cs   ComparisonEngine.cs
    ViewTemplateComparer.cs   ViewTemplateMerger.cs   StandardCurator.cs
    FamiliesComparer.cs            # adapts the existing family DB (phase 2; no re-extract)
  Models/Comparison/
    CategorySnapshot.cs   ViewTemplatesSnapshot.cs   StandardSnapshot.cs
    SnapshotMeta.cs   ItemDiff.cs   FieldDiff.cs   CategoryDiffResult.cs   ComparisonResult.cs
    DiffKind.cs   ItemProvenance.cs   DependencyClosure.cs   MergeResult.cs
  Database/
    SnapshotRepository.cs          # SQLite, IndexRepository conventions
  Serialization/
    SnapshotJson.cs                # #if REVIT2024 DataContractJsonSerializer / else System.Text.Json

src/LibraryBrowser/RVTuk.UI/
  ViewModels/  ComparatorViewModel.cs  CategoryViewModelBase.cs
               ViewTemplatesCategoryViewModel.cs  PlaceholderCategoryViewModel.cs
               ItemDiffViewModel.cs  FieldDiffViewModel.cs  DecisionOption.cs
  Views/       ComparatorWindow.xaml(.cs)

src/RVTuk.Revit/
  Commands/         CompareProjectsCommand.cs        # new ribbon button
  Extraction/       ICategoryExtractor.cs  ViewTemplateExtractor.cs
  ExternalEvents/   CaptureSnapshotEventHandler.cs  OpenModelSnapshotEventHandler.cs
```

Ribbon: add a second `PushButtonData` ("Project Comparator") to the existing `RVTuk` panel in `Application.CreateRibbon`; register the two new `ExternalEvent`s in `OnStartup` alongside the existing handlers.

---

## 6. Scope (MoSCoW summary)

Full detail in [features.md](../../comparator/features.md).

- **Must (v1):** ribbon button + window; snapshot capture (active doc + background-open from disk, with all safeguards); View Templates extract → match (name within view-type) → inclusion-aware field diff; completeness scoring; **dependency manifest**; shared-parameter inventory; **build the Standard by accepting whole items + dependency closure, with provenance + replace/keep conflict handling**; Audit = compare project vs Standard; report panel + **self-contained HTML export**; Revit 2023/24/25; zero writes to Revit.
- **Should:** full V/G per-category override drill-down; field-level cherry-pick/merge into the Standard; Standard revision history + rollback; CSV export; "open in Revit" jump; rename-detection hint.
- **Won't (v1):** any write-back to Revit; other categories; batch/multi-project parallel runs; cross-session *decision* persistence (the Standard itself does persist); materializing the Standard into a real `.rte`/`.rvt`.

---

## 7. Risks & mitigations (top)

| Risk | Mitigation |
|------|-----------|
| Background-opening huge/workshared centrals (memory, time, upgrade prompts) | Detached + closed worksets; one at a time; dialog suppression; version guard; `finally`-close. **Validate on a real central early.** |
| Headless open blocks on a modal → permanent wait | Failure/dialog-suppression scope **plus** a timeout on the `ManualResetEventSlim` wait. |
| net48 `DataContractJsonSerializer` fussiness (dictionaries/polymorphism) | Flat DTOs, key/value lists, annotate everything, round-trip unit tests on both targets. |
| `UniqueId` mistaken as a cross-model key | Name-first matching; `UniqueId` only as in-session tiebreaker. |
| Snapshot-format drift as categories evolve | `SchemaVersion` column + forward-only migration (on-open, like `MigrateSchema`). |
| Curated Standard that can't be faithfully materialized later | Enforce dependency closure at accept-time; record provenance; keep the snapshot shape a faithful superset of what write-back needs. |
| Scope creep into write-back | Hard line: `FutureApplyToken` stays null; no `ICategoryApplier` registered in v1. |

---

## 8. Open items (non-blocking; default decided)

| # | Question | Default |
|---|----------|---------|
| O1 | Master Standard form: `.rte` vs `.rvt` vs blessed snapshot | All — the Standard is a snapshot; produce it from any source. Configurable snapshot path in settings. |
| O2 | V/G category-override drill-down in v1 or fast-follow | Ship "overrides differ" (hash) as Must; full per-cell table as Should. |
| O3 | Scoring rubric configurable | Fixed documented rubric in v1; firm-configurable JSON later. |
| O4 | Snapshot/Standard storage location | Alongside the family DB under `.Setup` (same concurrency model). |

---

## 9. Next step

On approval of this written spec → invoke the **writing-plans** skill to produce the implementation plan (TDD, phased per §6).
