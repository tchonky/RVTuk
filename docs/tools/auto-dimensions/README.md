# Auto Dimensions

**What it is:** draw a detail line on the dedicated "Dimensions_Line" style as a
positional reference; the tool dimensions every wall, door and window crossing it — in the
host model **and in loaded Revit links** — re-runnable after model changes without
re-picking references. The single entry point is a dockable pane: tick the reference
categories, and per level tick which views receive the dimensions (a single-view run is
just that one view ticked). Levels collapse; their views are listed alphabetically. The pane also
picks the **dimension type** every created dimension is given — the only way to reach Revit's
*Show Opening Height*, which prints a door's or window's height under its width. Tick that on the
type in Revit: the tool applies types, it never edits them.

**The cut plane decides what counts.** A plan view *draws* far more than it *cuts* —
everything down to its view depth appears in projection — and the crossing test is 2D, so a
wall on the storey below crosses a reference line just as convincingly as this storey's.
Every candidate, host or linked, must therefore reach the view's cut plane. Skipping this for
host elements was the cause of the "ghost marks under the floor" bug, and of real walls
losing their marks: a ghost face within ~1mm of a real one collapses the pair, and the ghost
sorts first.

**Linked models:** a linked document can't be collected view-scoped (the view belongs to the
host), so its elements are collected whole-document and filtered by the same cut plane.
Consequences: links are only considered in plan views, and a linked element hidden in the
view by other means (filters, worksets, phase) is still a candidate. References are
re-expressed through the link instance via `Reference.CreateLinkReference`.

**Walls and openings are matched by opposite tests,** because their references face opposite
ways. A wall is measured across its thickness, so the line must **cross** it. A door or window
is measured across its width, so the line must run **along** its host wall — a line crossing
that wall lies parallel to the jambs and cannot measure them at all. Running alongside has no
intersection to key on, so an opening qualifies when its wall is near-parallel to the line, its
centre falls within the line's span, and **no other parallel wall stands between that wall and
the line**. There is no distance limit — a string reaches the whole run it belongs to, however
far out it sits, and stops at the first wall behind it. Walls are therefore collected on every
run whether or not they are being dimensioned: unticking Walls means "don't dimension walls",
not "pretend walls aren't there". Only the walls actually asked for are counted in the run
summary's exclusions.
Both kinds come back interleaved in one order along the line, so a string reads
cross-wall, jamb, jamb, cross-wall.

**An opening is dimensioned once, by the nearest line that qualifies.** A facade usually
carries several stacked dimension strings, and every one of them can reach the same door;
measuring it from all of them is noise. Ownership is settled across all of a level's lines at
once — which is why matching happens in `DimensionRunner.RunPair` rather than per line — and
among the lines that actually qualify, not merely the nearest, so a door the closest string
can't see or doesn't span still falls to one that does. Ties go to the earlier line, so
re-running never shuffles a door between strings. **Walls are not deduplicated:** a wall
crossed by three strings is measured by all three, which is what a chained string is.

**When a crossing carries no mark,** the run summary says which of the silent causes applied:
not cut by the view, no usable reference (curtain and stacked walls report no side faces), or
references merged for sharing a position along the line.

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
- [specs/2026-07-29-auto-dimensions-opening-references-design.md](specs/2026-07-29-auto-dimensions-opening-references-design.md) — approved design (occlusion, dimension type)
- [plans/2026-07-04-auto-dimensions.md](plans/2026-07-04-auto-dimensions.md) — implementation plan (v1)
- [plans/2026-07-26-auto-dimensions-scope-pane.md](plans/2026-07-26-auto-dimensions-scope-pane.md) — implementation plan (scope pane)
- [plans/2026-07-29-auto-dimensions-opening-references.md](plans/2026-07-29-auto-dimensions-opening-references.md) — implementation plan (occlusion, dimension type)
- [backlog.md](backlog.md) — bugs / improvements / ideas
