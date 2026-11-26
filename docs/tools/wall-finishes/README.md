# Wall Finishes

**What it is:** finish lines for the contractor. In a floor plan, press **Wall Finishes** on the
RVTuk panel. Every exposed wall face the view cuts gets a detail line 10 cm off the face, in the
open space in front of it. The line's style names the face's finish: the paint on that face, or
else the wall type's outermost layer on that side. Lines meet cleanly at the corners.

- **Only the outside of a build-up.** Where walls stand against each other, joined or not, the
  faces where they touch get no line.
- **Openings are ignored.** Lines run straight across doors, windows and wall openings. When
  those cannot be set aside for the read (another user has them borrowed in a workshared
  model), the lines break at each opening instead, and the tool says so.
- **Free ends are wrapped**, in the end's paint, or else the finish of the side the line turns from.
- **Re-runnable.** A second run in the same view replaces that view's lines: every line on a
  `_DP-Mat …` style in the view is deleted first, including any drawn by hand on those styles.
- **Legend.** Each run rebuilds the drafting view **RVTuk Wall Finishes** (1:50): one sample line
  per finish style that has a line anywhere in the project, with the material's name. Place it
  on a sheet like any view.

**Line styles.** One per material, `_DP-Mat <material name>`, made the first time that material
appears, in the material's own colour with a solid pattern. Set a pattern (or a darker colour for
white finishes) once in Object Styles; the tool never changes a style that exists.

**Not handled:** linked models, curtain walls, walls the cut plane does not cut, columns
interrupting a face (the line follows the wall's own outline).

**Status:** built, Revit 2024–2027, **not yet checked in Revit** — see the plan's checklists.
Behind `RegisterWallFinishes` (on) in `src/RVTuk.Revit/Application.cs`. Not in KKarea (2023).

**Entry point:** `src/RVTuk.Revit/WallFinishes/Commands/WallFinishesCommand.cs`.
[Design](specs/2026-11-26-wall-finishes-design.md) · [Plan](plans/2026-11-26-wall-finishes.md)
