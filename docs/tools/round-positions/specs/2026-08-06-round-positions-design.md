# Round Positions — Design

**Date:** 2026-08-06
**Status:** Approved (simplified 2026-08-06; scan-and-fix revision 2026-08-06 — see *Revisions*)
**Area:** Productivity

## Problem / motivation

Model geometry drifts off round numbers from four directions at once, and all four are live in our
projects: walls traced over an imported CAD survey (which also leaves them slightly *rotated*, not
just offset), models inherited from consultants, our own drafting drift when snapping is off or a
temporary dimension is typed carelessly, and clusters displaced by a copy/move with a non-round
delta. The result is a model where an element sits at 1.001, 5.001 instead of 1, 5.

We work in centimetres. Nothing in our drawings needs finer resolution than a centimetre, with the
optional exception of a half centimetre. A tool that pulls the model back onto that resolution
removes an entire class of tedious manual correction.

## Revisions

### Revision 1 — move things, don't reshape them

An earlier draft tried to fix *geometry* as well as position: straightening near-orthogonal walls
onto their axis and rounding their lengths. Planning that exposed a hard conflict. Straightening a
wall and preserving its corners are mutually exclusive under any per-element rule — a horizontal
wall controls its own Y, a vertical wall its own X, and when they share a corner each moves it
without telling the other, so the corner splits in two. Resolving that properly needs a global
rectification pass, which is a substantially larger algorithm than the tool is worth in v1.

**So v1 moves things; it does not reshape them.** No lengths change, no angles change, no endpoints
move independently of each other. Geometry repair is v2.

That also retired a 5 cm displacement cap an earlier draft carried. It existed to stop straightening
from yanking a long near-orthogonal wall sideways. With straightening gone, nothing can move further
than half a grid step on any axis, so the cap guarded against nothing.

### Revision 2 — scan first, fix by group

The first version was a one-click ribbon button that ran over the whole model with no dialog. A
scan-and-apply flow was considered at design time and declined in favour of that simplicity.

Running it changed the information. On an average-size model the first real run was slow enough that
**the user stopped it before it finished** — and because Revit gives a command no way to report
progress, a long run and a hang are indistinguishable. There was no number to judge by and no way to
tell whether waiting would help.

Two things follow, and the second matters more than the first:

1. **The cost was the committing, not the work.** Each moved element opened its own `SubTransaction`,
   and every sub-transaction commit makes Revit regenerate the document. A thousand dirty walls meant
   a thousand regenerations. See *Batching*.
2. **A scan is nearly free**, because reading geometry opens no transaction. So the tool can say
   "312 walls are off grid" before committing to anything, and can offer to fix a category or a
   level rather than everything at once.

The one-click button is therefore replaced by a **dockable pane**: scan, read the tree, fix what you
choose. This supersedes the "no scope or category control" non-goal from the previous version.

## Goals

- A **dockable pane** that scans the active model for elements off the grid and reports them as a
  Category → Level tree with counts.
- **Fix by group**: any node in the tree, or everything, in one action.
- The user chooses whether half centimetres are allowed.
- Elements that cannot be fixed are listed with the reason, never silently dropped.
- A fix is a single undo step.
- **Fast enough to be routine.** See *Batching*.

## Non-goals (all v2)

- **Wall lengths and endpoints.** A wall's length is whatever it was; only its position changes.
- **Straightening.** A wall at 89.97° stays at 89.97°, is reported, and is not fixed.
- **Sketch-based elements** — floors, roofs, ceilings. Their boundary is a `Sketch`, editable only
  through `SketchEditScope`, and sketch curves are frequently locked to wall faces.
- **Arcs**, stairs, railings.
- Rooms, areas, dimensions and tags — derived from what bounds them, they follow along on their own.
- KKarea / Revit 2023. This ships in `RVTuk.Revit` only, so 2024 and 2025.

## The grid

Anchored at **Revit's internal origin**, in X, Y and Z. The step is the user's choice:

| Checkbox | Step | 0.1 → | 0.34 → | 0.5 → |
|---|---|---|---|---|
| **Allow half centimetres** (default, on) | 0.5 cm | 0.0 | 0.5 | 0.5 |
| off | 1.0 cm | 0.0 | 0.0 | 1.0 |

Snapping is `Math.Round(v / step, MidpointRounding.AwayFromZero) * step`, performed in centimetres
and converted back to Revit's internal decimal feet (1 ft = 30.48 cm, exactly). `AwayFromZero` rather
than .NET's default keeps exact midpoints symmetric across the origin: on the 1 cm grid 0.5 → 1.0 and
−0.5 → −1.0.

Nothing can therefore move more than half a step on any one axis.

The choice is remembered between sessions in `AppConfig`, stored **inverted** as
`RoundPositionsWholeCentimetresOnly` (false meaning half centimetres are allowed), because net48's
`DataContractJsonSerializer` skips property initialisers, so a property defaulting to `true` would
silently flip to `false` when an older config file is loaded. Same trick as `DwgExportDwgOff`.

### Base points

Because the grid is anchored to the internal origin, a Project Base Point at a non-round offset would
leave every *project* coordinate dirty even after a clean run. So the **Project Base Point and Survey
Point are themselves snapped**, which makes project and survey coordinates round for free.

This is the highest-consequence thing the tool does:

- Moving the Survey Point is **not a local change**. Every linked model positioned by shared
  coordinates shifts relative to this one, as does anything exported by shared coordinates.
- **Only unclipped points are touched**, where the move relocates the marker rather than the
  coordinate system; a clipped point is reported and left alone. That the clip parameter reads this
  way is an in-Revit check before the pass is trusted, not a design assumption.

## What v1 moves

### Point-positioned family instances

Unhosted instances with a `LocationPoint` — columns, furniture, casework, generic models, plumbing
fixtures. The insertion point is snapped in X, Y and Z. Where the family carries an elevation/offset
parameter, Z is rounded through that parameter, so level + offset both being round makes the absolute
height round.

If the rotation about Z is within **0.5°** of a multiple of 45°, it is snapped onto that multiple. A
family deliberately placed at 37.4° is left alone.

### Straight lines

Walls, structural framing, grids, reference planes, model lines and detail lines.

**Only exactly-orthogonal lines are moved**, and they move **parallel to themselves**: the coordinate
the line does *not* run along is snapped, and the along-axis coordinates are left exactly as they
were. A horizontal wall from (10.3, 5.2) to (410.9, 5.2) becomes (10.3, 5.0) to (410.9, 5.0).

"Exactly orthogonal" is a **displacement** test, not an angle test: a line runs along X iff its Y
extent is at most **0.01 cm** and its X extent is more than that. An angular tolerance would scale
with length — 0.01° is float dust on a 40 cm wall and 0.7 cm of real drift on a 40 m one — so the
same number would quietly mean two different things.

**A wall at 89.97° is reported, not fixed.** On a model traced over CAD, expect that list to be long.
It is the v2 work queue.

Z is not touched by this pass. A wall's height comes from its level and base offset.

**Corners.** Two joined orthogonal walls control different coordinates — the corner is (vertical's X,
horizontal's Y) — and since the along-axis ends never move, each wall's end can sit up to half a step
from the other's new axis position. Revit maintains wall joins by extending and trimming the joined
ends, so joined walls stay joined; unjoined walls that merely touched can open a gap of up to half a
step. Verified in Revit, not assumed.

### Wall-hosted instances

Doors and windows, fixed **after** their host wall and **only for walls that were themselves fixed or
already clean** — never for a wall that was reported. The instance's point is projected onto the host
wall's line, its distance from the wall's start point measured along the wall is snapped, and it moves
along the wall direction only. It can never leave its host.

### Levels

Level elevations are snapped. This is the largest single change the tool makes — every wall, floor and
family hosted on a level rides along with it — and it was chosen deliberately.

## What is never touched

Each is reported with its reason:

- **Revit link instances**, and anything inside them.
- **Imported and linked CAD.** The traced DWG is the reference the model was built against.
- **Group members.** Editing one member rewrites every instance of that group.
- **Design-option elements.**
- **Elements owned by another user** (`CheckoutStatus.OwnedByOtherUser`; `NotOwned` is fine, it means
  nobody holds it).
- **Non-orthogonal lines**, arcs, sketch-based elements (all v2).
- **Base points whose clip state is unexpected.**
- **Anything Revit rejected** after the constraint retry below.

## Pins and constraints

**Pinned elements are unpinned, moved, and re-pinned.** If the move fails after unpinning, the
sub-transaction rollback restores the pin along with the position.

**Constraints are released only on demand, never pre-emptively.** The tool attempts the move first.
Only if Revit refuses does it look at what is holding the element:

- A **locked dimension** is *unlocked*, not deleted. The dimension stays on the drawing and stops
  fighting — the constraint goes, the annotation and its design intent do not.
- A true **alignment-lock constraint** (`OST_Constraints`) exists only to constrain, so it is deleted.

Then the move is retried once. If it still fails, the element is reported.

Every release is listed, by host element, because **nothing in a Revit model shows what *used* to be
constrained** — that list is the only record the change happened. It is recorded only after the
release has actually committed, so it can never name a constraint that a rollback put back.

## Scan and fix

### The scan

Read-only: it opens no transaction and triggers no regeneration, so it can be re-run freely. For each
candidate element it applies the eligibility gate, reads the geometry, asks Core for the delta, and
sorts the element into one of three buckets: **already on grid**, **dirty** (with its category and
level), or **cannot be fixed** (with a reason).

The scan runs on demand, not automatically when the pane opens — a docked pane may sit open all day,
and a full traversal must run on Revit's main thread. The one exception: after a fix, the pane re-runs
a **full** scan, not a partial refresh of the node that was fixed. A fix can move things outside the
node it targeted — a level carries its whole floor, a wall drags its openings — so refreshing only
the fixed node would leave the rest of the tree quietly wrong. The scan is cheap; correctness is
worth more than the saving.

### The fix

Acts on whatever the chosen tree node covers. **The scan is a snapshot to decide from, never a list of
instructions to replay**: every element is re-read before it is moved, and one that has been deleted,
or has since become clean, is passed over rather than treated as an error. The model can change
between scan and fix — the user draws a wall, a colleague syncs, an undo lands — and the fix must
survive that.

One `Transaction` per fix action, so each fix is one Ctrl+Z.

### Batching

Revit regenerates when a **sub-transaction commits**, not on each API call. So elements are moved in
batches inside a single sub-transaction — one batch costs one regeneration instead of one per element.

Batch size is a single named constant, **starting at 250**. It is a guess, not a measurement: bigger
batches mean fewer regenerations but a more expensive fallback when one fails. The first in-Revit
timing decides whether it moves.

If any element in a batch throws, the **whole batch rolls back** and is re-run element by element.
That restores exact per-element attribution, and pays the slow path only for the batch that actually
contained trouble. On a clean model the fallback never runs.

Constraint release is never batched — it has to know which element it is releasing for, and it only
runs after a refusal, which is already the per-element path.

`FailureHandlingOptions` use `SetForcedModalHandling(false)` and `SetClearAfterRollback(true)` with a
preprocessor that deletes warnings; without it a model-wide fix drowns the user in dialogs.

**The transaction's commit status is checked.** `Transaction.Commit()` returns `RolledBack` when
failures are unresolved, and a report claiming work that Revit then discarded would be worse than no
report at all — particularly for the constraint list, which is the only record those constraints
existed.

## The pane

Registered like the Auto Dimensions and Topo Tools panes, opened by the ribbon button.

- **Controls** — *Allow half centimetres*, a **Scan** button, and a status line: when the last scan
  ran and what the last fix did.
- **The tree** — three tiers: **Category → Level → element**. Counts on the two group tiers, a Fix
  button on both of them, and **Fix all** above the tree. Element rows exist for **inspection only** —
  they name the element and select it in the model, and carry no Fix button of their own. Fixing is a
  group action; picking off single elements is not the workflow.
- **Cannot be fixed** — grouped by reason, with counts and select-in-model. This is the v2 queue.

Selecting any node — category, level or element — selects the elements it covers in the model. The
pane is not modal, so that is the point: scan, look at the model, fix a group, look again.

Element rows are collapsed by default and a level can hold hundreds, so the element tier must
virtualize. An `ItemsControl` inside a `ScrollViewer` realises every row it is given, which on a
consultant model means thousands of controls built at expand time.

### Threading

The view model runs on the WPF thread and sees only `Func<>`/`Action` delegates — no Revit type
crosses into `RVTuk.UI`. Each delegate raises an `ExternalEvent` and blocks on a
`ManualResetEventSlim` until Revit's main thread has done the work, the same ping-pong the other two
panes use. This applies to the scan as well: read-only or not, the Revit API may only be touched on
its own thread.

## Order of operations

Within a fix, when more than one category is involved:

1. **Base points** — so everything below is measured against a clean origin.
2. **Levels** — so everything hosted sits on clean heights.
3. **Grids and reference planes** — the datums the model was drawn against.
4. **Walls**, then **structural framing**, then **model and detail lines**.
5. **Point-positioned unhosted families.**
6. **Wall-hosted families** — last, because they need their host resolved.

## Architecture

One folder per tool, namespace = root namespace + folder path:

```
src/RVTuk.Core/RoundPositions/
    GridPoint, GridSnapper          the grid, cm↔ft
    AxisClassifier                  is this direction orthogonal
    LineOffset                      the parallel-offset rule
    ScanResult, DirtyElement        the scan's output, and its Category → Level grouping
    Batches                         splitting a list into fixed-size batches
    RoundingReport, SkipReason      what a fix did, and why elements were left

src/RVTuk.Revit/RoundPositions/
    ElementEligibility              link / import / group / design option / workset checks
                                    (deliberately NOT pins — see Pins and constraints)
    ConstraintReleaser              unlock-or-delete, returning what it released
    RoundingFailurePreprocessor     keeps Revit's warning dialogs out of a bulk fix
    MoveAttempt                     one element: unpin, apply, re-pin, release, retry
    BatchMover                      optimistic batch, pessimistic per-element fallback
    <Category>Scanner / Applier     per category, per the passes above
    RoundPositionsRunner            orchestrates scan and fix
    RoundPositionsPaneProvider      pane registration
    ExternalEvents/                 scan, fix, select handlers
    Commands/RoundPositionsCommand  the ribbon entry — opens the pane

src/RVTuk.UI/RoundPositions/
    ViewModels/RoundPositionsPaneViewModel
    Views/RoundPositionsPaneView

tests/RVTuk.Core.Tests/RoundPositions/
```

Snapping, axis classification, the offset rule, the scan grouping and batch splitting all live in
Core as plain functions over plain structs, with no Revit type near them. That is the unit-tested
part. The Revit side collects elements, hands geometry to Core, and writes results back.

## Testing

`tests/RVTuk.Core.Tests/RoundPositions/` covers:

- `GridSnapperTests` — both steps, negatives, exact midpoints both directions, cm↔ft round-trip, the
  half-step maximum-displacement property.
- `AxisClassifierTests` — exact axes, float dust inside tolerance, 89.97° rejected, the boundary value
  itself (which side of the tolerance counts as dust).
- `LineOffsetTests` — horizontal and vertical offsets, the along-axis coordinates untouched, the start
  point as anchor, already-on-grid, non-orthogonal rejected with no delta.
- `ScanResultTests` — Category → Level grouping, counts, ordering, an empty scan, and an element with
  no level (a grid, a base point) still appearing under its category rather than being dropped.
- `BatchesTests` — empty, fewer than one batch, an exact multiple, a remainder.

The Revit-side scanners, appliers and the pane are not unit-testable — they take `Document` and
`Element`, which cannot exist outside a Revit process. They are covered by an in-Revit checklist.

## Risks and open questions

**Whether batching delivers.** The design assumes Revit defers regeneration across a sub-transaction,
so one batch costs one regeneration. That is standard practice for bulk Revit edits, but it is
unmeasured here — the last timing attempt was abandoned. **The first in-Revit check times a fix and
records the number**, before anything else is optimised.

**API behaviour that cannot be verified without Revit.** Each is an explicit in-Revit check:

- Whether a refused move **throws** or **posts an error**. The whole constraint-retry design rests on
  it throwing. Sub-transactions run no failure processing, so a posted error would commit the
  sub-transaction and surface only at the outer commit, bypassing the retry entirely.
- Whether joined walls stay joined when one is translated.
- Project Base Point / Survey Point clip-state semantics, and what moving each actually moves.
- Setting `Level.Elevation` with a full building hosted on the level.
- Repositioning a `Grid` and a `ReferencePlane` (both move by transform, not by setting a curve).
- Whether unlocking a `Dimension` and deleting `OST_Constraints` elements actually releases the
  element, and whether the retry then succeeds.
- What `Transaction.Commit()` returns under `SetForcedModalHandling(false)` — `RolledBack`, or
  `Pending`, which is neither committed nor rolled back.

**Reference geometry for other tools.** "Detail lines" includes the `_DP-Dim` and `_DP-Topo Line`
styles that Auto Dimensions and Topo Tools read as input. Rounding them gives both tools cleaner
input, but a fix should be followed by a re-run of those tools where they are in use.

## Registration and docs

- Ribbon: a fifth button on the "RVTuk" panel, opening the pane, with a generated icon in the house
  style, added in `src/RVTuk.Revit/Application.cs`.
- No feature flag. The tool ships on.
- `docs/tools/round-positions/` carries `README.md`, `backlog.md`, this spec and the plans, with the
  v2 list (wall lengths, straightening, sketch-based elements, arcs) seeded into the backlog.
- CLAUDE.md's tool list, canonical-name list and v1 launch surface are updated.
- `docs/BACKLOG.md`'s per-tool backlog index gains a Round Positions entry.

## Branch note

This work sits on `round-positions`, branched from `master`. At branching, `master` was behind two
unmerged feature branches — `topo-tools` and `dwg-exporter` — so `Application.cs` and CLAUDE.md will
conflict lightly when those land. The conflicts are additive and expected.
