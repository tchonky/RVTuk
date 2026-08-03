# Auto Dimensions — Backlog

Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] **Curtain and stacked walls cross a line and mark nothing.** `HostObjectUtils.GetSideFaces`
  reports no side faces for either, so the candidate is dropped. The run summary now counts these
  ("no usable faces"). A stacked wall could be resolved through `GetStackedWallMemberIds`; a
  curtain wall has no side faces to find and would need a different reference entirely.

## ✨ Improvements

*(none tracked)*

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
- [ ] **In-Revit verification pass still outstanding.** The 10 checks are in
  [plans/2026-07-26-auto-dimensions-scope-pane.md](plans/2026-07-26-auto-dimensions-scope-pane.md)
  (Task 12, Step 7). The one with no test behind it is fanning out to a *second* view of the
  same level — that exercises `DimensionRunner.ToTargetViewPlane`.
- [ ] **Verify *Show Opening Height* actually fires.** Tick it on a dimension type, pick that type
  in the pane, run over a plan with doors and windows, and confirm the height prints under the
  width — in the host model *and* through a Revit link, which is the documented sore spot. If it
  does not fire on `GetReferences(Left/Right)` references, the reference strategy needs revisiting,
  not the type.
- [ ] **Verify occlusion on a real plan.** Draw a reference line outside a facade and confirm the
  string dimensions the facade's openings and none from the interior walls behind it; then untick
  Walls and confirm the same openings are matched (walls still occlude) and that the run summary's
  "does not cut" count does not balloon.
