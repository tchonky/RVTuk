# Topo Tools

**What it is:** draw detail lines on the dedicated "Topo_Line" style in a plan view, give each a
`TOPO_Elevation` (a shared/survey elevation), and the tool puts points at that height along each
line on the toposolids beneath them. Re-runnable: move a line, change its height or delete it, run
again, and the result is what you would have got by drawing it that way to begin with.

**Heights are shared (survey) elevations,** absolute as a surveyor quotes them. The conversion is
one subtraction — `ProjectPosition.Elevation` at the internal origin — which does mean the tool
depends on the document's shared-coordinate setup: relocating the survey point changes what a
stored value resolves to. For site work that is the correct dependency, and a re-run corrects the
model.

**Detail lines, and plan views only.** View-specific lines keep site working lines out of every
other view and out of 3D. A detail curve's geometry comes back on the view's sketch plane, so
dropping Z only recovers the drawn shape where that plane is horizontal — in a section the same
projection would collapse the line to a streak, and the pane refuses. A *model* line given the
Topo_Line style is ignored rather than half-supported: it would appear in every plan at once, which
is the confusion view-specific lines were chosen to avoid.

**A point has no identity.** There is no `Toposolid.AddPoints`; points go through
`GetSlabShapeEditor()`, and `SlabShapeVertex` is a position rather than an element. So each
toposolid carries an Extensible Storage ledger of `source line id → the points it owns there`,
recording the positions `AddPoints` returned, and a re-run matches them back within 0.1 mm. A
recorded point that has moved was moved by hand: it is left alone and reported.

**The ledger lives on the toposolid, not the line** — unlike `AutoDimensionTracker`. Storage on a
line dies with the line and orphans its points; on the toposolid a run sees a line id that no longer
resolves and cleans up. It also means a line merely absent from the scanned view is never mistaken
for a deleted one.

**Two subtleties in what a re-run deletes.** It removes the points of *every line the run saw*, not
only those producing points — otherwise clearing a line's height would leave the old height behind.
And it looks *across every toposolid holding that line's points*, not only the ones receiving points
now — otherwise dragging a line onto its neighbour leaves a copy behind on the first.

**Status:** registered on its own "RVTuk" panel on Revit's Massing & Site tab, gated by
`RegisterTopoTools` in `src/RVTuk.Revit/Application.cs` (on). Revit 2024/2025 only — `Toposolid`
does not exist in 2023, so KKarea never ships it. **In-Revit verification pass still outstanding**
(see [backlog.md](backlog.md)), including whether the Massing & Site panel resolves or the
Add-Ins fallback runs.

**Names:** code `TopoTools`; ribbon button "Topo Tools"; line style `Topo_Line`; parameter
`TOPO_Elevation`.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/TopoTools/` (sampler, containment, ledger codec, scope models) |
| UI    | `src/RVTuk.UI/TopoTools/` (pane view + view model) |
| Revit | `src/RVTuk.Revit/TopoTools/` (line style, parameter, collectors, router, applier, ledger store, runner, pane, external events) |
| Tests | `tests/RVTuk.Core.Tests/TopoTools/` |

`XyPoint` moved to `src/RVTuk.Core/Shared/Geometry/` when this tool was built — Auto Dimensions and
Topo Tools both use it, and the repo's rule is that a file lives in a tool folder only while one
tool needs it. `src/RVTuk.Revit/IsExternalInit.cs` arrived at the same time: this tool declares the
project's first records, and net48 has no such marker type in its BCL.

## Docs

- [specs/2026-07-29-topo-tools-design.md](specs/2026-07-29-topo-tools-design.md) — approved design
- [plans/2026-07-29-topo-tools.md](plans/2026-07-29-topo-tools.md) — implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas
