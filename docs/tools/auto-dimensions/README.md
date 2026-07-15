# Auto Dimensions

**What it is:** draw a detail line on the dedicated "Dimensions_Line" style as a
positional reference; a ribbon command dimensions every wall crossing it, re-runnable
after model changes without re-picking references.

**Status:** code-complete, **hidden for v1** behind the `RegisterUnreleasedTools` flag
in `src/RVTuk.Revit/Application.cs`.

**Names:** code `AutoDimensions`; ribbon button "Auto Dimensions" (when registered).
Supersedes the old separate `KKimensions` / DimensionPropagator project.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/AutoDimensions/` (wall-crossing finder, reference filter) |
| UI    | — (no UI; the tool is a single command) |
| Revit | `src/RVTuk.Revit/AutoDimensions/` (command, tracker, line style) |
| Tests | `tests/RVTuk.Core.Tests/AutoDimensions/` |

## Docs

- [specs/2026-07-04-auto-dimensions-design.md](specs/2026-07-04-auto-dimensions-design.md) — approved design
- [plans/2026-07-04-auto-dimensions.md](plans/2026-07-04-auto-dimensions.md) — implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas
