# Auto Dimensions — Backlog

Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] **Curtain and stacked walls cross a line and mark nothing.** `HostObjectUtils.GetSideFaces`
  reports no side faces for either, so the candidate is dropped. The run summary now counts these
  ("no usable faces"). A stacked wall could be resolved through `GetStackedWallMemberIds`; a
  curtain wall has no side faces to find and would need a different reference entirely.
  A curtain Wall can be measured by the mullion.
- [ ] **A ref line can mark a door jamb instead of the wall end, silently — parallel walls only.**
  It takes two things at once: the wall's true end face missing or off-angle (a mitered corner
  join leaves a 45° face the 5° filter rejects), *and* another qualifying face within 1 ft along
  the string — a jamb being a vertical face whose normal runs along its host wall, which on a
  wall parallel to the string is also along the string, exactly what
  `WallEndResolver.TryFaceAlongString` admits. The error is bounded to ≤305 mm, so it reads as a
  correct dimension. Known closure: also require the face normal to point *away* from the wall
  body — the near jamb's outward normal points back along the wall, so a sign test excludes it.
  Does not apply to a perpendicular wall: there the jambs face across the string and are already
  rejected. Distinct from the joined-end case in the README, which fails *loudly* (counted and
  reported); this one fails quietly.
- [x] Outer strings are not getting any measurement, even if ref lines are used.
  - Ok, I found the problem. We create two layers of walls sometimes on the facade. A concrete wall
  and a architectural wall, and the windows and doors are hosted in the concrete wall. Even with the
  windows and doors cutting the architectural wall, probably the `wall in between` is blocking the
  measure of the concrete wall. The solution now is to hide the architectural wall. But maybe the is
  a prettier solution for this?
  - **Confirmed, and it is narrower than "two layers".** `CandidateMatcher.IsBlocked` blocks on any
  parallel wall that is *strictly nearer* the string than the opening's centre, by more than
  `BlockingEpsilon` (1e-6). An opening's own host escapes only because the opening sits exactly ON
  the host's location curve — "strictly nearer" then fails by rounding alone. So the moment the
  opening's centre is even slightly inboard of a parallel wall, that wall blocks it. Reproduced in
  Core with a single facade wall at y=10, an outer string at y=0 and a window centred at y=10.05:
  the window is dropped, and its blocker is its own host. Two-layer construction is the common way
  to hit it, but so is any family whose origin is offset from the wall centre.
  - Fixing it properly means asking "does this wall stand between them" of the *opening*, not of a
  point: the host (and anything the opening's own cut passes through) should never block it. Candidate
  approaches: exclude the opening's host wall by id rather than by distance; or require a blocker to
  clear the opening's near face by more than half the opening's own host thickness. The first needs
  the host id carried into `Occluders`, which today is geometry-only.
  

## ✨ Improvements

- [ ] **`ResolveRefLines` sits outside the per-line try/catch,** so one throw costs the whole
  view's run rather than one line.
- [ ] **An arc on `_DP-Dim Ref` is counted as "touched no string"** and given advice that won't
  help. It needs its own cause, or a "must be straight" clause in the message.
- [ ] **A string whose only mark is one ref-line reference is counted `Skipped` in silence.**
  Revit needs two references; the user sees nothing and is told nothing. Deserves its own counter.
- [ ] **`RefLinesUnattached`/`RefLinesUnresolved` are view-independent facts tallied per target
  view** (and per hit), so an eight-view fan-out reports three bad ref lines as 24.
- [ ] **`TryEndFace` calls `get_Geometry` once per candidate end, per hit, per target view.** The
  face it returns doesn't depend on the string, so memoising by (wall id, end point) for one run
  would remove most of it. Hoisting the `RefLineMatcher.Match` pass up to the per-level caller
  would fix this and the tally multiplicity together.
- [ ] **`RefLineMatcher.TryIntersect` rejects only exact-parallel** (`|det| < 1e-12`) where
  `WallCrossingFinder` uses a 5° angle test, so a ref line drawn *nearly* along a string registers
  a numerically valid but meaningless station.
- [x] **"Too far off parallel to the string" overstated the gate,** which admitted up to 85°.
  Closed by the orientation fix: the `ReferenceAlignment.CanDimension` pre-gate is gone and the
  real 5° test is applied to each candidate face's own normal against the string, so the summary's
  wording now matches what the code does. (2026-08-03)
- [ ] **`DimensionLineStyle.AllStyleNames` is a mutable `public static readonly string[]`** — a
  caller could reassign an element and break creation and detection at once.
- [ ] **The line-intersection algebra is duplicated** between `RefLineMatcher` and
  `WallCrossingFinder` (same Cramer solve, ~8 lines). Reviewed and accepted: the divergent
  boundary rules are the interesting parts — `WallCrossingFinder` must reject endpoint touches, a
  ref line's primary case *is* an endpoint touch. Extract a shared `TryLineParameters(out u, out t)`
  only if a third caller appears.

## 🚀 Ideas

- [ ] **v2 — Ceilings and Floors as reference categories.** Their checkboxes exist in the scope
  pane but are disabled: both are host objects needing top/bottom-face resolution
  (`HostObjectUtils.GetTopFaces`/`GetBottomFaces`), structurally unlike the family-instance
  Left/Right approach doors and windows use. `DimensionCategories` already reserves their bits and
  `CategoryMask.FromMask` clamps them off until the resolution logic exists.
- [ ] **Surface a level with several reference views.** Today the first by ascending `ElementId`
  silently wins; the convention is one per level, so this is only worth doing if it bites.
- [ ] **A family exposing several Left/Right references.** `GetReferences` returns a list and the
  first is used. Expected to be a list of one — Revit's *Is Reference* flag is set per reference
  plane, and nested families are the plausible source of duplicates. Choosing between them would
  need each reference's position, which family-instance references do not readily give up.
- [ ] **Linked elements ignore view-level visibility.** They're filtered by the target view's cut
  plane only (see the README) — a linked wall hidden by a view filter, workset or phase is still
  dimensioned. Tightening this means resolving the link's visibility settings per view.
- [ ] **Links are skipped in non-plan views.** No cut plane, so no elevation filter, so no safe
  way to tell which storey of the link belongs in the view.

## ⏳ Release

- [x] Registered on the RVTuk ribbon panel (`RegisterAutoDimensions` in
  `src/RVTuk.Revit/Application.cs`, on).
- [x] **Outer/inner rings.** `_DP-Dim Outer` and `_DP-Dim Inner` replace `Dimensions_Line`.
  Ownership of an opening is settled by (ring, distance, line order), so a facade window
  lands on the facade string rather than on a nearer interior one. (2026-08-03)
- [x] **`_DP-Dim Ref` lines.** Draw from a wall end to a string to mark that wall end on it.
  Resolves the wall's end face, not the detail line, so it survives the fan-out and follows
  the wall. Wall ends only for now; joined ends can't be marked and are reported. (2026-08-03)
- [x] **Styles create themselves on the pane's first refresh.** Previously the style was created
  only by a Create Dimensions run, so the first press necessarily did nothing — there had
  been no style to draw a reference line on. (2026-08-03)
- [ ] **In-Revit verification pass still outstanding.** The Revit layer has no automated tests —
  only `RVTuk.Core` is unit-testable — so this pass is its entire verification budget. The scope
  pane's original 10 checks are in
  [plans/2026-07-26-auto-dimensions-scope-pane.md](plans/2026-07-26-auto-dimensions-scope-pane.md)
  (Task 12, Step 7); the one with no test behind it is fanning out to a *second* view of the
  same level, which exercises `DimensionRunner.ToTargetViewPlane`. The rings and ref lines add
  these:
  1. **First-run creation.** Open a project with none of the four styles, show each pane, and
     confirm `_DP-Dim Inner`, `_DP-Dim Outer`, `_DP-Dim Ref` and `_DP-Topo Line` all appear in
     Object Styles → Lines — and that a *second* refresh does not re-mark the document modified.
  2. **Families are untouched.** Open a family in the Family Editor with the Auto Dimensions pane
     docked and tab back to the pane. The family must gain no line subcategories and must not be
     marked modified. (A family's subcategories are baked into every project it is loaded into,
     so a leak here spreads.)
  3. **Rings.** With an outer string outside a facade and an inner string inside it standing
     *closer* to a window, the window is measured by the outer string only. Delete the outer
     string, re-run: the inner takes it.
  4. **Ref line, reference view.** Draw one from a free-ending parallel wall out to a string; the
     wall end gets a mark at the station where the ref line meets the string.
  4b. **Ref line onto a perpendicular wall.** Point one at the corner of a wall running *across*
     the string — the case that used to land a wall-thickness off. The mark must sit on the face
     pointed at, not on the wall's other face and not on a neighbouring wall's end. Check both
     faces of the same wall by pointing at each in turn, and check a wall at a diagonal reports
     as unresolved rather than marking something arbitrary.
  5. **Ref line across the fan-out — the load-bearing check.** Tick a second view of the same
     level and run. The mark must appear there too. If it appears only in the reference view,
     something is referencing the detail line instead of the wall's end face.
  6. **Ref line follows the wall.** Move the wall along its own length, re-run, confirm the mark
     moves with it.
  7. **Ref line through both strings.** Extend it out through the inner string as well; both must
     carry the mark.
  8. **Joined wall end.** Draw a ref line at a wall end joined into another wall. The run must
     complete and report it under "Not marked:", not throw.
  9. **A slanted ref line.** Confirm its mark lands in the right place relative to neighbouring
     wall marks. The station used is where the ref line crosses the string, not where the wall
     end projects onto it — identical when perpendicular, possibly not when slanted.
- [ ] **Verify *Show Opening Height* actually fires.** Tick it on a dimension type, pick that type
  in the pane, run over a plan with doors and windows, and confirm the height prints under the
  width — in the host model *and* through a Revit link, which is the documented sore spot. If it
  does not fire on `GetReferences(Left/Right)` references, the reference strategy needs revisiting,
  not the type.
- [ ] **Verify occlusion on a real plan.** Draw a reference line outside a facade and confirm the
  string dimensions the facade's openings and none from the interior walls behind it; then untick
  Walls and confirm the same openings are matched (walls still occlude) and that the run summary's
  "does not cut" count does not balloon.
