# Topo Tools — backlog

## Verification

Exercised in Revit 2024 on 2026-07-30. **Most of the list passes, including the geometry path and
idempotence; two checks are still open, and the two that failed had root causes worth keeping** —
they are recorded inline rather than deleted, because both invalidated a design decision.

Deploy with `.\Deploy.ps1 2024` from an elevated shell, Revit closed.

- [x] **Ribbon placement** — **answered 2026-07-30: Massing & Site is unreachable.** The button
      appeared on the RVTuk panel on Add-Ins, i.e. the fallback ran. The API only places custom
      panels on Add-Ins, on Analyze, or on a tab the add-in creates itself; the `Autodesk.Windows`
      route that could reach the real tab is unsupported and may break on any Revit update, so it
      was declined. The dead attempt-and-fall-back code has been removed. **Do not retry this** —
      if the placement ever matters enough, the supported move is a top-level `RVTuk` tab for the
      whole toolkit, not a hack for one button.
- [x] **Setup** — **failed 2026-07-30, root cause found and fixed.** *Set up this project* reported
      success while the banner stayed, because `TOPO_Elevation` can never bind: `OST_Lines` has
      `AllowsBoundParameters == false`, so `ParameterBindings.Insert` returned false — and
      `EnsureBound` was discarding that return value, so a total failure was reported as "Ready".
      Heights moved to Extensible Storage edited in the pane (`TopoElevationStore`), and the
      parameter binder is gone. **Re-check:** *Set up this project* creates the `Topo_Line` style
      (Manage → Object Styles → Lines) and the banner then disappears.
- [x] **Setup button removed; the style creates itself.** `_DP-Topo Line` is made on the pane's
      first refresh. The helper only opens a transaction when the style is actually missing, so
      later refreshes stay read-only and don't mark the document modified. (2026-08-03)
- [x] **Setting a height** — type into a row's height box; the status line confirms it, and the row
      changes from *no height set* to a point count. Clearing the box un-sets it. Check that a value
      typed the way this office writes survey elevations is accepted by `UnitFormatUtils.TryParse`.
- [X] **Selection, both ways** — selecting one or more topo lines in the view highlights exactly
      their rows; a row's *Select* button selects that line in the view without moving the camera.
      Check the highlight survives editing a height (which rebuilds the rows).
- [x] **The height survives** — set a height, close and reopen the project: it is still there. Copy
      a topo line: the copy carries the same height.
- [x] **A first run** — two detail lines on `Topo_Line` over a toposolid in a floor plan, each with
      a different `TOPO_Elevation`; Refresh lists both as *ready* with a point count; *Apply Points*
      deforms the toposolid, with points at the elevations given.
- [x] **Idempotence** — *Apply Points* again with nothing changed: the summary reports the same
      number added and that number replaced, and the toposolid is unchanged. **The single most
      important check.**
- [x] **A moved line** — drag one line, re-run: old points gone, new ones in place.
- [ ] **A cleared elevation** — clear one line's `TOPO_Elevation`, re-run: reported as skipped for
      having no elevation, **and its points from the previous run are gone.**
- [x] **A deleted line** — delete a line, re-run: its points are gone.
- [ ] **Undo** — one Ctrl+Z takes the whole run back.
- [x] **A section view** — with a section active, the pane says topo lines are read from plan views
      only.
- [ ] **`AddPoints` on a coincident point** — put a line's point directly over an existing toposolid
      vertex and re-run twice; the summary must not accumulate "moved by hand" counts, which would
      mean Revit merged the point and the ledger recorded something that is not there.
      **Superseded in intent by "coincident points should average" below** — the check still says
      what today's code does, but the answer is meant to change.

Also unverified by anything automated: `SlabShapeEditor.Enable()` on a toposolid that already has
points; whether a `SlabShapeVertex` wrapper stays usable after a sibling vertex is deleted
(`TopoPointApplier` snapshots the vertex list once and guards each `DeletePoint` with
`IsValidObject`); and whether an Extensible Storage double field with `SetSpec(SpecTypeId.Length)`
round-trips through `Get<double>(field, UnitTypeId.Feet)` on a detail line.

## To check after the 2026-08-02 UI round

- [ ] **Enter commits a height**, on both the height boxes and the spacing box.
- [ ] **Trailing zeros are gone** — heights and lengths read "42750", not "42750.0". This is the
      second attempt: Revit's own `SuppressTrailingZeros` did nothing and failed silently, so the
      numbers are now converted and formatted by `TopoLengthFormatter` instead.
- [ ] **The active view's name** is green when the view has topo lines, red when it has none.
- [ ] **Spacing reads centimetres** and the remembered value survives a restart. The config key was
      renamed from the millimetre one, so the first run after this change starts at the 100 cm
      default rather than carrying the old number across.
- [ ] **Committing a height while clicking straight onto another row's box.** A commit triggers a
      refresh that rebuilds every row, so the rebuild races the click — watch for focus landing
      somewhere unexpected, or the second box clearing itself.
- [ ] **Ctrl-click several rows, then click a wall in the view.** The selection round trip has two
      sources of truth wired together and a guard flag between them; this is the sequence most
      likely to expose a loop or a stuck highlight.

## Next

- **Coincident points should average, not race.** Two points landing on the same XY should become
  one at the mean Z rather than both being handed to `AddPoints` for Revit to reconcile however it
  sees fit. Two contours crossing in plan is not an error — it is what a saddle or a steep bank
  looks like — so the rule needs to be ours and deterministic.

  Belongs in the **sample or route stage**, before anything reaches the toposolid: done there it is
  pure Core logic with a unit test, and the ledger then records one point where one point exists.
  Done inside the applier we would be reconciling against whatever Revit had already merged.

  **Open question before building it:** averaging a 42.750 contour with a 43.000 one gives 42.875, a
  height neither line asked for. That is right for a genuine crossing and wrong where the two lines
  describe a vertical face — a retaining wall read in plan is exactly two contours at the same XY.
  Decide whether averaging is the whole rule, or whether a large enough Z gap means "wall" and
  should keep both points, or warn.

## Ideas (deliberately out of v1)

- Read contours from an imported survey DWG — the sampler and router are reused untouched; only a
  new collector is needed. This is the obvious next feature.
- Create a toposolid from the topo lines when none exists yet.
- Subdivisions and split lines/creases (`SlabShapeEditor.DrawSplitLine`).
- Multi-view scope, as Auto Dimensions has for levels and views.
- A toposolid hidden in the scanned view keeps stale points: nothing in the run can reach its
  ledger. Only matters on phased or design-option sites.
