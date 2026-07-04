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
- The dimension references every `Wall` whose centerline **transversally crosses** the line
  (a true interior intersection, not just touching at an endpoint/corner — see Design), using
  each wall's two side faces (`HostObjectUtils.GetSideFaces`) so measurements read face-to-face,
  in the order the walls appear along the line.
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
- Curved (arc/spline) walls — only walls with a straight `LocationCurve` are considered; curved
  walls are silently excluded from crossing detection (Revit can't linear-dimension a curved
  face against a straight line anyway).
- Reverse tracking from dimension back to line — deleting a reference line orphans the
  dimension it produced permanently (nothing notices or cleans it up). Accepted for v1.

## Design

### Component 1 — WallCrossingFinder (project: Core)
Pure logic, no Revit types, and **strictly 2D**: the Revit side projects both the reference
line's endpoints and every wall's centerline endpoints onto the active view's plane (drop Z —
wall centerlines sit at the wall's base elevation while the detail line sits on the view's
sketch plane, so a naive 3D test would find zero crossings for everything) before calling in.
Given the reference line's 2D start/end points and a list of walls' 2D centerline start/end
points (plain `XyPoint`-style value types defined in Core), determines which walls' centerlines
**transversally cross** the line and returns them **ordered by their crossing point's projection
along the line's direction** (start → end).

"Transversally cross" is deliberately narrower than "intersects," to make T-junctions and
collinear/near-parallel walls unambiguous:
- The crossing point must fall strictly **inside** both the wall's centerline segment and the
  reference line's bounded segment (excludes walls that only touch at an endpoint/corner, e.g. a
  partition butting into the reference line at a T — those are excluded, not dimensioned).
- A wall whose centerline direction is within **5°** of parallel to the reference line's
  direction is excluded outright, regardless of where it would otherwise cross. This covers both
  the truly collinear case (no single crossing point exists) and the near-parallel/oblique case
  (the resulting side-face references would be near-degenerate and destabilize the dimension).
- Both exclusions apply per-wall, not per-line: a line can still produce a valid dimension from
  its other crossings even if one candidate wall is excluded this way.

This is the unit-testable core of the "which walls, in what order" question — exercised with
plain coordinate data in `tests/RVTuk.Core.Tests`, no live Revit session needed. Mirrors the
`RVTuk.Core.NeoProperties.ParameterOrderer` pattern: Revit-side code extracts plain data, Core
does the pure reasoning, Revit-side code turns the pure result back into Revit API calls. The
straight-wall-only guarantee (see Non-goals) is enforced by the Revit side filtering out any
`Wall` whose `LocationCurve.Curve` isn't a `Line` before it ever reaches this component — Core
never has to know curved walls exist.

### Component 2 — DimensionLineStyle (project: Revit)
Ensures the `"Dimensions_Line"` line subcategory exists under `OST_Lines`, creating it if
missing (called at the start of every run — idempotent, cheap to check). Also exposes a
predicate to test whether a given `CurveElement`'s line style is this subcategory, used to find
reference lines in the active view.

### Component 3 — AutoDimensionTracker (project: Revit)
Wraps the Extensible Storage `Schema`/`Entity` read/write of "the `ElementId` of the `Dimension`
this line last produced" on a detail line element. Extensible Storage was chosen (over a visible
shared parameter) so the bookkeeping is invisible to users and travels automatically with the
line through copy/move operations — nothing for a user to accidentally edit or break. That same
copy-travels-with-it behavior means a copied reference line arrives already "pointing at" the
original line's dimension, so the tracker validates before ever deleting:

- `TryGetTrackedDimension(line, activeView) : Dimension?` — reads the stored `ElementId` (if
  any), resolves it via `doc.GetElement`, and returns it **only if** the element still exists,
  is actually a `Dimension`, and its `OwnerViewId` equals the active view's id. Any other case
  (dangling id after a manual delete, wrong element type, or a dimension that belongs to a
  *different* view because the line was copied there) returns null and does nothing — in
  particular, it never deletes a dimension belonging to a view other than the one currently
  being processed.
- `SetTrackedDimension(line, dimensionId)` — overwrites the stored id unconditionally. Because
  this always runs after a fresh `NewDimension` call in the *current* view, a previously-wrong
  entry (e.g. from a cross-view copy) self-heals the next time that line's view is processed.

Two operations only: `TryGetTrackedDimension` and `SetTrackedDimension`.

### Component 4 — AutoDimensionsCommand (project: Revit)
The ribbon command (`IExternalCommand`, `TransactionMode.Manual`, one transaction for the whole
run). Orchestrates, for the active view:
1. `DimensionLineStyle.EnsureExists(doc)`.
2. Collect `DetailLine`s in the active view whose line style matches (`DimensionLineStyle`). If
   none are found, the end-of-run summary says so explicitly (see step 5) rather than a bare
   "0 created, 0 skipped" — distinguishing "you haven't drawn any reference lines yet" from "your
   reference lines all failed."
3. Collect `Wall`s visible in the active view once, reused across all reference lines, filtered
   to those whose `LocationCurve.Curve` is a `Line` (straight walls only — see Component 1).
4. Per reference line:
   a. **Always first**: call `AutoDimensionTracker.TryGetTrackedDimension(line, activeView)`; if
      it returns a dimension, delete it. This happens *before* and *independently of* whether
      the line currently has any crossings — a line whose walls were since deleted or moved away
      must still have its stale dimension removed, not just skipped.
   b. Project the line and each candidate wall's centerline to the view plane (drop Z) → call
      `WallCrossingFinder.FindCrossings` (Core).
   c. If zero crossing walls: tally as skipped, move to the next line (the delete in step 4a
      already handled cleanup).
   d. Otherwise, for each crossing wall, resolve its two side faces via
      `HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)` and `.Interior`; take the
      **first** `Reference` from each list (a wall can theoretically report more than one per
      side — e.g. a profile-edited or embedded-condition wall); if either list is empty for a
      given wall, exclude that wall from this line's dimension (tally the exclusion, don't fail
      the whole line) rather than throwing.
   e. Build the ordered `ReferenceArray` from the surviving walls → `doc.Create.NewDimension(view,
      line.GeometryCurve as Line, referenceArray)` → `AutoDimensionTracker.SetTrackedDimension`
      with the new dimension's id → tally created.
5. After all lines: one `TaskDialog` summary — e.g. "3 dimensions created, 1 line skipped: no
   walls found," or, if no reference lines were found at all, "No Dimensions_Line lines found —
   the line style now exists in this project; draw reference lines and run again."

A single line's failure (caught per-line, not per-run) is tallied as skipped and does not abort
processing the rest — but the whole command still runs inside one transaction, so a hard
Revit-level failure rolls back cleanly rather than leaving partial dimensions.

## Data flow

```
Ribbon click → AutoDimensionsCommand.Execute (one transaction)
  → DimensionLineStyle.EnsureExists
  → collect Dimensions_Line DetailLines + straight Walls in active view
  → per line:
      AutoDimensionTracker: delete previously-tracked Dimension, if any owned by this view
      → project line + wall centerlines to view plane (drop Z, plain data)
      → WallCrossingFinder.FindCrossings (Core, pure — excludes T-junctions & near-parallel walls)
      → zero crossings? tally skipped, next line
      → ordered crossing walls → HostObjectUtils.GetSideFaces (first ref per side, exclude wall if empty)
      → ReferenceArray → NewDimension → AutoDimensionTracker.SetTrackedDimension(new id)
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

- **Unit (Core):** `WallCrossingFinderTests` — walls fully (transversally) crossing the line in
  various orders (verify sorted-by-projection output); a wall touching only at an
  endpoint/corner (T-junction — must be **excluded**); a wall collinear with the line (must be
  **excluded**); a wall crossing at a shallow oblique angle within the 5° tolerance (must be
  **excluded**) and just outside it (must be **included**); zero crossing walls (empty result);
  walls on both sides of the line's start/end bounds (excluded if the crossing point falls
  outside the line's bounded segment).
- **Manual in-Revit:**
  1. Draw a `Dimensions_Line` detail line crossing 2–3 walls; run the command; confirm one
     dimension appears exactly on the line, referencing the correct wall faces in left-to-right
     (or start-to-end) order.
  2. Move one of the crossed walls; re-run; confirm the old dimension is gone and a new,
     correctly-updated one appears (no duplicate).
  3. Delete (or move away) all the walls a line previously crossed, then re-run; confirm the
     stale dimension is removed and the line is tallied as skipped (this is the delete-must-not-
     depend-on-crossings-existing case).
  4. Draw a `Dimensions_Line` crossing zero walls; run; confirm no dimension is created and the
     summary reports it as skipped.
  5. Run in a project that doesn't yet have the `Dimensions_Line` line style; confirm it's
     created automatically and the run still succeeds.
  6. Run with multiple reference lines in the same view at once; confirm each gets its own
     correct dimension and the summary tallies all of them.
  7. Copy a reference line (with its tracked dimension) into a **different view**; run the
     command in that new view; confirm it creates its own new dimension there and does **not**
     delete the dimension still owned by the original view.
  8. Manually delete a dimension the tool previously created, leaving its line's tracking entry
     dangling; re-run; confirm no error, and a fresh dimension is created and tracked normally.

## Open questions
- None blocking — object-type expansion (openings, columns, grids) and multi-view/whole-project
  runs are explicitly deferred, not undecided.
