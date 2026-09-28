# Room Floors — backlog

## Ideas (out of v1, each a clean addition)

- Replace a floor that hosts elements or carries tags/dimensions by editing its sketch
  (`SketchEditScope`) instead of deleting it, so hosted elements, tags/dimensions, instance
  parameters, workset and shape edits all survive the replace — retiring the F1 refusal.
- Extend the floor into door openings: a strip under each door hosted in the room's bounding
  walls, to the middle of the wall.
- Pick the floor type (and offset) up front, or take it from the room's *Floor Finish* parameter.
- Batch mode: every room on a level in one go.
- Rooms in linked models.
- Structural slabs that run under walls, not just finish floors.
- Floors copied to the same level are still linked to the original room (containment check).
- A room on another level whose floor is a pasted copy gets a second floor on top of it.

## Bugs

(none yet)

## Done

- v1: click-to-create, replace keeping type and offset, skip summary (2026-09-28).
