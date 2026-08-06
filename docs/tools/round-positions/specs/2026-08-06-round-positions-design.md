# Round Positions — Design

**Date:** 2026-08-06
**Status:** Approved
**Area:** Productivity

## Problem / motivation

Model geometry drifts off round numbers from four directions at once, and all four are live in our
projects: walls traced over an imported CAD survey (which also leaves them slightly *rotated*, not
just offset), models inherited from consultants, our own drafting drift when snapping is off or a
temporary dimension is typed carelessly, and clusters displaced by a copy/move with a non-round
delta. The result is a model where a wall sits at 1.001, 5.001 instead of 1, 5, and reads 1000.4 cm
long instead of 1000.5.

We work in centimetres. Nothing in our drawings needs finer resolution than a centimetre, with the
single exception of a half centimetre. A one-click command that pulls the whole model back onto
that resolution removes an entire class of tedious manual correction.

## The core tension

"Round position" and "round dimension" are not the same goal, and for non-orthogonal geometry they
are mutually exclusive. A wall from (0, 0) to (301.2, 400.1) cm cannot have both endpoints on a
0.5 cm grid *and* a length that is a whole 0.5 cm — the maths forbids it, since the length of a
grid-aligned diagonal is almost never a grid multiple.

The resolution: **straighten first, then round.** A line within tolerance of an axis was almost
certainly *meant* to be on that axis, and once it is, both goals are satisfiable at once. Only
genuinely diagonal geometry has to choose, and there endpoints win.

## Goals

- One ribbon button, **"Round Positions"**, that runs over the whole active model with no dialog,
  no scope picker and no options, and reports afterwards what it did.
- Positions, lengths and heights land on a 0.5 cm grid.
- Near-orthogonal and near-45° geometry is straightened exactly onto its axis.
- Wall corners and other coincident endpoints stay coincident.
- Elements the tool must not or cannot touch are skipped and reported, never half-moved.
- The whole run is a single undo step.

## Non-goals

- **Sketch-based elements (floors, roofs, ceilings) are not modified in v1.** Their boundary is a
  `Sketch`, editable only through `SketchEditScope`, and sketch curves are frequently locked to
  wall faces, so edits are either overridden or throw. v1 measures and reports their off-grid
  vertices so the damage is visible; the sketch pass is v2.
- Arcs — arc walls, arc grids, arc framing. Reported, not touched.
- Stairs and railings.
- Any scope control. The button is deliberately all-or-nothing; a scope/category dialog was
  considered and declined.
- Rooms, areas, dimensions and tags. These are derived from what bounds them and follow along on
  their own.
- KKarea / Revit 2023. This ships in `RVTuk.Revit` only, so 2024 and 2025.

## The rounding rules

### The grid

0.5 cm in X, Y and Z, anchored at **Revit's internal origin** — not the Project Base Point and not
the Survey Point. Snapping is `Math.Round(v / 0.5, MidpointRounding.AwayFromZero) * 0.5`, performed
in centimetres and converted back to Revit's internal decimal feet (1 ft = 30.48 cm, exactly).
`AwayFromZero` rather than the .NET default keeps exact midpoints symmetric across the origin:
0.25 → 0.5 and −0.25 → −0.5.

A pure grid snap can never displace a point by more than 0.25 cm.

Anchoring at the internal origin means that in a model whose Project Base Point sits at a non-round
offset — normal once a survey DWG has been acquired — project coordinates will still read dirty.
That was chosen deliberately: the internal origin is what the API hands back directly, and in any
model where the base point was never moved the two are identical.

### Straight lines

Applies to walls, structural framing, grids, reference planes, model lines and detail lines. Given
the line's direction:

| Case | Rule | Result |
|---|---|---|
| Within **0.5°** of 0° or 90° | Snap the direction exactly onto the axis; round the start point to the grid; round the **length** to the grid; end = start + direction × length | Both endpoints on grid, length a whole 0.5 cm, exactly axis-aligned |
| Within **0.5°** of 45° or 135° | Round both endpoints to the grid, then force \|Δx\| = \|Δy\|: take the mean of the two grid-rounded deltas, snap that mean to the grid, and give both deltas that value, keeping each one's sign | Exact 45°, both endpoints on grid. Length is Δx·√2 and cannot be round — accepted |
| Anything else (a genuine 30° wall) | Round both endpoints to the grid | Endpoints clean; angle and length land where they land |
| Not a `Line` (arc, spline) | Untouched | Reported |

For cardinal lines the end point is exactly on grid by construction: an axis-aligned direction times
a grid-multiple length, added to a grid point, is a grid point.

### The displacement cap

**If rounding would move any defining point of an element — either endpoint of a line, or the
insertion point of an instance — by more than 5 cm, the element is skipped and reported.**

Since a grid snap alone can never exceed 0.25 cm, this guard only ever fires on the straightening
step. It exists because angular tolerance scales badly with length: a 40 m wall sitting 0.49° off
axis is inside the 0.5° tolerance, but straightening it sweeps its far end 34 cm sideways. That is
not a cleanup, it is a redesign, and the tool must refuse it rather than perform it silently.

### Endpoint clustering

Independent snapping breaks joins. Two walls meeting at a corner share a point; if it reads 1.24 on
one wall and 1.26 on the other, they snap to 1.0 and 1.5 — a 5 mm gap where a clean corner was, and
Revit begins reporting that joined walls no longer intersect.

So before any snapping, every line endpoint in the run is collected and **clustered**: a union-find
over the graph of points within **1 cm** of each other. Each cluster's mean is snapped once, and
every member of the cluster is assigned that single result. Coincident points go in together and
come out together.

This is pure geometry over plain structs and is the single most important thing to get right, so it
carries the heaviest unit-test load — including the chaining case, where a, b, c each sit 0.9 cm
apart and transitively form one cluster spanning 1.8 cm.

### Point-based families

Columns, furniture, casework, generic models and other unhosted instances: round the insertion
point to the grid, and if the instance's rotation about Z is within 0.5° of a multiple of 45°, snap
it exactly onto that multiple. A family deliberately placed at 37.4° is left alone.

### Hosted families

Doors and windows are never moved in X/Y independently — on a diagonal wall that would push them
out of their host. Instead, after the wall pass has run and the host's line is already clean, the
instance's point is projected onto that line, its **distance from the wall's start point, measured
along the wall** is rounded to the grid, and it is moved along the wall direction only.

### Height

Levels are rounded. This was an explicit decision: it is the largest single change the button makes,
since every wall, floor and family hosted on a level rides along with it, but a level at 3.0012 m is
exactly the kind of dirt the tool exists to remove.

For other elements, Z is rounded through the element's own elevation/offset parameter where it has
one, and through the location point where it does not. Level and offset both being round makes the
absolute height round.

## Order of operations

Each pass depends on the ones before it having already run:

1. **Levels** — so everything downstream sits on clean heights.
2. **Grids and reference planes** — the lines the rest of the model was drawn against.
3. **Walls**, then **framing**, then **model and detail lines** — all through one shared clustering
   pass, so a wall corner, a grid intersection and a beam end that met before still meet after.
4. **Point-based unhosted families.**
5. **Doors and windows** — last, because they need their host wall already clean.
6. **Floors, roofs, ceilings** — measured and counted only.

## What is never touched

Each of these is recorded with its reason and shown in the summary:

- **Revit link instances**, and anything inside them.
- **Group members.** Editing one member rewrites every instance of that group — a different and
  worse kind of damage than the one being fixed.
- **Design-option elements.**
- **Elements on a workset the user does not own.**
- **Imported and linked CAD.** The traced DWG is the reference the model was built against; moving
  it would invert the whole point of the exercise.
- **Arcs and sketch-based elements** (v1 limitation).
- **Anything exceeding the 5 cm displacement cap.**
- **Anything Revit rejected** — a locked dimension or alignment it could not satisfy.

**Pinned elements are unpinned, moved, and re-pinned.** This overrides a deliberate decision someone
made, and was chosen knowingly: in practice pins are applied to imported CAD and to elements people
did not want nudged by accident, and the second group is exactly the group this tool exists to
correct. If the move fails after unpinning, the sub-transaction rollback restores the pin along with
the position.

## Architecture

Layout follows the repo rule — one folder per tool, namespace = root namespace + folder path:

```
src/RVTuk.Core/RoundPositions/
    GridSnapper          scalar/point snapping, cm↔ft conversion
    LineRounder          axis classification, the three line cases, the displacement cap
    EndpointClusterer    union-find over coincident endpoints
    GridPoint            small readonly struct — the pure passes' point type
    RoundingReport       counts + per-element outcomes
    SkipReason           the enumeration behind the summary's grouping

src/RVTuk.Revit/RoundPositions/
    RoundPositionsRunner            orchestrates the six passes
    ElementEligibility              group / design option / workset / link / pin checks
    LevelPass, LinePass, PointInstancePass, HostedInstancePass, SketchSurveyPass
    RoundingFailurePreprocessor     keeps Revit's warning dialogs out of a model-wide run
    Commands/RoundPositionsCommand  the ribbon entry, TransactionMode.Manual

src/RVTuk.UI/RoundPositions/
    ViewModels/RoundPositionsResultViewModel
    Views/RoundPositionsResultWindow

tests/RVTuk.Core.Tests/RoundPositions/
```

Every decision above — the grid, the three line cases, the cap, the clustering — lives in Core as
plain maths over plain structs, with no Revit type anywhere near it. The Revit project's passes
collect elements, hand geometry to Core, and write the results back.

`GridPoint` duplicates, in miniature, the `XyzPoint` that TopoTools carries on its own unmerged
branch. Rather than hoist a shared point type across a branch boundary, the duplication is accepted
for now; whoever merges the two branches can promote one to `Core/Shared/` if it proves worth it.

### Transactions and failure handling

One `Transaction` named "Round Positions" wraps the whole run, so the entire cleanup is a single
Ctrl+Z.

Inside it, each element **that actually needs to move** gets its own `SubTransaction`. Elements
already on grid never open one — in a mostly-clean model that is the majority, and it keeps the cost
proportional to the mess rather than to the model. If setting the location throws, or a constraint
cannot be satisfied, that one sub-transaction is rolled back and the element is recorded as skipped.
The model is never left half-moved.

The transaction's `FailureHandlingOptions` use `SetForcedModalHandling(false)` and
`SetClearAfterRollback(true)`, with a preprocessor that deletes warnings — without it, a model-wide
run drowns the user in dialogs about walls that no longer intersect.

### Threading

Everything runs inside the command's own API context and the results window is shown modally from
there — the same shape as the DWG Exporter's dialog. No `ExternalEvent`, no background thread, no
ping-pong.

### The summary window

Counts first: examined, moved, already clean, skipped. Then the skipped elements grouped by reason,
each row naming its category and element, with a button that selects a group in the model.

For an inherited consultant model that list *is* the product — it is the map of the mess a button
cannot fix.

## Testing

Core's xunit suite gains `tests/RVTuk.Core.Tests/RoundPositions/`:

- `GridSnapperTests` — positive and negative coordinates, exact 0.25 midpoints in both directions,
  cm↔ft round-tripping, the 0.25 cm maximum-displacement property.
- `LineRounderTests` — a wall at 0.49° vs one at 0.51° (straightened vs not); cardinal lines coming
  out with both a grid length and grid endpoints; 45° lines coming out with |Δx| = |Δy|; a genuine
  30° wall keeping its angle; the 5 cm cap firing on a long near-orthogonal wall.
- `EndpointClustererTests` — coincident points sharing one result; points straddling a grid boundary
  (the 1.24/1.26 corner) landing together; transitive chaining; points further apart than the
  cluster radius staying independent.

The Revit-side passes are not unit-testable; they are covered by the in-Revit checklist in the
implementation plan.

## Risks and open questions

**Speed.** A sub-transaction per moved element means a regeneration per moved element. On a large
inherited model this may be slow. This is deliberately not pre-optimised: the first in-Revit check
measures it. If it is unacceptable, the fallback is batching sub-transactions per category, which
costs precise per-element skip attribution.

**API behaviour that cannot be verified without Revit.** The following are designed against known
APIs but their real behaviour under a model-wide run is unproven, and each becomes an explicit
in-Revit check in the plan rather than a silent assumption:

- Setting `Level.Elevation` with a full building hosted on the level.
- Repositioning a `ReferencePlane`.
- Whether wall joins auto-adjust when `LocationCurve` is set, and whether clustering is sufficient
  to preserve them in practice.
- How a hosted door or window behaves when slid along its host wall.
- Whether the failure preprocessor suppresses everything a model-wide run can raise.

**Reference geometry for other tools.** "Detail lines" includes the `_DP-Dim Outer`,
`_DP-Dim Inner`, `_DP-Dim Ref` and `_DP-Topo Line` styles that Auto Dimensions and Topo Tools read
as input. Rounding them yields cleaner input for both tools, but it does mean this button moves
another tool's reference geometry, and a Round Positions run should be followed by a re-run of those
tools where they are in use.

## Registration and docs

- Ribbon: a fifth button on the existing "RVTuk" panel, with a generated icon in the established
  house style, added in `src/RVTuk.Revit/Application.cs`.
- No feature flag. The tool ships on.
- `docs/tools/round-positions/` gains `README.md` and `backlog.md` alongside this spec.
- CLAUDE.md's tool list, canonical-name list and v1 launch surface are updated.
- `docs/BACKLOG.md`'s per-tool backlog index gains a Round Positions entry.

## Branch note

This work sits on `round-positions`, branched from `master`. At the time of branching, `master` was
behind two unmerged feature branches — `topo-tools` and `dwg-exporter` — so `Application.cs` and
CLAUDE.md will conflict lightly when those land. The conflicts are additive (a ribbon button and a
tool-list entry each) and expected.
