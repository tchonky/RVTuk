# Auto Dimensions — Backlog

Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] **Curtain and stacked walls cross a line and mark nothing.** `HostObjectUtils.GetSideFaces`
  reports no side faces for either, so the candidate is dropped. The run summary now counts these
  ("no usable faces"). A stacked wall could be resolved through `GetStackedWallMemberIds`; a
  curtain wall has no side faces to find and would need a different reference entirely.
- [ ] **A profile-edited wall may mark at the wrong place.** Reference resolution takes
  `GetSideFaces(...)[0]`, an arbitrary one when a wall reports several faces per side.

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
- [ ] **Linked elements ignore view-level visibility.** They're filtered by the target view's cut
  plane only (see the README) — a linked wall hidden by a view filter, workset or phase is still
  dimensioned. Tightening this means resolving the link's visibility settings per view.
- [ ] **Links are skipped in non-plan views.** No cut plane, so no elevation filter, so no safe
  way to tell which storey of the link belongs in the view.

## ⏳ Release

- [x] Registered on the RVTuk ribbon panel (`RegisterAutoDimensions` in
  `src/RVTuk.Revit/Application.cs`, on).
- [ ] **In-Revit verification pass still outstanding.** The 10 checks are in
  [plans/2026-07-26-auto-dimensions-scope-pane.md](plans/2026-07-26-auto-dimensions-scope-pane.md)
  (Task 12, Step 7). The one with no test behind it is fanning out to a *second* view of the
  same level — that exercises `DimensionRunner.ToTargetViewPlane`.
