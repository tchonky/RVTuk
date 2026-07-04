# Auto Dimensions — Design

**Date:** 2026-07-04
**Status:** Approved
**Area:** Productivity
**Branch:** Adimensions

## Problem / motivation

Placing dimensions in Revit is tedious and fragile for inexperienced users: overlapping
references make it easy to pick the wrong face, and when the model changes (a wall moves,
gets added, or is deleted) existing dimensions lose their references and have to be redone by
hand. This is the first productivity tool absorbing the older, separate `KKimensions` /
`DimensionPropagator` project (see `VISION.md`) — this spec supersedes that project rather than
extending it; its code was not available to review, so this design is a fresh take on the
underlying idea.

The proposed fix: let the user draw a plain detail line as a positional "reference" for where a
dimension should go. A command then finds every wall crossing that line and builds a dimension
automatically, positioned exactly on the line. Re-running after a model change simply replaces
the old dimension — no manual re-picking of references ever needed.

## Goals
- A ribbon command that, for the active view, finds every detail line drawn on a dedicated
  `"Dimensions_Line"` line style and creates one Revit `Dimension` per line.
- The dimension is placed exactly on the reference line's geometry (same location, same
  direction).
- The dimension references every `Wall` whose centerline crosses the line, using each wall's two
  side faces (`HostObjectUtils.GetSideFaces`) so measurements read face-to-face, in the order the
  walls appear along the line.
- Re-running on a view that already has auto-generated dimensions deletes each line's
  previously-created dimension (tracked via Extensible Storage on the line) before creating a
  fresh one — always correct after a model edit, at the cost of losing any manual tweaks made
  directly to that specific dimension.
- The `"Dimensions_Line"` line subcategory is created automatically on first run if it doesn't
  exist yet in the project — no separate setup step.
- A line crossing zero walls is skipped silently; a one-line summary at the end of the run
  reports totals (created vs. skipped).

## Non-goals
- Object types other than `Wall` (columns, grids, openings, etc.) — a future extension point,
  not built now.
- Processing every view in the project in one run — v1 is active-view-only.
- Multi-segment/chained reference lines — each reference line is a single straight `DetailLine`;
  an L-shaped run needs two separate reference lines.
- Any editing of existing manually-placed dimensions — this tool only manages dimensions it
  created itself (tracked via its own Extensible Storage entries).
- A visible/editable parameter for the tracking mechanism — the link between a line and "its"
  dimension lives in Extensible Storage, not a shared parameter.

## Design

### Component 1 — WallCrossingFinder (project: Core)
Pure logic, no Revit types: given the reference line's start/end points and a list of walls'
centerline start/end points (plain `XyzPoint`-style value types defined in Core), determines
which walls' centerlines cross the line and returns them **ordered by their crossing point's
projection along the line's direction** (start → end). This is the unit-testable core of the
"which walls, in what order" question — exercised with plain coordinate data in
`tests/RVTuk.Core.Tests`, no live Revit session needed. Mirrors the
`RVTuk.Core.NeoProperties.ParameterOrderer` pattern: Revit-side code extracts plain data, Core
does the pure reasoning, Revit-side code turns the pure result back into Revit API calls.

### Component 2 — DimensionLineStyle (project: Revit)
Ensures the `"Dimensions_Line"` line subcategory exists under `OST_Lines`, creating it if
missing (called at the start of every run — idempotent, cheap to check). Also exposes a
predicate to test whether a given `CurveElement`'s line style is this subcategory, used to find
reference lines in the active view.

### Component 3 — AutoDimensionTracker (project: Revit)
Wraps the Extensible Storage `Schema`/`Entity` read/write of "the `ElementId` of the `Dimension`
this line last produced" on a detail line element. Two operations: `TryGetTrackedDimensionId`
and `SetTrackedDimensionId`. Extensible Storage was chosen (over a visible shared parameter) so
the bookkeeping is invisible to users and travels automatically with the line through copy/move
operations — nothing for a user to accidentally edit or break.

### Component 4 — AutoDimensionsCommand (project: Revit)
The ribbon command (`IExternalCommand`, `TransactionMode.Manual`, one transaction for the whole
run). Orchestrates, for the active view:
1. `DimensionLineStyle.EnsureExists(doc)`.
2. Collect `DetailLine`s in the active view whose line style matches (`DimensionLineStyle`).
3. Collect `Wall`s visible in the active view once, reused across all reference lines.
4. Per reference line: extract each wall's centerline endpoints (plain doubles) → call
   `WallCrossingFinder.FindCrossings` (Core) → for each crossing wall, resolve two side-face
   `Reference`s via `HostObjectUtils.GetSideFaces` (both `ShellLayerType.Exterior` and
   `.Interior`) → if zero crossing walls, skip and tally; otherwise build the ordered
   `ReferenceArray` → look up and delete any previously-tracked `Dimension` via
   `AutoDimensionTracker` → `doc.Create.NewDimension(view, line.GeometryCurve as Line,
   referenceArray)` → `AutoDimensionTracker.SetTrackedDimensionId` with the new dimension's id →
   tally created.
5. After all lines: one `TaskDialog` summary, e.g. "3 dimensions created, 1 line skipped: no
   walls found."

A single line's failure (caught per-line, not per-run) is tallied as skipped and does not abort
processing the rest — but the whole command still runs inside one transaction, so a hard
Revit-level failure rolls back cleanly rather than leaving partial dimensions.

## Data flow

```
Ribbon click → AutoDimensionsCommand.Execute (one transaction)
  → DimensionLineStyle.EnsureExists
  → collect Dimensions_Line DetailLines + Walls in active view
  → per line:
      wall centerlines (plain data) → WallCrossingFinder.FindCrossings (Core, pure)
      → ordered crossing walls → HostObjectUtils.GetSideFaces per wall → ReferenceArray
      → AutoDimensionTracker: delete old Dimension (if tracked) → NewDimension → track new id
  → TaskDialog summary
```

## Threading

Everything runs synchronously on the Revit UI thread inside `IExternalCommand.Execute` — Revit
already invokes commands on the main thread, so no `ExternalEvent`/`ManualResetEventSlim`
ping-pong is needed (unlike the Family Browser's background-triggered actions). This matches the
existing `SetupRishuiZaminParamsCommand`/`AreaCalcCommand` pattern of direct, synchronous ribbon
commands that do their own transaction.

## Persistence (if any)

No SQLite involvement. The only persisted state is the Extensible Storage entry on each
`Dimensions_Line` detail line (one `Schema` with a single `ElementId` field: the last dimension
it produced). This lives in the Revit model itself, not the RVTuk database.

## Error handling

- Per-reference-line try/catch: a line with zero crossings, an unreadable wall, or a face-lookup
  failure is skipped and counted in the summary — never aborts the run.
- The whole run is one transaction: any unhandled/fatal error rolls back every change made so
  far, so the model is never left with a half-updated set of dimensions.
- Never touches manually-created dimensions — only ones whose `ElementId` is found via this
  tool's own Extensible Storage tracking on a given line.

## Testing

- **Unit (Core):** `WallCrossingFinderTests` — walls fully crossing the line in various orders
  (verify sorted-by-projection output), a wall touching only at an endpoint/corner, zero
  crossing walls (empty result), walls on both sides of the line's start/end bounds (excluded if
  the crossing point falls outside the line's bounded segment).
- **Manual in-Revit:**
  1. Draw a `Dimensions_Line` detail line crossing 2–3 walls; run the command; confirm one
     dimension appears exactly on the line, referencing the correct wall faces in left-to-right
     (or start-to-end) order.
  2. Move one of the crossed walls; re-run; confirm the old dimension is gone and a new,
     correctly-updated one appears (no duplicate).
  3. Draw a `Dimensions_Line` crossing zero walls; run; confirm no dimension is created and the
     summary reports it as skipped.
  4. Run in a project that doesn't yet have the `Dimensions_Line` line style; confirm it's
     created automatically and the run still succeeds.
  5. Run with multiple reference lines in the same view at once; confirm each gets its own
     correct dimension and the summary tallies all of them.

## Open questions
- None blocking — object-type expansion (openings, columns, grids) and multi-view/whole-project
  runs are explicitly deferred, not undecided.
