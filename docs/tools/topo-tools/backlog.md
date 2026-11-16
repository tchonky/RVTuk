# Topo Tools — backlog

## Verification

The Topo Edit session (2026-11) was tried in Revit 2024 on 2026-11-16 and reported working, but its
checklist rows are not ticked one by one. The finishing pass of the same day (Modify-tab buttons for
floors and Floor ⇄ Topo, icons, spacing without Enter, crossing points, error handling) has not
been in Revit at all: checklist rows 18 and 26–28, and Floor ⇄ Topo row 24. Both checklists are in
[README.md](README.md#in-revit-checklist-topo-edit-session). The pane-era checks (rows, selection
round trip, height boxes, status line) are gone with the pane.

Also unverified from the finishing pass: that `ElementOwnerViewFilter` keeps lines and texts hidden
in the view (Hide in View, a filter, Text Notes off) — the collectors depend on it, and rows 3 and 9
cover the area; and whether Revit shows its own error dialog as well as ours when Start, Height
Labels or Cancel return `Failed`.

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

- **A line grouped or restyled after being applied leaves its points.** Such a line is no longer
  collectable, so Update neither cleans nor reports its points on the element. The same happens to
  every line when `_DP-Topo Line` is renamed (the name comments now say so).
- `ShapeReplayPlanner.Plan` de-duplicates with a linear scan (O(n²)); bucket on an XY grid, as
  `TopoPointDeduper` now does, if survey toposolids of tens of thousands of points turn out slow
  (Floor ⇄ Topo checklist 5/8b).
- **A pre-existing vertex raised by Update keeps its height.** An edge point that lands on a vertex
  the tool did not create (an outline corner, a hand-placed point) is raised but not recorded, so it is
  never deleted by mistake. The cost: when the line moves away or is deleted, that vertex keeps the
  line's height. Fix if it matters: record the vertex's original Z in the ledger and put it back.
- **Two views sharing an outline point on one element:** raising a vertex for this view's line changes
  the Z another view's line recorded, so that view's next Update reports one false "moved by hand".
  The crossing rule weighs only the lines of the session's view, so it does not help here.
- **An interior point merged into an existing vertex still counts as "added".**

- **Revit 2026/2027 edge points are unverified.** Those APIs no longer have `DrawPoint`, so the
  edit session (like the converter) uses `AddPoint` there. Whether it accepts a point on the outline
  is unchecked; a refused point counts as lost. Check in 2026 before relying on edge points there.

## Done in the finishing pass (2026-11-16)

- The Modify-tab panel shows Topo Tools for one floor as well as one toposolid, and Floor ⇄ Topo for
  any selection of only floors and toposolids.
- Icons on the Topo Edit buttons, Floor ⇄ Topo and the Modify-tab buttons (`TopoIcons`).
- Spacing typed without Enter is read by Update and Accept; the spacing is rounded to the two
  decimals the box shows, so the read-back never rewrites a value Enter kept.
- Start and Height Labels catch, log and report failures; Start ends a half-started session and
  stops if Revit rolls back the "create styles" transaction. Cancel disables the updater inside its
  `try`, so a failing `tx.Start()` switches it back on.
- Coincident points: decided as **the higher point wins** (not averaging) — a retaining wall drawn
  as two lines keeps its top, and a genuine crossing gets the higher contour. One rule for interior
  and edge points (`TopoPointDeduper`, 1 mm), before anything reaches the element; the summary
  counts the points that took a higher line's height.
- A flat toposolid converted to a floor comes back a plain floor (`ResetSlabShape()`).
- A message ending in "." no longer gives ".." in a skip or refusal line (`Sentence`).
- The collectors use `ElementOwnerViewFilter` (never the view constructor, which drops hidden
  elements).
- The converter's undocumented losses are in the README; the converter spec matches the code's
  escaping and dependent wording.

## Ideas (deliberately out of v1)

- Read contours from an imported survey DWG — the sampler and router are reused untouched; only a
  new collector is needed. This is the obvious next feature.
- Create a toposolid from the topo lines when none exists yet.
- Subdivisions and split lines/creases (`SlabShapeEditor.DrawSplitLine`).
- Multi-view sessions, as Auto Dimensions has for levels and views.
- A session on several elements at once.
