# Wall Finishes — design

2026-11-26. Tool name `WallFinishes`, ribbon "Wall Finishes", branch `wall-finishes`.

## Why

The contractor needs to see, on the floor plans, which finish goes on every wall face. Today
that is drawn by hand. The drawings are printed in colour; the contractor tells finishes apart
by a line's colour and pattern, and reads them off a legend.

## What it does

In the active floor plan, press **Wall Finishes**. Every exposed wall face cut by the view gets
a detail line 10 cm off the face, in the open space in front of it. The line's style names the
face's material: the paint on that face if it is painted, otherwise the wall type's outermost
layer on that side. Lines meet cleanly at corners.

- **Only the outside of a build-up counts.** Where walls stand against each other — joined or
  not — the faces where they touch get no line. (Daniel, 2026-11-26)
- **Openings are ignored.** Lines run straight across doors, windows and wall openings, as if
  they were not there; jambs get no line. (Daniel, 2026-11-26)
- **Free ends are wrapped.** At the end of a wall that stops in the open, the line runs round
  the end too, in the end face's paint, or else the finish of the side it turns from.
  (Daniel, 2026-11-26)
- **Re-runnable.** A second run in the same view replaces that view's lines.
- **A legend.** Each run rebuilds a drafting view, "RVTuk Wall Finishes": one short line per
  finish style used anywhere in the project, with the material's name next to it. It is placed
  on a sheet by hand like any view.

One button, no pane, no settings — like Room Floors. One undo step. Revit 2024–2027, in
`RVTuk.Revit`; not in KKarea.

## Non-goals

- Linked models' walls, curtain walls, walls the cut plane does not cut (low walls).
- Columns, furniture or anything but walls interrupting a face: a line runs past a column
  standing against the wall.
- Partly painted faces beyond what Revit already splits into separate faces.
- Other things that cut walls (wall reveals, face-based voids, in-place cuts) are not removed
  before reading: where they notch the cut outline, the line follows the notch. So does a
  column that cuts into a wall.
- Keeping lines up to date as the model changes: the user re-runs.
- A chosen pattern per material. See *Line styles*.

## Approaches

**A. Merge the walls' plan outline into one shape — chosen (Daniel, 2026-11-26).** Cut every
wall at the view's cut height, merge the sections with Revit's own solid boolean, and the
merged shape's edges are exactly the exposed faces. Each edge is matched to the wall face it
lies on for its material, and the outline is offset 10 cm with `CurveLoop.CreateViaOffset`,
which gives clean corners for free. Risk: Revit's boolean can fail on bad geometry.

B. Draw each face, subtracting where another wall covers it. Corner trimming becomes ours to
write. More code, more cases. Dropped.

C. Trust Revit's wall joins to hide inner faces. Walls that touch without being joined would
still get lines. Rejected.

## How

All in one external command, `RVTuk.Revit/WallFinishes/`.

1. **Walls.** Walls visible in the active plan view (`FilteredElementCollector(doc, view.Id)`),
   minus curtain walls; a stacked wall contributes its member walls.
2. **Read the geometry without openings.** In a transaction that is always rolled back: delete
   every door, window and `Opening` hosted by those walls, `doc.Regenerate()`, then read. Both
   the geometry and the materials (step 4) are read in here, before rollback; only plain
   curves and material ids leave it.
   **Fallback** (Daniel, 2026-11-26): if the deletion cannot be made — elements another user
   has borrowed in a workshared model, or anything else it throws — read the walls as they
   are, and leave out every edge lying on a face the inserts made
   (`wall.GetGeneratingElementIds(face)` names an insert). The line then breaks at each
   opening instead of running across it; jambs still get none. Said once in the report.
3. **Merge.** Intersect each wall's solid with one thin horizontal box at the view's cut height
   (level elevation + view range cut offset), union the results
   (`BooleanOperationsUtils.ExecuteBooleanOperation`), and take the merged solid's downward
   planar face. Its edge loops are the exposed faces — the outside of each build-up, and the
   inner loops round each room.
4. **Material per edge.** For each edge, find the wall face its midpoint lies on. If
   `doc.IsPainted(wallId, face)` → `doc.GetPaintedMaterial(wallId, face)`. Otherwise the wall's
   `CompoundStructure`: the exterior layer (index 0) when the face is among
   `HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)`, the interior layer (last)
   when among `…Interior` — no normal test, so arc walls work. A free end's face, being on
   neither side, takes the material of the side face that the edge before it on the loop lay
   on (the side the line turns from). A layer with no material takes
   the Walls category's material; nothing at all → skip the edge and count it.
5. **Offset.** `CurveLoop.CreateViaOffset(loop, 10 cm, Z)`, the sign chosen so the result lies
   outside the merged face. The offset is not guaranteed one-to-one with the edges (a short
   edge at a concave corner can vanish), so each offset curve takes the material of the edge
   it runs parallel to at 10 cm — matched by geometry, never by index. A loop whose offset
   throws, or comes back crossing itself (a gap narrower than 20 cm), is skipped and counted.
6. **Draw.** In one transaction "Wall Finishes": delete the detail lines owned by this view on
   any `_DP-Mat …` style, create each missing style, draw the new lines, rebuild the legend.
7. **Report** at the end, in a `TaskDialog`, only if something was skipped: how many walls
   failed the boolean (those are then drawn from their own outline alone), edges without a
   material, loops whose offset failed.

### Line styles

One per material: `_DP-Mat <material name>`, created through `Shared/LineStyleCreator`
the first time that material appears, with the material's `Color` and a solid pattern. The
architect sets a pattern once in Object Styles if wanted; the tool never changes a style that
exists, so that choice is kept. A white or near-white material (plaster, white paint) gives a
line that barely prints; the architect changes that style's colour once, and it is kept.
*(Claude's call: no fallback colour rule.)*

### Legend

A `ViewDrafting` named "RVTuk Wall Finishes", made if missing, at 1:50. Each run deletes
everything owned by it and redraws: per `_DP-Mat …` style that has at least one line in some
view of the project (one collector over `CurveElement`, grouped by `LineStyle` — so a style
whose material was renamed or is no longer used drops off), sorted by name, a
1 m line (2 cm on paper) in that style with a text note of the material name beside it, rows
1 m apart. The API cannot create a Legend view from scratch, hence a drafting view.

## Left to the builder

- Characters a subcategory name refuses, in material names, and how they are replaced.
- Merging collinear neighbouring lines of the same material into one, or not.
- Tolerances for "a midpoint lies on a face".
- The thin box's thickness, and nudging the cut height when it lands exactly on a wall's top.
- Whether a wall that fails the boolean still gets its own lines (step 7 assumes it does).

## To check in Revit

- Paint survives the rolled-back deletion of a wall's inserts (step 2), so it reads right.
- `CreateViaOffset` keeps curves one-to-one on merged outlines with arcs.
- Whether the rolled-back deletion in step 2 needs the inserts' ownership in a workshared or
  cloud model. If it does, the fallback in step 2 is what those models get.
- Speed on a full floor of a large project; deleting every insert may be slow.
