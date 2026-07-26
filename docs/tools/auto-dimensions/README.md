# Auto Dimensions

**What it is:** draw a detail line on the dedicated "Dimensions_Line" style as a
positional reference; a ribbon command dimensions every wall crossing it, re-runnable
after model changes without re-picking references. A dockable **scope pane** fans the same
per-line logic out across every selected view of every level, and adds doors and windows as
reference categories alongside walls.

**Status:** code-complete, **hidden for v1** behind the `RegisterUnreleasedTools` flag
in `src/RVTuk.Revit/Application.cs`.

**Names:** code `AutoDimensions`; ribbon buttons "Auto Dimensions" (single view, walls only)
and "Dimension Scope" (the pane). Supersedes the old separate `KKimensions` /
DimensionPropagator project.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/AutoDimensions/` (crossing finder, reference filter, scope models, category mask, opening segment, id codecs) |
| UI    | `src/RVTuk.UI/AutoDimensions/` (scope pane view + view models) |
| Revit | `src/RVTuk.Revit/AutoDimensions/` (commands, runner, candidate collector, discovery, tracker, line style, pane provider, external events) |
| Tests | `tests/RVTuk.Core.Tests/AutoDimensions/` |

## Docs

- [specs/2026-07-04-auto-dimensions-design.md](specs/2026-07-04-auto-dimensions-design.md) — approved design (single view, walls)
- [specs/2026-07-23-auto-dimensions-scope-pane-design.md](specs/2026-07-23-auto-dimensions-scope-pane-design.md) — approved design (scope pane)
- [plans/2026-07-04-auto-dimensions.md](plans/2026-07-04-auto-dimensions.md) — implementation plan (v1)
- [plans/2026-07-26-auto-dimensions-scope-pane.md](plans/2026-07-26-auto-dimensions-scope-pane.md) — implementation plan (scope pane)
- [backlog.md](backlog.md) — bugs / improvements / ideas
