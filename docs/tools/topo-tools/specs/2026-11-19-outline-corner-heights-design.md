# Topo Tools — the toposolid's outline corners follow the lines

Date: 2026-11-19. Status: approved by Daniel on 2026-11-19. Comes from Daniel's Revit 2024 check of
2026-11-18 (branch `topo-tools-finish`, PR tchonky/RVTuk#19). Marks: **(yours)** = Daniel decided
it; **(my call)** = proposed by Claude, open to correction. Reference implementation of the rule:
[2026-11-19-outline-corner-heights-reference.py](2026-11-19-outline-corner-heights-reference.py)
(`corner_height`, `prepare`, `make_bridges`; the rest of that file is the test run and needs the
scratch harness, which is not kept). Test vectors:
`tests/RVTuk.Core.Tests/TopoTools/Fixtures/outline-corner-heights-vectors.txt` (plain text: scenes, lines, expected bridges, points).

## Why

Update puts points only along the topo lines. The edited element's own vertices keep their height,
so a corner of the outline (or of a cut-out) between lines at 41.00 and 43.00 stays at the
toposolid's original 41.02 and the surface folds sharply around it; one corner in Daniel's
screenshot sits 0.28 m from the end of the 43.00 line and stays 2 m lower. Daniel: "calculate what
would be the approximate height for those points taking into consideration the points that the
user creates nearby" (yours).

Today (traced in the code, 2026-11-19): the only own vertex Update changes is one that an edge
point lands on within 0.1 mm; it is raised and never recorded (README "The one exception", backlog
"keeps the line's height after the line moves"). `SlabShapeVertexType` is read nowhere.

## Goals, non-goals, anti-goals

- **Goals:** every outline point the tool did not place gets the height the lines around it imply,
  at every Update and Accept; a point no line influences any more goes back to its own height; the
  same corner gets the same height in the session of each toposolid that shares it.
- **Non-goals:** points inside the outline (survey imports, points placed with Modify Sub
  Elements) — never changed (yours: "Outline only"); writing any element but the one being edited;
  a constrained triangulation of the whole surface.
- **Anti-goals:** a corner height that depends on the order or direction the lines were drawn in,
  or on the previous Update's result; touching a point the ledger records for any line; a floor
  none of whose corners takes a height from the lines getting its shape editor switched on.

## What the user sees

- After Update or Accept, each outline corner (and any other outline point the tool did not place)
  sits at the height of the nearest line, moved towards the next line of another height it can see
  beyond itself, in proportion to the two distances. Where lines of one height stop on both sides
  of a gap (a building, a cut-out, a path), they count as one line drawn straight across.
  - Daniel's three corners: 41.20, 41.50, 42.89 (today 41.02, 41.02, 41.02).
  - A corner with no line beyond its nearest line keeps its own height — unless it is within 5 cm
    of that line, then it takes the line's height (my call: two outlines sketched a few mm apart do
    not open a crack).
  - **No reach limit** (yours, 2026-11-19): a corner 30 m from every line still moves if lines lie
    on both sides of it.
- When no line influences a corner any more (lines deleted, moved away, or the corner now has
  nothing beyond its nearest line), it goes back to the height it had before the tool first moved
  it.
- A corner moved by hand (Modify Sub Elements) after the tool set it is left alone from then on,
  and the summary says so once (my call, like "moved by hand" for line points).
- **Cancel** puts every corner the session moved back where it was when the session started (my
  call): the session keeps a copy of the corner record taken at its start, as it does for typed-over
  texts, and Cancel restores each corner whose record changed since (to the start's setZ, or to ownZ
  when the start had no record; a corner flagged as moved by hand is left alone). It collects no
  lines, so it can never raise a corner the session did not touch, and the corners match the texts
  Cancel puts back. The points of lines that survive keep their last-Update heights, as today; the
  next Update reconciles both.
- **Summary:** Accept's summary adds "N corners of the outline took their height from the lines"
  and "N corners went back to their own height", when not zero. Neither counts as needing
  attention, so Update stays quiet (spec 2026-11-17 item 12). "N corners moved by hand are left
  alone" does count. Exact copy in "Left to the builder".
- Floors being edited get the same.

## The rule (exact; metres; every number is part of it)

Inputs: every topo line of the session's plan view that has a readable height, **as drawn** (not
clipped to the element), each one plan polyline with one height; arcs tessellated by us to a chord
deviation of at most 1 mm (n = ceil(sweep / (2·acos(1 − 0.001/r))), not `Curve.Tessellate`, whose
tolerance is unchecked). V is one outline point the tool did not place (see "Which points").

-1. **Cleaning.** Convert to metres once; subtract a fixed local origin (the lines' bounding-box
    minimum rounded to whole metres). Drop consecutive vertices closer than 1e-6 m, then any line
    with fewer than 2 vertices. Round each height to a whole number of tenths of a millimetre; "same
    height" means the two differ by at most 5 tenths.
0.  **Bridges**, once per Update, from the drawn lines only. A line is closed when its first and last
    points are within 1 mm; only open lines have ends. An end E is free when no other line of the
    same height passes within 1 mm of it (its own line and bridges do not count). Its direction t_E
    is the unit vector from the first vertex at least 1 mm back along the line to E (no such vertex:
    the end is not free). Free ends E and F (different ends, possibly of one line) face each other
    when they have the same height, |EF| > 1 mm, (F−E)·t_E ≥ cos 60°·|EF| and (E−F)·t_F ≥ cos 60°·|EF|.
    Each free end picks the nearest free end facing it (distances within 1e-9 m: the smaller x of
    the other end, then the smaller y). When E and F pick each other, take the segment from
    E + 1 mm·w to F − 1 mm·w (w = (F−E)/|EF|; whichever way round it comes out); if it touches no
    drawn line whose height differs from max(zE, zF) by more than 0.5 mm, add the bridge [E, F] at
    height max(zE, zF). Bridges come after the drawn lines, in the order found; they take part in
    steps 1–5 like drawn lines, and are never placed, stored or drawn.
1.  For every line i: d_i = plan distance from V to the polyline, P_i = its nearest point.
2.  If some d_i ≤ 1 mm: h = the highest such line's height. Done.
3.  dmin = the smallest d_i; T = lines with d_i ≤ dmin + 1e-9; keep those within 0.5 mm of the
    highest height in T; L1 = the one whose P_i is smallest by (x, then y). h1, d1, P1;
    u = (V − P1)/d1. The far side is {P : (P − V)·u − 1e-9 > 0}.
4.  Clip every segment to the far side (cut at t = s_A/(s_A − s_B)); ignore parts no longer than
    1e-9 m. Q_j = the nearest point of line j's far part, e_j = |Q_j − V|; far_min = the smallest e_j.
    a. No line has a far part: h = h1 when d1 ≤ 0.05 m + 1e-9; otherwise V keeps its height. Done.
    b. Candidates: lines with a far part whose height differs from h1 by more than 0.5 mm. Order:
       repeatedly take the smallest e (within 1e-9: the higher height, then list order).
    c. t_same = the smallest t > 0 at which V + t·u meets a segment of a line of h1's height (L1 and
       bridges included); a vertex W of such a line with |cross(u, W − V)| ≤ 1 mm counts at
       t = (W − V)·u > 0; a segment with |cross(u, d)| ≤ 1e-12·|d| is skipped (the vertex clause
       covers it). None: infinite.
    d. For each candidate in order: if e_j ≤ far_min + 1e-9 it is L2; else if e_j > t_same there is
       no L2; else, unless |VQ_j| ≤ 2 mm, skip it when the segment from V to 1 mm short of Q_j
       touches any other line (drawn or bridge). The first not skipped is L2. All skipped: no L2.
5.  L2 found: h = h1 + (h2 − h1)·d1/(d1 + e2). No L2: h = h1.

"Touching": two closed segments touch when their distance is ≤ 1e-9 m — 0 when the strict
orientation signs show a proper crossing, else the smallest of the four endpoint-to-segment
distances (no exact `orientation == 0` tests), after a bounding-box reject. All ties are set
selections as written, never a sort with a tolerance comparer, so the result does not depend on
drawing order or direction.

Left alone: exactly the points with no line (drawn or bridge) on the far side of their nearest
line, more than 5 cm from it.

## Approaches considered

1. **Nearest line on each side** (the first candidate): a corner halfway up a square-cornered bank
   got the toe's height and jumped 0.5 m for a 7 mm move; building corners on slopes whose contours
   stop at the walls came out wrong. 227 wrong of 611 test points.
2. **A triangulation (TIN) of the lines' points**, the kind of surface Revit builds: a corner just
   past a line's end falls outside every triangle and keeps its height — the reported fold; near a
   wall drawn as two close lines it was up to 0.5 m off. 114 wrong.
3. **Nearest line, then the next height in view, same-height gaps bridged** — chosen (yours,
   "Outline only" / "No limit"; the rule is my call, Daniel handed over the how). 44 wrong; the
   remaining ones are listed under "Known weaknesses". Inverse-distance weighting and natural
   neighbours were dropped earlier (no exact slopes, pits in mounds, fold-back past the last line).

Checked by three re-implementations written from the text alone: 698 of 698 test points and every
bridge identical; order and direction independence on 2,094 points and 29,507 grid points.

## Which points, and what is remembered

- **Candidates:** the live vertices within 1 mm of any loop edge of the element's outline (outer and
  holes; `SlabTarget` loops), that no ledger entry of any line or view records (matched as the
  applier matches, in X, Y and Z), and that are not within 1 mm (plan) of one of this run's line
  points. Not filtered by `SlabShapeVertexType`: what Revit reports for outline and hole corners is
  unchecked, and geometry already answers the question (my call).
- **Today's exception folds in:** an edge point within 1 mm of an outline vertex reuses that vertex
  (today 0.1 mm; Revit merges points closer than about 0.8 mm), and the vertex is remembered like
  any adjusted corner, so it goes back to its own height when the line goes. This closes the backlog
  item.
- **Record:** a new Extensible Storage schema on the element, `RVTukTopoToolsOutlineHeights`, GUID
  `cca5f35d-98c8-466c-9175-4d51afe00df1`, vendor KnafoKlimor, one string field `CornersByXy`:
  `x,y,ownZ,setZ,flag;…` — plan XY and Z in internal feet ('R' format), `ownZ` the height before the
  tool first moved it (never overwritten), `setZ` the height the tool last gave it, `flag` 0 or
  `hand`. Pinned in FrozenIdentifierTests once committed. A separate schema, not a key inside the
  ledger string, because an older build rewrites the ledger and would erase it; Floor ⇄ Topo copies
  every KnafoKlimor entity, so it carries this one unchanged (my call).
- **Each Update, for each candidate:** compute h. If h exists: record (ownZ = current Z the first
  time) and raise to h. If V is left alone and has a record: put ownZ back and drop the record.
  A recorded corner whose current Z differs from setZ by more than 0.1 mm was moved by hand: flag it,
  never touch it again, report it once. A record whose vertex is gone (Edit Boundary, Reset Shape) is
  dropped silently.
- Heights never depend on the corners' current Z, so a re-run gives identical results.

## How it is built

- **Core:** `RVTuk.Core/TopoTools/OutlineCornerHeights.cs`, pure: `Prepare(lines)` (cleaning,
  bridges) and `HeightAt(XyPoint) → double?` (null = left alone), plus the record codec
  (`OutlineCornerRecordCodec`). Tests port the vectors file as an xunit theory (228 scenes, 698
  points; null = keep) and add an order/direction test.
- **Revit:** the pass runs in `TopoPointApplier.Apply`, after the interior and edge points are placed
  and before the ledger write, in the same "Topo Update" or "Topo Cancel" transaction (one Ctrl+Z).
  Regenerate before reading the vertices. The runner passes the view's lines as drawn with their
  heights (today only clipped samples reach the applier) and the loops. Raise with
  `ModifySubElement`, offset = target Z − the reference (learnt by the first raise this run, else
  `ReferenceGuess`).
- **When the pass runs** (my call, the same for toposolids and floors): at every Update and Accept
  when the view has at least one topo line with a readable height, or the element has a corner
  record — whether or not a line crosses the element, because a corner shared with a neighbour must
  move even when all the lines lie over the neighbour. The rule is worked out without the shape
  editor (vertex positions come from the outline loops and, once enabled, the editor); the editor is
  switched on only when some corner's height differs from its current Z by more than 0.1 mm or a
  record must be put back. So a floor no line influences stays unshaped. README row 24 is reworded
  to that: "a floor none of whose corners takes a height from the lines stays a plain floor". The
  applier's early return (nothing to place, no stale line) must not skip this pass. Whether a
  toposolid's editor can be off is unchecked; the same test covers it.
- **Cancel** does not run the rule: it restores corners from the start-of-session copy, inside the
  "Topo Cancel" transaction, and only when that copy differs from the current record.
- Lines in other views of the same element are not used (my call); if two views edit one element,
  the last Update decides its corners. Lines over no element with unreadable heights are left out
  silently; lines over the element keep today's report.
- Cost: a C# port runs 2,000 corners × 200 lines in about 0.3 s; no spatial index needed.

## Known weaknesses (to tell Daniel; all shared with approach 1)

- Past the free end of a line that stops between a lower and a higher line, a corner pairs with
  whichever is nearer and can jump by half the step between them (0.57 m two metres past a stopped
  contour). Drawing the line on to the corner settles it.
- A retaining wall's top line drawn in the view pulls corners along its foot up (45.65 instead of
  41.40 beside a 5 m wall); a corner sketched a few cm inside a wall-top line is read across the wall.
  Workaround: a short line at the foot's height beside those corners, or snap the outline to the line.
- Along the axis of a swale, a spur or a U opening onto a lower line the height stays at the contour
  (40.50 where 40.30 is right).
- A neighbouring area's line passing beside a corner can beat the contour straight across.
- No reach limit (yours): far corners between distant areas move (42.50 over an existing 47.00
  knoll).

## Checks in Revit after the build

New README rows: the three screenshot corners (41.20 / 41.50 / 42.89 within 2 cm); a corner raised,
then its line deleted → back to its own height; Cancel puts back a corner only the session's line
moved; a corner moved by hand after Update is left alone and reported once; a corner shared by two
toposolids gets one height from both sessions; a floor; Revit 2025–2027. Unknowns these settle:
whether `ModifySubElement` accepts a Corner vertex of a toposolid and of a floor, and measures it
from the same unshaped top as an edge point; whether the heights hold at a level or height offset
other than 0 (rows 19 and 73).

## Docs

README (what Update writes, "The one exception", Cancel, the summary lines, the new rows), backlog
(close "keeps the line's height after the line moves"), CLAUDE.md's Topo Tools bullet, the
2026-07-29 spec's "not editing points the tool did not make", FrozenIdentifierTests.

## Left to the builder

- Exact summary copy, in the style of `TopoSessionReport` (left-to-right text; counts as words for
  one); whether the 5 cm and 1 mm thresholds go through `TopoPointDeduper.SameSpotFeet`.
- Whether raises are batched (snapshot once, as `SlabShape.Raise`) or regenerate per vertex; settle
  by the Revit check.
- Splitting the work into tasks; the rule's port first, against the vectors.

## As built (2026-11-19)

Where the build (commits 8fbe3b0..4df574c) differs from the text above; the README describes the
build.

- **`Apply(CornerPass?)`.** The applier takes one `CornerPass` (the prepared rule, null when the view
  has no readable line, plus the shared elevation of the internal origin) instead of two loose
  parameters; a null `CornerPass` runs no corner pass at all (Cancel's step 2). With a null rule the
  pass still puts back recorded corners.
- **Candidates and line ends.** A vertex is kept out only within 1 mm (plan) of a point this run
  *placed* as a new vertex, not of every line point. An edge point within 1 mm of a candidate vertex
  is not drawn at all (rather than reusing the vertex); the rule's 1 mm snap raises and records that
  vertex, so "points added" no longer counts it.
- **Accept's count and copy.** The sentences are the plan's: "N outline corners took their heights
  from the lines.", "N outline corners went back to their own heights.", "N outline corners moved by
  hand are left alone." (singular forms in `TopoSessionReport`). The first counts every corner holding
  a line height after the run (records not flagged by hand, less raises that missed), those set by
  earlier Updates and those already at their height included, so Accept after a quiet Update still
  names them.
- **Hand and failed-delete rules.** A ledgered line point found moved by hand (no vertex at its X, Y
  and Z, one at its plan spot) gets a corner record flagged `hand`, so the pass never raises it. A
  ledgered point that matched but that Revit would not delete is kept out of that run's pass only,
  with no record. A record not flagged `hand` whose corner stands at its `ownZ` but off its `setZ`
  was reset (Reset Shape), not moved by hand: the record is dropped and the corner planned afresh.
- **Misses.** A raise records the Z it reached, so a miss is tried again (and counted as not placed)
  at every Update; a put-back that misses keeps a record (`ownZ`, `setZ` = where it stands).
- **Plain elements.** With shape editing off and no line work, the pass is worked out dry from the
  sketch's corners at the flat top (level plus height offset, else the bounding box top); a floor
  switched on for the pass alone whose corners then did not move gets `ResetSlabShape()`. Records on
  an element whose shape editing is off are ignored and overwritten.
- **Cancel.** The start copy is normalised at Start (`EffectiveStart`): against the vertices on the
  outline, a record already stale then (Reset Shape, a hand move not yet flagged, shape editing off)
  is dropped or flagged, so Cancel never raises a corner to a height it did not have. Cancel's step
  runs when `CancelHasCornerWork` says so (the record changed, or the start copy was stale), only on
  vertices on the outline (`OnOutline`), and not when the element was plain when Cancel began. A start
  record the session dropped (an Update put its corner back) comes back and its corner is raised
  again. The message adds "… and N outline corners" (or "put back N outline corners." with no text
  put back).
