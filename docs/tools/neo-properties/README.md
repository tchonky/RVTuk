# Neo Properties

**What it is:** a dockable pane mirroring the selected element's parameters like the
native Properties palette, but with pinned parameters shown first and remaining groups
in a fixed custom order. Read-only, single-element only.

**Status:** code-complete, **hidden for v1** behind the `RegisterUnreleasedTools` flag
in `src/RVTuk.Revit/Application.cs`.

**Names:** code `NeoProperties`; ribbon button "Neo Properties" (when registered).

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/NeoProperties/` (parameter ordering/grouping) |
| UI    | `src/RVTuk.UI/NeoProperties/` (pane view + view model) |
| Revit | `src/RVTuk.Revit/NeoProperties/` (command, pane provider, selection handler) |
| Tests | `tests/RVTuk.Core.Tests/NeoProperties/` |

## Docs

- [specs/2026-07-04-neo-properties-design.md](specs/2026-07-04-neo-properties-design.md) — approved design
- [plans/2026-07-04-neo-properties.md](plans/2026-07-04-neo-properties.md) — implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas
