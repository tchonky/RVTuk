# Auto Dimensions — Scope Pane — Design

**Date:** 2026-07-23
**Status:** Approved
**Area:** Productivity

## Problem / motivation

The existing Auto Dimensions command (see
[2026-07-04-auto-dimensions-design.md](2026-07-04-auto-dimensions-design.md)) is active-view-only
and wall-only: the user must draw `Dimensions_Line` reference lines and run the ribbon command
separately in every view that needs dimensions, and only `Wall` centerlines are ever considered as
crossing candidates. In practice, reference lines are usually drawn once per level (in one plan
view) and the same dimensions are wanted on every other view of that level (different phases,
work-in-progress vs. as-built, etc.) — and openings (doors, windows) are dimensioned as often as
walls.

This spec adds a dockable pane that lets the user choose, per project, which categories
participate as dimension references and which views of each level should receive the replicated
dimensions, then fans the existing per-line logic out across all of them from a single "Create
Dimensions" action. It supersedes nothing — the underlying per-line crossing/reference/cleanup
logic from the 2026-07-04 spec is reused, generalized to run across more than one view and more
than one category.

## Goals

- A dockable pane (registered the same way as the Neo Properties pane) containing:
  - A category checklist: **Walls**, **Doors**, **Windows** (checked/wired up in v1); **Ceilings**
    and **Floors** are shown disabled with a "coming in v2" tooltip — visible for layout
    continuity, not functional yet.
  - A scrollable tree of **Levels → Views**, populated from the live document.
  - A **Create Dimensions** button, always visible — pinned below the scrollable tree, not part of
    the scrolling region, since the tree can grow long and the pane's height is whatever the user's
    screen/dock leaves it.
- On open (and on demand thereafter), the pane discovers, for every `Level` in the project,
  whether any view owned by that level contains at least one `Dimensions_Line` detail line — the
  same `OwnerViewId`-based, view-template-visibility-independent lookup the existing command uses
  (a hidden line style must still count). A level with such a view has that view become its
  **reference view** for this run; the level appears in the tree with every view under that level
  listed as a checkable target, the reference view included and pre-checked by default. A level
  with no reference view appears in red with no children — nothing to select, nothing to run,
  matching the "RR" case from the pane mockup.
- Checking Doors and/or Windows adds their instances to the reference set precisely where their
  opening crosses a `Dimensions_Line`, using
  `FamilyInstance.GetReferences(FamilyInstanceReferenceType.Left)` /
  `.Right` for the two references — in place of a wall's two side faces.
- Clicking **Create Dimensions** runs, for every selected level, for every selected view of that
  level, the same per-line pipeline as today (crossing detection → reference resolution →
  zero-segment cleanup → tracking) against that reference view's lines — just repeated across
  (line × selected view) pairs instead of once per active view.
- The category checklist and the tree's checked views persist per-project (Extensible Storage), so
  reopening the pane on the same model restores the selection from the last successful run.

## Non-goals

- Ceilings/Floors reference resolution — both are host objects needing top/bottom-face logic
  (`HostObjectUtils.GetTopFaces`/`GetBottomFaces`), structurally unlike the family-instance
  Left/Right approach used for doors/windows. Deferred to v2 entirely; their checkboxes are inert
  in v1.
- Multiple reference views per level: if more than one view under a level happens to contain
  `Dimensions_Line` lines, the first found (by `ElementId` order — arbitrary but deterministic) is
  used as that level's reference view. Not expected in normal use (the convention is one reference
  view per level) and isn't specially surfaced beyond that.
- Views with no `Level` (3D views, drafting views, schedules, sheets) — excluded entirely from
  discovery; never candidates as either reference or target views.
- Any change to how reference lines are authored — line placement stays entirely manual, exactly
  as before. This pane only changes how many views a level's lines fan out to.
- A "select all / clear all" convenience control for the tree or the category list — not requested;
  can be added later if it turns out to be needed.
- Reworking the existing single-view ribbon command — it stays as-is for a quick single-view rerun;
  the pane is an additive, alternative entry point.

## Design

### Component 1 — LevelScope (project: Core)
Pure data, no Revit types: `LevelScope(LevelId, LevelName, HasReferenceView, ReferenceViewId?,
IReadOnlyList<ViewInfo> Views)` where `ViewInfo` is `(ViewId, ViewName)`. Mirrors the
`ProjectFamilyInfo` pattern from Family Browser — Revit-side discovery extracts plain data, Core
(and the UI layer, which depends only on Core) never touches Revit types.

### Component 2 — LevelDiscoveryEventHandler (project: Revit)
An `IExternalEventHandler` following `GetProjectFamiliesEventHandler`'s
`ManualResetEventSlim`/`Result` pattern. Groups every `ViewPlan` by `GenLevel`, and for each level
checks (via the existing document-wide `CurveElement`/`OwnerViewId` lookup) whether any of its
views owns a `Dimensions_Line` line. Returns `IReadOnlyList<LevelScope>`.

### Component 3 — Candidate crossing reuse (projects: Core unchanged, Revit new)
`WallCrossingFinder` (Core) is reused unchanged for every category — no new Core crossing
algorithm. A new Revit-side helper converts each checked-category candidate into the same
`WallCandidate` 2D-segment shape the finder already expects:
- **Wall**: unchanged — the wall's own centerline endpoints.
- **Door/Window** (`FamilyInstance`): a segment of length = the instance's `Width` parameter,
  centered on its location point, oriented along its host wall's direction (not the instance's own
  `FacingOrientation`, to sidestep flip/orientation quirks).

`AutoDimensionsCommand`'s per-line loop builds one combined candidate list (walls always;
doors/windows only if their checkbox is checked) tagged with each candidate's originating category
and element, runs `WallCrossingFinder.FindCrossingIndices` once, then dispatches reference
resolution per matched candidate's category: `HostObjectUtils.GetSideFaces` for `Wall`,
`FamilyInstance.GetReferences(FamilyInstanceReferenceType.Left/.Right)` for Door/Window. Ordering
along the line, zero-segment cleanup, and dimension creation stay exactly as today, just operating
on the merged, category-tagged reference list.

### Component 4 — AutoDimensionTracker (project: Revit, changed)
Changes from a single `long` (`ElementId`) field per line to a `Map<long,long>` field
(target-view-id → dimension-id), since one reference line can now produce several dimensions
across several views instead of exactly one. `TryGetTrackedDimension`/`SetTrackedDimension` both
gain a `viewId` parameter and read/write into the map entry for that view rather than a scalar
field. This is a breaking Extensible Storage schema change; acceptable because the tool is
unreleased (hidden behind `RegisterUnreleasedTools`) — no live installs to migrate.

### Component 5 — Selection persistence (project: Revit, new)
A new Extensible Storage schema on the document's `ProjectInformation` element: the checked
category set (a simple `int` field, one bit per category) and the checked view ids (an
`AddArrayField<long>`). Written in the same transaction as a successful **Create Dimensions**
run — not on every checkbox toggle, so no extra transaction is needed just to reflect in-progress
UI state. If the user changes selections but never clicks Create Dimensions, the persisted state
stays whatever the last successful run left it as. Read once on pane open to seed the ViewModel's
initial checked state (defaulting, for a project with no persisted selection yet, to Walls + Doors
+ Windows checked and each level's reference view pre-checked).

### Component 6 — AutoDimensionsPaneViewModel / View (projects: UI)
`AutoDimensionsPaneViewModel` (depends on Core only) exposes an `ObservableCollection` of category
options (`Walls`/`Doors`/`Windows` toggleable, `Ceilings`/`Floors` present but `IsEnabled = false`)
and an `ObservableCollection<LevelNode>` (each holding `ViewNode`s with an `IsChecked` bool),
populated from `LevelScope` data handed in by the Revit host. Exposes a `CreateDimensionsCommand`
(`RelayCommand`) the host wires to the actual Revit-side work via a plain delegate, matching the
existing UI-depends-on-Core-only / Revit-wires-delegates architecture. `AutoDimensionsPaneView`
(XAML) implements the pane mockup layout: header, category checklist, scrollable level/view tree
(`Border`+`ScrollViewer`, not part of the pinned footer), footer `Button` docked to the bottom.

### Component 7 — AutoDimensionsPaneProvider (project: Revit, new)
Registers the pane the same way `NeoPropertiesPaneProvider` does — `IDockablePaneProvider`, a
stable `DockablePaneId` guid, `DockPosition.Tabbed` grouped alongside the Neo Properties pane (both
are utility panes; tabbing avoids adding two new permanent dock slots). A ribbon command
(mirroring `NeoPropertiesCommand`) shows/activates the pane; its content triggers the two
`ExternalEvent`s (discovery on show, create-on-click) internally rather than needing a second
ribbon entry point.

## Data flow

```
Pane shown → LevelDiscoveryEventHandler (ExternalEvent) → IReadOnlyList<LevelScope>
  → read persisted selection (Extensible Storage on ProjectInformation), or defaults
  → populate categories + level/view tree (RR-style levels: red, no children)
→ user adjusts checkboxes
→ click Create Dimensions → ExternalEvent → one transaction
    for each selected level:
      for each selected view of that level:
        for each Dimensions_Line in the level's reference view:
          AutoDimensionTracker: delete previously-tracked Dimension for (line, this view), if any
          → build combined candidates (walls always; doors/windows per checked categories)
            from elements visible in *this target view*
          → WallCrossingFinder.FindCrossingIndices (Core, unchanged)
          → per matched candidate: resolve references by category
            (GetSideFaces for Wall, GetReferences(Left/Right) for Door/Window)
          → ReferenceArray → NewDimension(targetView, ...)
          → zero-length segment cleanup (unchanged, reused)
          → AutoDimensionTracker.SetTrackedDimension(line, targetViewId, dimension.Id)
    → persist current category + view selection to ProjectInformation
  → TaskDialog summary (per-level/per-view tallies + levels skipped for missing reference view)
```

## Threading

The pane is a WPF `UserControl` that can trigger work at any time from user interaction, not a
ribbon click that Revit already marshals onto its main thread — so, unlike today's single-view
command, both discovery and Create Dimensions go through `ExternalEvent` +
`ManualResetEventSlim`, following the `GetProjectFamiliesEventHandler` pattern used by the Family
Browser. The pane's button click handler never calls the Revit API directly.

## Persistence (schema)

- New Extensible Storage schema on `ProjectInformation`: one `int` bitmask field for the checked
  category set, one `AddArrayField<long>` for checked view ids.
- `AutoDimensionTracker`'s existing schema changes from a single `long` field to a `Map<long,long>`
  field (target-view-id → dimension-id) — see Component 4.

## Error handling

- A level with no reference view is excluded from selection (shown red, no children) and tallied
  in the run summary as "N level(s) skipped — no reference view."
- A (line, view) combination that produces zero crossings or no resolvable references for the
  checked categories is skipped and tallied, exactly as today — never aborts the run.
- The whole run stays one transaction: a hard failure rolls back every change made so far, same
  guarantee as today's command.

## Testing

- **Unit (Core):** no changes needed to `WallCrossingFinderTests` — the finder itself is reused
  unchanged. Any new pure logic (e.g. a persisted-selection round-trip helper, if one emerges
  during implementation) gets tests in `tests/RVTuk.Core.Tests/AutoDimensions`.
- The family-instance-to-`WallCandidate` conversion (Width parameter + host wall direction) is
  Revit-side geometry and can only be verified manually in-Revit, consistent with the rest of this
  tool's Revit-side code.
- **Manual in-Revit** (extends the 10-point list in the 2026-07-04 spec):
  1. Open the pane in a project with 2+ levels, some with reference views drawn and one with none
     — confirm the tree matches (the no-reference-view level in red, no children).
  2. Check only a level's reference view, click Create Dimensions — behavior matches today's
     single-view command exactly.
  3. Check the reference view plus one more view of the same level — confirm dimensions appear in
     both, referencing that view's own visible elements.
  4. Toggle Doors/Windows on, re-run — confirm door/window Left/Right references appear correctly
     ordered alongside wall faces along the line.
  5. Close and reopen the pane (or the project) — confirm category and view selections are
     restored from the last successful run, not reset to defaults.
  6. Re-run after moving a wall or door — confirm old dimensions in every previously-targeted view
     are replaced, not duplicated (validates the tracker's per-view map).
  7. Confirm Ceilings/Floors checkboxes are visibly disabled and do nothing when clicked.

## Open questions

- None blocking. Ceilings/Floors reference-resolution logic is explicitly deferred to v2.
