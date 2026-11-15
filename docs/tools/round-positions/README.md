# Round Positions

**What it is:** a dockable pane that pulls model geometry back onto a round-number grid. Press
**Round Positions** on the RVTuk panel, scan the active model, and the pane lists everything that
sits off the grid as a Category → Level tree with counts. Fix any node, or everything, in one
action — one undo step. Elements that cannot be fixed are listed with the reason, never silently
dropped.

**The grid.** Whole or half centimetres, anchored at Revit's internal origin in X, Y and Z. The
"Allow half centimetres" checkbox (default on) picks the step; the choice is remembered in the
settings key `RoundPositionsWholeCentimetresOnly`.

**Moves things, never reshapes them.** Point-positioned family instances (columns, furniture,
casework, generic models), exactly-orthogonal straight lines (walls, framing, grids, reference
planes, model and detail lines — moved parallel to themselves), wall-hosted doors and windows
(along their wall only) and level elevations are snapped. No length changes, no angle changes
(a wall at 89.97° is reported, not fixed). Sketch-based elements (floors, roofs, ceilings), arcs,
stairs and railings are v2.

**Status:** v1, Revit 2024/2025. Behind `RegisterRoundPositions` (on) in
`src/RVTuk.Revit/Application.cs`. Not in KKarea (2023/2026/2027). Merged into `master` in
November 2026 after sitting on its own branch since August; not yet re-verified in Revit since
the merge.

**Entry points:** ribbon button `RoundPositions`; dockable pane id
`b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370` (`RoundPositionsPaneProvider`). Scan, fix and select run
through three external events (`ScanEventHandler`, `FixEventHandler`, `SelectEventHandler`);
the logic lives in `RVTuk.Core.RoundPositions`, the pane in `RVTuk.UI.RoundPositions`.

**Spec:** [`specs/2026-08-06-round-positions-design.md`](specs/2026-08-06-round-positions-design.md) ·
**Plans:** [`plans/2026-08-06-round-positions-v1.md`](plans/2026-08-06-round-positions-v1.md),
[`plans/2026-08-06-round-positions-v2-scan-and-fix.md`](plans/2026-08-06-round-positions-v2-scan-and-fix.md)
