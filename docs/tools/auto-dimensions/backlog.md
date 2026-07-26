# Auto Dimensions — Backlog

Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

*(none tracked)*

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

## ⏳ Release

- [ ] In-Revit verification pass, then flip `RegisterUnreleasedTools` (or promote this
  tool to the always-registered set) to ship it.
