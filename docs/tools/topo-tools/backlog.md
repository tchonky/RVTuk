# Topo Tools — backlog

## Verification

The Topo Edit session (2026-11) has not been exercised in Revit yet. Its checklist, and the
Floor ⇄ Topo checklist, are in [README.md](README.md#in-revit-checklist-topo-edit-session). The
pane-era checks (rows, selection round trip, height boxes, status line) are gone with the pane.

Settled earlier and kept for the record:

- **Ribbon placement** — answered 2026-07-30: Massing & Site is unreachable; the API only places
  custom panels on Add-Ins, on Analyze, or on a tab the add-in creates itself. For the panel,
  **do not retry**; if it ever matters, the supported move is a top-level `RVTuk` tab for the whole
  toolkit. The one exception is the Topo Tools shortcut on the Modify tab, through `Autodesk.Windows`
  (decided 2026-11-16, see the README).
- **Heights cannot be a parameter on the line** — `OST_Lines` has `AllowsBoundParameters == false`
  and `ParameterBindings.Insert` returns false (the old setup silently discarded that return value).
  That is why the height is a text note.
- **The line style creates itself** — no setup button; a transaction is opened only when something
  is missing.
- **A first run, idempotence, a moved line, a deleted line, a section view** all passed in the
  pane-era tool on 2026-07-30; the apply path (sample, route, ledger, applier) is unchanged.

Still unverified by anything automated: `SlabShapeEditor.Enable()` on a toposolid that already has
points; whether a `SlabShapeVertex` wrapper stays usable after a sibling vertex is deleted
(`TopoPointApplier` snapshots the vertex list once and guards each `DeletePoint` with
`IsValidObject`); and **`AddPoints` on a coincident point** — put a line's point directly over an
existing vertex and run Update twice; the summary must not accumulate "moved by hand" counts.
(Superseded in intent by "coincident points should average" below.)

## To check: Floor ⇄ Topo converter (2026-09-29)

Work through the checklist in [README.md](README.md#in-revit-checklist-floor--topo). The two
unknowns the design leans on are cases 1 and 8: whether Revit answers `GetDefaultFamilyTypeId` for
toposolids, and whether `AddPoints` takes points on a boundary edge. Case 6 is the most important:
a round trip must not break Update's idempotence.

Also unverified, and only visible in Revit:
- whether an `Entity` read from an element stays usable for `SetEntity` after that element is
  deleted (cases 6 and 12 cover it);
- whether `Sketch.Profile` of a sloped floor includes the slope arrow (case 9);
- in 2025, split lines go through `AddSplitLine`, since `DrawSplitLine` is deprecated there, and
  edge points still use the deprecated `DrawPoint` (cases 7 and 8, 2025 column).

## Next

- **Follow-ups from the edit-session reviews (2026-11-16)**, none blocking:
  - **Spacing typed without Enter is ignored.** Clicking away from the box keeps the old value;
    commit on focus loss too, or say so in the tooltip.
  - **Height Labels and Start lack try/catch and logging.** Update and Cancel have it; the other
    two commands let an exception reach Revit's own error dialog.
  - **No icons** on the Topo Edit buttons or on Floor ⇄ Topo.
  - **A line grouped or restyled after being applied leaves its points.** Such a line is no longer
    collectable, so Update neither cleans nor reports its points on the element.
  - **The collectors could use `ElementOwnerViewFilter`** instead of filtering every `CurveElement`
    and `TextNote` in the document by owner view in C#.
  - **Renaming `_DP-Topo Height` disconnects every text** (and renaming `_DP-Topo Line` every line),
    since both are found by name. Say so in the type's name comment, or find them by a stored id.
- **Deferred from the converter's final review (2026-09-29), all minor:**
  - A flat toposolid converted to a floor comes back with shape editing enabled, because a
    toposolid always reports its corner vertices and `Replay` enables the floor's editor even for
    an empty plan. Fix: `ResetSlabShape()` on a floor target that was not enabled and got nothing.
  - `ShapeReplayPlanner.Plan` de-duplicates with a linear scan (O(n²)); bucket on an XY grid if
    survey toposolids of tens of thousands of points turn out slow (checklist 5/8b).
  - Losses the docs do not list yet: walls and columns attached to the floor detach; global
    parameter associations are lost (only the value copies); the span-direction symbol is lost.
  - A Revit or exception message ending in "." gives ".." in a skip line.
  - The spec still says `Uri.EscapeDataString` and "2 hosted families, 1 slab edge"; the code
    escapes only `% | ;` and says "hosted elements / tags or dimensions / parts". Sync the spec.
- **Coincident points should average, not race.** Two points landing on the same XY should become
  one at the mean Z rather than both being handed to `AddPoints` for Revit to reconcile however it
  sees fit. Two contours crossing in plan is not an error — it is what a saddle or a steep bank
  looks like — so the rule needs to be ours and deterministic.

  Belongs in the **sample or route stage**, before anything reaches the element: done there it is
  pure Core logic with a unit test, and the ledger then records one point where one point exists.
  Done inside the applier we would be reconciling against whatever Revit had already merged.

  **Open question before building it:** averaging a 42.750 contour with a 43.000 one gives 42.875, a
  height neither line asked for. That is right for a genuine crossing and wrong where the two lines
  describe a vertical face — a retaining wall read in plan is exactly two contours at the same XY.
  Decide whether averaging is the whole rule, or whether a large enough Z gap means "wall" and
  should keep both points, or warn.

- **A pre-existing vertex raised by Update keeps its height.** An edge point that lands on a vertex
  the tool did not create (an outline corner, a hand-placed point) is raised but not recorded, so it is
  never deleted by mistake. The cost: when the line moves away or is deleted, that vertex keeps the
  line's height. Fix if it matters: record the vertex's original Z in the ledger and put it back.
- **Two views sharing an outline point on one element:** raising a vertex for this view's line changes
  the Z another view's line recorded, so that view's next Update reports one false "moved by hand".
- **Cancel: `TopoSessionUpdater.Disable()` runs before `tx.Start()`, outside the `try`.** If `Start`
  throws, the session stays open with the updater off. Move both inside the `try`.
- **Interior points:** the edge-point dedupe does not cover two lines putting interior points on the
  same XY (see averaging above); a vertex merged into an existing one still counts as "added".

## Ideas (deliberately out of v1)

- Read contours from an imported survey DWG — the sampler and router are reused untouched; only a
  new collector is needed. This is the obvious next feature.
- Create a toposolid from the topo lines when none exists yet.
- Subdivisions and split lines/creases (`SlabShapeEditor.DrawSplitLine`).
- Multi-view sessions, as Auto Dimensions has for levels and views.
- The Topo Tools shortcut on a floor's contextual tab (trivial to add; not asked for).
- A session on several elements at once.
