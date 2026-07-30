# Topo Tools — backlog

## Verification — outstanding

The code is complete and all three configurations build, but **nothing has been exercised in
Revit yet**. Deploy (`.\Deploy.ps1 2024` from an elevated shell, Revit closed) and work through
this list; record what each answers.

- [ ] **Ribbon placement** — is the "Topo Tools" button on the **Massing & Site** tab, or did it
      fall back to the RVTuk panel on Add-Ins? This is the one question the reference assemblies
      could not answer: `Autodesk.Revit.UI.Tab` names only `AddIns` and `Analyze`, so the panel is
      created through `CreateRibbonPanel("Massing & Site", "RVTuk")` with a fallback. If it fell
      back, leave the fallback in place and note it here.
- [ ] **Setup** — the pane shows its banner; *Set up this project* creates the `Topo_Line` line
      style (Manage → Object Styles → Lines) and puts **TOPO_Elevation** on a detail line's
      Properties palette as a Length.
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
points, and whether a `SlabShapeVertex` wrapper stays usable after a sibling vertex is deleted
(`TopoPointApplier` snapshots the vertex list once and guards each `DeletePoint` with
`IsValidObject`).

## Ideas (deliberately out of v1)

- Read contours from an imported survey DWG — the sampler and router are reused untouched; only a
  new collector is needed. This is the obvious next feature.
- Create a toposolid from the topo lines when none exists yet.
- Subdivisions and split lines/creases (`SlabShapeEditor.DrawSplitLine`).
- Multi-view scope, as Auto Dimensions has for levels and views.
- A toposolid hidden in the scanned view keeps stale points: nothing in the run can reach its
  ledger. Only matters on phased or design-option sites.
