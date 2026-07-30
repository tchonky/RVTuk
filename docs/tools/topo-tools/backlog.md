# Topo Tools — backlog

## Verification — outstanding

The code is complete and all three configurations build, but **nothing has been exercised in
Revit yet**. Deploy (`.\Deploy.ps1 2024` from an elevated shell, Revit closed) and work through
this list; record what each answers.

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
- [ ] **Setting a height** — type into a row's height box; the status line confirms it, and the row
      changes from *no height set* to a point count. Clearing the box un-sets it. Check that a value
      typed the way this office writes survey elevations is accepted by `UnitFormatUtils.TryParse`.
- [ ] **Selection, both ways** — selecting one or more topo lines in the view highlights exactly
      their rows; a row's *Select* button selects that line in the view without moving the camera.
      Check the highlight survives editing a height (which rebuilds the rows).
- [ ] **The height survives** — set a height, close and reopen the project: it is still there. Copy
      a topo line: the copy carries the same height.
- [ ] **A first run** — two detail lines on `Topo_Line` over a toposolid in a floor plan, each with
      a different `TOPO_Elevation`; Refresh lists both as *ready* with a point count; *Apply Points*
      deforms the toposolid, with points at the elevations given.
- [ ] **Idempotence** — *Apply Points* again with nothing changed: the summary reports the same
      number added and that number replaced, and the toposolid is unchanged. **The single most
      important check.**
- [ ] **A moved line** — drag one line, re-run: old points gone, new ones in place.
- [ ] **A cleared elevation** — clear one line's `TOPO_Elevation`, re-run: reported as skipped for
      having no elevation, **and its points from the previous run are gone.**
- [ ] **A deleted line** — delete a line, re-run: its points are gone.
- [ ] **Undo** — one Ctrl+Z takes the whole run back.
- [ ] **A section view** — with a section active, the pane says topo lines are read from plan views
      only.
- [ ] **`AddPoints` on a coincident point** — put a line's point directly over an existing toposolid
      vertex and re-run twice; the summary must not accumulate "moved by hand" counts, which would
      mean Revit merged the point and the ledger recorded something that is not there.

Also unverified by anything automated: `SlabShapeEditor.Enable()` on a toposolid that already has
points; whether a `SlabShapeVertex` wrapper stays usable after a sibling vertex is deleted
(`TopoPointApplier` snapshots the vertex list once and guards each `DeletePoint` with
`IsValidObject`); and whether an Extensible Storage double field with `SetSpec(SpecTypeId.Length)`
round-trips through `Get<double>(field, UnitTypeId.Feet)` on a detail line.

## Deferred deliberately

- **Bulk height setting** — "type one height, apply it to every line currently selected". The
  per-row editor covers the normal case, since each contour has its own height; this would only pay
  off for a pad drawn as several lines at one level. Add it if that turns out to be common.

## Ideas (deliberately out of v1)

- Read contours from an imported survey DWG — the sampler and router are reused untouched; only a
  new collector is needed. This is the obvious next feature.
- Create a toposolid from the topo lines when none exists yet.
- Subdivisions and split lines/creases (`SlabShapeEditor.DrawSplitLine`).
- Multi-view scope, as Auto Dimensions has for levels and views.
- A toposolid hidden in the scanned view keeps stale points: nothing in the run can reach its
  ledger. Only matters on phased or design-option sites.
