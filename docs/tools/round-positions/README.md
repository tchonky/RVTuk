# Round Positions

**What it is:** a dockable pane that pulls model geometry back onto a round-number grid. Press
**Round Positions** on the RVTuk panel, scan the active model, and the pane lists everything that
sits off the grid as a Category → Level tree with counts. Fix any node, or everything, in one
action — one undo step. Elements that cannot be fixed are listed with the reason, never silently
dropped.

**The grid.** Whole or half centimetres, anchored at Revit's internal origin in X, Y and Z. The
"Allow half centimetres" checkbox (default on) picks the step; the choice is remembered in the
settings key `RoundPositionsWholeCentimetresOnly`.

**Moves things, never reshapes them.** Today the only category the tool handles is **Walls**:
exactly-orthogonal straight walls are snapped, moved parallel to themselves. No length changes, no
angle changes (a wall at 89.97° is reported, not fixed). The design also covers structural
framing, model and detail lines, levels, grids, reference planes, point-positioned families
and wall-hosted doors and windows; those are planned but not built (see Status). Sketch-based
elements, arcs, stairs and railings are v2.

**Status:** partly built, Revit 2024/2025. The scan-and-fix pane works end to end, but
`PassRegistry.All` holds Walls only, with `LineReader` as the sole reader. Tasks 1-7 of the v2
plan are done (scan result and tree, batching, offset verdict and wall reader, runner, timing
gate, pane view model, pane view and handlers). Tasks 8-14 are not on `master`: framing and
lines, levels, grids and reference planes, point families, wall-hosted openings, base points, and
the tool's backlog. `RegisterRoundPositions` (on) in `src/RVTuk.Revit/Application.cs` gates the
pane and its external events; the ribbon button is always added, and with the flag off it shows a
message instead of the pane. Not in KKarea (2023/2026). Merged into `master` in
November 2026 after sitting on its own branch since August; not yet re-verified in Revit since
the merge.

**Entry points:** ribbon button `RoundPositions`; dockable pane id
`b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370` (`RoundPositionsPaneProvider`). Scan, fix and select run
through three external events (`ScanEventHandler`, `FixEventHandler`, `SelectEventHandler`);
the logic lives in `RVTuk.Core.RoundPositions`, the pane in `RVTuk.UI.RoundPositions`.

**Spec:** [`specs/2026-08-06-round-positions-design.md`](specs/2026-08-06-round-positions-design.md) ·
**Plans:** [`plans/2026-08-06-round-positions-v1.md`](plans/2026-08-06-round-positions-v1.md),
[`plans/2026-08-06-round-positions-v2-scan-and-fix.md`](plans/2026-08-06-round-positions-v2-scan-and-fix.md)
