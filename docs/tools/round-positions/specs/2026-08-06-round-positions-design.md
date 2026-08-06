# Round Positions — Design

**Date:** 2026-08-06
**Status:** Approved (simplified 2026-08-06 — see *Revision*)
**Area:** Productivity

## Problem / motivation

Model geometry drifts off round numbers from four directions at once, and all four are live in our
projects: walls traced over an imported CAD survey (which also leaves them slightly *rotated*, not
just offset), models inherited from consultants, our own drafting drift when snapping is off or a
temporary dimension is typed carelessly, and clusters displaced by a copy/move with a non-round
delta. The result is a model where an element sits at 1.001, 5.001 instead of 1, 5.

We work in centimetres. Nothing in our drawings needs finer resolution than a centimetre, with the
optional exception of a half centimetre. A one-click command that pulls the model back onto that
resolution removes an entire class of tedious manual correction.

## Revision — what this version is not

An earlier draft of this spec tried to fix *geometry* as well as position: straightening
near-orthogonal walls onto their axis and rounding their lengths. Planning that exposed a hard
conflict. Straightening a wall and preserving its corners are mutually exclusive under any
per-element rule — a horizontal wall controls its own Y, a vertical wall controls its own X, and
when they share a corner each moves it without telling the other, so the corner splits in two.
Resolving that properly needs a global rectification pass (cluster the axis values, then anchor the
along-axis endpoints against the perpendicular walls' assigned positions), which is a substantially
larger algorithm than the tool is worth in v1.

**So v1 moves things; it does not reshape them.** Every element is translated onto the grid and
nothing else. No lengths change, no angles change, no endpoints move independently of each other.
Geometry repair — wall lengths, straightening near-orthogonal walls, sketch-based elements — is v2.

That also retires the 5 cm displacement cap the earlier draft carried. It existed to stop
straightening from yanking a long near-orthogonal wall sideways. With straightening gone, nothing
can move further than half a grid step on any axis, so the cap guards against nothing.

## Goals

- A ribbon button, **"Round Positions"**, opening a one-checkbox dialog, that pulls the whole active
  model onto a whole-centimetre grid and reports afterwards what it did.
- The user chooses whether half centimetres are allowed.
- Elements the tool must not or cannot touch are skipped and reported, never half-moved.
- The whole run is a single undo step.

## Non-goals (all v2)

- **Wall lengths and endpoints.** A wall's length is whatever it was; only its position changes.
- **Straightening.** A wall at 89.97° stays at 89.97°. It is skipped entirely (see *Straight lines*).
- **Sketch-based elements** — floors, roofs, ceilings. Their boundary is a `Sketch`, editable only
  through `SketchEditScope`, and sketch curves are frequently locked to wall faces, so edits are
  either overridden or throw.
- **Arcs**, stairs, railings.
- Rooms, areas, dimensions and tags — derived from what bounds them, they follow along on their own.
- Any scope or category control. The button is all-or-nothing over the active model.
- KKarea / Revit 2023. This ships in `RVTuk.Revit` only, so 2024 and 2025.

## The grid

Anchored at **Revit's internal origin**, in X, Y and Z. The step is the user's choice:

| Checkbox | Step | 0.1 → | 0.34 → | 0.5 → |
|---|---|---|---|---|
| **Allow half centimetres** (default, on) | 0.5 cm | 0.0 | 0.5 | 0.5 |
| off | 1.0 cm | 0.0 | 0.0 | 1.0 |

Snapping is `Math.Round(v / step, MidpointRounding.AwayFromZero) * step`, performed in centimetres
and converted back to Revit's internal decimal feet (1 ft = 30.48 cm, exactly). `AwayFromZero`
rather than .NET's default keeps exact midpoints symmetric across the origin: on the 1 cm grid
0.5 → 1.0 and −0.5 → −1.0.

Nothing can therefore move more than half a step on any one axis.

The choice is remembered between sessions in `AppConfig`. It is stored **inverted** — as
`RoundPositionsWholeCentimetresOnly`, false meaning half centimetres are allowed — because net48's
`DataContractJsonSerializer` skips property initialisers, so a property defaulting to `true` would
silently flip to `false` when an older config file is loaded. This is the same trick
`DwgExportDwgOff` already uses.

### Base points

Because the grid is anchored to the internal origin, a Project Base Point sitting at a non-round
offset would leave every *project* coordinate dirty even after a clean run. So the **Project Base
Point and Survey Point are themselves snapped onto the grid** when they are off it, which makes
project and survey coordinates round for free.

This is the highest-consequence thing the tool does and is treated accordingly:

- Moving the Survey Point is **not a local change**. Every linked model positioned by shared
  coordinates shifts relative to this one, as does anything exported by shared coordinates.
- **Clipped vs unclipped changes what "move" means** for both points — one moves the coordinate
  system, the other moves the marker. The tool reads the clip state and **refuses to touch a point
  whose clip state it does not expect**, reporting it instead. The expected behaviour is an
  in-Revit check, not a design assumption.

## What v1 moves

### Point-positioned family instances

Unhosted instances with a `LocationPoint` — columns, furniture, casework, generic models, plumbing
fixtures. The insertion point is snapped in X, Y and Z. Where the family carries an elevation/offset
parameter, Z is rounded through that parameter instead, so level + offset both being round makes the
absolute height round.

If the instance's rotation about Z is within **0.5°** of a multiple of 45°, it is snapped exactly
onto that multiple. A family deliberately placed at 37.4° is left alone.

### Straight lines

Walls, structural framing, grids, reference planes, model lines and detail lines.

**Only exactly-orthogonal lines are moved**, and they are moved **parallel to themselves**: the
coordinate the line does *not* run along is snapped, and the along-axis coordinates are left exactly
as they were. A horizontal wall from (10.3, 5.2) to (410.9, 5.2) becomes (10.3, 5.0) to (410.9, 5.0)
— the line lands clean, its length is untouched, and nothing along it shifts.

"Exactly orthogonal" means within **0.01°** of 0° or 90° — tight enough to mean *actually
orthogonal*, loose enough to absorb float dust in Revit's stored geometry.

**A wall at 89.97° is skipped and reported.** This is deliberate and it is the main limitation of
v1: translating a near-orthogonal wall does not make it orthogonal, and straightening it is exactly
the geometry repair deferred to v2. On a model traced over CAD, expect a large skip list. That list
is the v2 work queue.

Z is not touched by this pass. A wall's height comes from its level and base offset, which the level
pass and the offset parameters handle.

**Corners.** Two joined orthogonal walls end up controlling different coordinates — the corner is
(vertical's X, horizontal's Y) — and since the along-axis ends never move, each wall's end can sit
up to half a step from the other's new axis position. Revit maintains wall joins by extending and
trimming the joined ends, so joined walls stay joined; unjoined walls that merely touched can open a
gap of up to half a step. Verified in Revit, not assumed.

### Wall-hosted instances

Doors and windows, run **after** the wall pass and **only for walls that pass actually moved or
found already clean** — never for a wall that was skipped. The instance's point is projected onto its
host wall's line, its distance from the wall's start point measured along the wall is snapped, and it
is moved along the wall direction only. It can never leave its host.

### Levels

Level elevations are snapped. This is the largest single change the button makes — every wall, floor
and family hosted on a level rides along with it — and it was chosen deliberately.

## What is never touched

Each is recorded with its reason and shown in the summary:

- **Revit link instances**, and anything inside them.
- **Imported and linked CAD.** The traced DWG is the reference the model was built against; moving
  it would invert the point of the exercise.
- **Group members.** Editing one member rewrites every instance of that group.
- **Design-option elements.**
- **Elements owned by another user** in a workshared model (`CheckoutStatus.OwnedByOtherUser`;
  `NotOwned` is fine, it means nobody holds it).
- **Non-orthogonal lines**, arcs, sketch-based elements (all v2).
- **Base points whose clip state is unexpected.**
- **Anything Revit rejected** after the constraint retry below.

## Pins and constraints

**Pinned elements are unpinned, moved, and re-pinned.** If the move fails after unpinning, the
sub-transaction rollback restores the pin along with the position.

**Constraints are removed only on demand, never pre-emptively.** The tool attempts the move first.
Only if Revit rejects it does it look at what is holding the element, and then:

- A **locked dimension** is *unlocked*, not deleted. The dimension stays on the drawing and simply
  stops fighting — the constraint is gone, the annotation and its design intent are not.
- A true **alignment-lock constraint** (`OST_Constraints`), which has no purpose beyond constraining,
  is deleted.

Then the move is retried once. If it still fails, the element is skipped and reported.

Every constraint unlocked or deleted is listed in the summary, by element. Nothing in a Revit model
shows what *used to* be constrained, so that list is the only record that the change happened.

## Order of operations

1. **Base points** — Project Base Point and Survey Point, so everything below is measured against a
   clean origin.
2. **Levels** — so everything hosted sits on clean heights.
3. **Grids and reference planes** — the datums the model was drawn against.
4. **Walls**, then **structural framing**, then **model and detail lines**.
5. **Point-positioned unhosted families.**
6. **Wall-hosted families** — last, because they need their host wall already resolved.

## Architecture

Layout follows the repo rule — one folder per tool, namespace = root namespace + folder path:

```
src/RVTuk.Core/RoundPositions/
    GridPoint            plain point in centimetres
    GridSnapper          scalar/point snapping at a chosen step, cm↔ft conversion
    AxisClassifier       is this direction orthogonal, and along which axis
    LineOffset           the parallel-offset rule for one line
    RoundingReport       counts, moved/skipped records, constraint records
    SkipReason           the enumeration behind the summary's grouping

src/RVTuk.Revit/RoundPositions/
    RoundPositionsRunner            orchestrates the six passes
    ElementEligibility              link / import / group / design option / workset / pin checks
    ConstraintReleaser              unlock-or-delete-then-retry
    BasePointPass, LevelPass, DatumPass, LinePass, PointInstancePass, HostedInstancePass
    RoundingFailurePreprocessor     keeps Revit's warning dialogs out of a model-wide run
    Commands/RoundPositionsCommand  the ribbon entry, TransactionMode.Manual

src/RVTuk.UI/RoundPositions/
    ViewModels/RoundPositionsViewModel, RoundPositionsResultViewModel
    Views/RoundPositionsWindow (the checkbox + Run), RoundPositionsResultWindow

tests/RVTuk.Core.Tests/RoundPositions/
```

Snapping, axis classification and the parallel-offset rule live in Core as plain maths over plain
structs, with no Revit type near them. That is the unit-tested part. The Revit passes collect
elements, hand geometry to Core, and write results back.

### Transactions and failure handling

One `Transaction` named "Round Positions" wraps the run, so the cleanup is a single Ctrl+Z.

Inside it, each element **that actually needs to move** gets its own `SubTransaction`; elements
already on grid never open one. If setting the location throws, the sub-transaction rolls back, the
constraint-release retry runs, and on a second failure the element is recorded as skipped. The model
is never left half-moved.

`FailureHandlingOptions` use `SetForcedModalHandling(false)` and `SetClearAfterRollback(true)`, with
a preprocessor that deletes warnings — without it a model-wide run drowns the user in dialogs.

### Threading

Everything runs inside the command's own API context and both windows are shown modally from there —
the same shape as the DWG Exporter's dialog. No `ExternalEvent`, no background thread.

### The windows

**Before:** one checkbox, *Allow half centimetres*, plus Run and Cancel.

**After:** counts — examined, moved, already clean, skipped — then the skipped elements grouped by
reason, each row naming its category and element, with a button that selects a group in the model.
Constraints released are their own group. For an inherited model that list is the product: it is the
map of what a v1 button cannot fix.

## Testing

Core's xunit suite gains `tests/RVTuk.Core.Tests/RoundPositions/`:

- `GridSnapperTests` — both steps; positive and negative coordinates; exact midpoints in both
  directions on both grids; cm↔ft round-tripping; the half-step maximum-displacement property.
- `AxisClassifierTests` — exact 0°/90°/180°/270°; float dust inside 0.01°; 89.97° classified as
  *not* orthogonal; 45° not orthogonal; the 359.99° wrap.
- `LineOffsetTests` — a horizontal line keeping both X and gaining a clean Y; a vertical line
  keeping both Y; an already-clean line reporting no movement; a diagonal line rejected.

The Revit passes are not unit-testable; they are covered by the in-Revit checklist in the plan.

## Risks and open questions

**Speed.** A sub-transaction per moved element means a regeneration per moved element. On a large
model this may be slow. Deliberately not pre-optimised — the first in-Revit check measures it. The
fallback is batching sub-transactions per category, which costs per-element skip attribution.

**API behaviour that cannot be verified without Revit.** Designed against known APIs, but unproven
under a model-wide run, and each an explicit in-Revit check rather than a silent assumption:

- Project Base Point / Survey Point clip-state semantics, and what moving each actually moves.
- Setting `Level.Elevation` with a full building hosted on the level.
- Repositioning a `Grid` and a `ReferencePlane` (both move by transform, not by setting a curve).
- Whether wall joins auto-adjust when a joined wall is translated.
- How a hosted door or window behaves when slid along its host wall.
- Whether unlocking a `Dimension` and deleting `OST_Constraints` elements actually releases the
  element, and whether the retry then succeeds.
- Whether the failure preprocessor suppresses everything a model-wide run can raise.

**Reference geometry for other tools.** "Detail lines" includes the `_DP-Dim Outer`,
`_DP-Dim Inner`, `_DP-Dim Ref` and `_DP-Topo Line` styles that Auto Dimensions and Topo Tools read as
input. Rounding them gives both tools cleaner input, but a Round Positions run should be followed by
a re-run of those tools where they are in use.

## Registration and docs

- Ribbon: a fifth button on the existing "RVTuk" panel, with a generated icon in the house style,
  added in `src/RVTuk.Revit/Application.cs`.
- No feature flag. The tool ships on.
- `docs/tools/round-positions/` gains `README.md` and `backlog.md` alongside this spec, with the v2
  list (wall lengths, straightening, sketch-based elements, arcs) seeded into the backlog.
- CLAUDE.md's tool list, canonical-name list and v1 launch surface are updated.
- `docs/BACKLOG.md`'s per-tool backlog index gains a Round Positions entry.

## Branch note

This work sits on `round-positions`, branched from `master`. At the time of branching, `master` was
behind two unmerged feature branches — `topo-tools` and `dwg-exporter` — so `Application.cs` and
CLAUDE.md will conflict lightly when those land. The conflicts are additive (a ribbon button and a
tool-list entry each) and expected.
