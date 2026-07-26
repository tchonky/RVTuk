# Auto Dimensions

**What it is:** draw a detail line on the dedicated "Dimensions_Line" style as a
positional reference; the tool dimensions every wall, door and window crossing it,
re-runnable after model changes without re-picking references. The single entry point is a
dockable pane: tick the reference categories, and per level tick which views receive the
dimensions (a single-view run is just that one view ticked).

**Status:** registered on the RVTuk ribbon panel, gated by `RegisterAutoDimensions` in
`src/RVTuk.Revit/Application.cs` (on). In-Revit verification pass still outstanding.

**Names:** code `AutoDimensions`; ribbon button "Auto Dimensions". Supersedes the old
separate `KKimensions` / DimensionPropagator project.

**History:** v1 was an active-view-only, walls-only ribbon command. v1.1 folded that command
into the pane — same per-line pipeline (`DimensionRunner`), now fanned out across views and
categories. The v1 command class was deleted; recover it from git history if ever needed.

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
