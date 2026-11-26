# Wall Finishes — backlog

## To check in Revit

- The whole tool: the checklists in Tasks 2–4 of the [plan](plans/2026-11-26-wall-finishes.md).
- Paint survives the rolled-back deletion of a wall's inserts.
- Whether that deletion needs ownership in a workshared or cloud model (else the fallback runs).
- Speed on a full floor of a large project.
- A straight run of two finishes (two wall types in line, or a split, painted face): each part
  keeps its own line style. Edges are split where a face ends; if `CreateViaOffset` merges the
  collinear pieces again, the whole run takes one finish.

## Deferred (from the review, 2026-11-26)

- An edge whose midpoint misses every face is filled from its neighbour, not counted.
- If every loop fails to offset, the run still deletes the previous lines in the view.
- Ceiling and area plans pass the view check (spec: floor plans).
- Each run wipes anything added to the legend view by hand.
- Two material names that differ only in refused characters share one style.
