# Topo Tools — backlog

## Verification

The Topo Edit session (2026-11) was tried in Revit 2024 on 2026-11-16 and reported working, but its
checklist rows are not ticked one by one. The finishing pass of the same day (icons, spacing
without Enter, crossing points, error handling) has not been in Revit at all: checklist rows 26–28,
and Floor ⇄ Topo row 24. Its Modify-tab buttons for floors and Floor ⇄ Topo are gone again (the
pass below). Both checklists are in [README.md](README.md#in-revit-checklist-topo-edit-session). The
pane-era checks (rows, selection round trip, height boxes, status line) are gone with the pane.

**The follow-the-session pass (2026-11-17 and 18)** is built, with Core tests on all three
frameworks, but not checked in Revit: checklist rows 29–85, and rows 2, 3, 18, 21, 27 and 28 as
reworded for it, 2024 first, then 2025–2027. The probe sitting (Revit 2024, 2026-11-18) ran a
throwaway build, and its main log was lost (Revit empties its per-session TEMP folder when it
closes). So each point the probe left open was decided on the side the plan built, and became a
row. These could still change the code:
- whether Revit's own Survey Point readings agree with the tool when the marker sits off the zero
  of the levels (rows 73 and 74): if they follow the marker, the tool measures from the marker and
  the marker line goes (the spec's rule 9);
- whether Draw Lines pressed in another view starts drawing in the session's view (row 42): if not,
  the "press Draw Lines again" fallback;
- whether a click into a tiled view disarms the session (row 43);
- whether ✓ clicked on the Modify-tab copy while drawing ends the tool and accepts (row 64): if not,
  no copy on the Modify tab, and the switch to Add-Ins stays;
- whether Synchronize with Central brings up the save prompt twice (row 57), and whether the
  prompt shows during a close's save (row 55);
- where the copy sits, and whether it folds at the office's usual width (rows 31 and 62): a copy
  folded at that width goes back to Daniel (accept it, or have Revit's own panels fold first).

UIFramework 2025 could not be read for this pass (not installed here), so the Modify-tab rows
matter most in 2025. The probe branch `topo-probe-2026-11-17` stays until these checks pass.

Also unverified from the finishing pass: that `ElementOwnerViewFilter` keeps lines and texts hidden
in the view (Hide in View, a filter, Text Notes off) — the collectors depend on it, and rows 3 and 9
cover the area; and whether Revit shows its own error dialog as well as ours when Start, Height
Labels or Cancel return `Failed`.

Settled earlier and kept for the record:

- **Ribbon placement** — answered 2026-07-30: Massing & Site is unreachable; the API only places
  custom panels on Add-Ins, on Analyze, or on a tab the add-in creates itself. For the panel,
  **do not retry**; if it ever matters, the supported move is a top-level `RVTuk` tab for the whole
  toolkit. The one exception is the Modify tab, through `Autodesk.Windows`: the Topo Tools shortcut
  (decided 2026-11-16) and, during a session, the Topo Edit panel (2026-11-17); see the README.
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
  deleted (cases 6 and 12 cover it); the API docs say `Element.GetEntity` returns a copy of the
  stored data (all four years);
- whether the outline curves read off the source's sketch stay usable once the source is deleted:
  `SlabConverter` builds the new element from them after the delete (SlabConverter.cs:46-47, :51,
  :88; cases 2–4 cover it);
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

## Done in the follow-the-session pass (2026-11-17 and 18)

Spec [2026-11-17-topo-edit-follows-session-design.md](specs/2026-11-17-topo-edit-follows-session-design.md),
plan [2026-11-17-topo-edit-follows-session.md](plans/2026-11-17-topo-edit-follows-session.md).

- The Topo Edit panel: ✗ above ✓ at its left end, icons only, red and green in a light-theme and a
  dark-theme version (swapped on `ThemeChanged`); then Draw Lines, Height Labels, Update and
  Spacing. Folded, it shows the Topo Tools icon.
- During a session a copy of the Topo Edit panel shows on the Modify tab, whatever is selected, and
  the ribbon switches to Add-Ins once, at Revit's first idle moment after the start
  (`ModifyTabHost`).
- The Modify-tab shortcut holds Topo Tools alone, for one non-subdivision toposolid only;
  Floor ⇄ Topo left the Modify tab. Every panel of ours there is Revit's own panel class
  (`ModifyTabAccess.NewRevitPanel`) and leaves the tab in edit modes: the old plain panel, only
  hidden, could make Revit's own code throw in an edit mode. Two switch files check the fail-safe
  (`topo-modify-tab-off`, `topo-modify-copy-off`).
- Topo Tools pressed during a session asks: Go to it, Accept it or Cancel it (`TopoSessionActions`
  runs ✓ and ✗ without their buttons).
- Arming: a new line in another style becomes a topo line only while the session is armed. Draw
  Lines arms it, and opens the session's view first when pressed in another view.
- The session ends at `DocumentClosed`, so a close called off keeps it and its last typed height
  (`TopoDocumentEvents`).
- Saving or syncing during a session asks first (`TopoSaveGuard`).
- Heights are metres, measured like a Survey Point level, in every project: `SurveyHeightText`, in
  Core with tests, replaces Revit's unit parsing and formatting. The first summary shown in a
  session names a Survey Point marker that sits off that zero.
- Update shows its summary only when something needs attention or nothing was applied; ✓ always
  shows it (`TopoSessionReport.ShowAfterUpdate`).
- A height text whose line was deleted no longer breaks Update or Height Labels: `HeightTexts.Label`
  read the Id of a text it had just deleted.
- New `_DP-Topo Line` and `_DP-Topo Height` are orange (RGB 255, 140, 0); existing ones keep their
  colour.
- Floor ⇄ Topo skips the element a session is open on.

## Done in the finishing pass (2026-11-16)

- The Modify-tab panel shows Topo Tools for one floor as well as one toposolid, and Floor ⇄ Topo for
  any selection of only floors and toposolids. (Undone on 2026-11-17: the shortcut is for one
  toposolid only, and Floor ⇄ Topo left the Modify tab.)
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
- Warn when a line's height is far from the element's surface. It would catch what the −500 to
  +3000 m net misses: a forgotten point, or an old text typed in centimetres, on a site between
  −5 and +30 m.
- Hide the Topo Edit panel only after Revit has listed its commands, so its buttons can take
  keyboard shortcuts. Revit builds that list when it starts and skips hidden panels; probe 7 found
  none of Topo Edit's buttons in it.
