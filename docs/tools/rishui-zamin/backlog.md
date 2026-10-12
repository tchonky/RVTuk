# Rishui Zamin — Backlog

Bugs, improvements, and ideas for the Rishui Zamin (Area Calc) tool, including its
KKarea 2023 host. Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] Validation warnings are never shown: `RishuiZaminExporter.Export` drops
  `ValidationResult.Warnings` and the UI never reads them, so the frame-cap, outside-frame
  and missing-number/name warnings are computed and discarded. The validator's page-1 length
  cap (14,500 mm) is also looser than the live robot's 14,000 mm (rules.md §4.4).

## ✨ Improvements

- [ ] Split crop regions: `AreaExtractor` maps every point with the view's first
  model→projection transform. A view with a split crop has one transform per region; pick
  the region whose boundary contains the point (`TransformWithBoundary.GetBoundary`).

## 🚀 Ideas

*(none tracked)*

## ✅ Done

- [x] v1: DXF+DAT export, usage-key schedules, marker forms (branch history: `RZarea`).
- [x] KKarea standalone Revit 2023 host
  (see [specs/2026-07-06-kkarea-revit2023-design.md](specs/2026-07-06-kkarea-revit2023-design.md)).
- [x] Window UI redesign
  (see [specs/2026-07-05-area-submission-ui-redesign-design.md](specs/2026-07-05-area-submission-ui-redesign-design.md)).
- [x] `.dat` wrote the plot scale (`DWFX_SCALE 100` at 1:100) once the scale became
  auto-detected; the robot rejected it as error #41. It now always writes `10`
  (2026-10-12, rules.md §8).
- [x] Areas exported in the wrong sheet position (27 cm left, 554 cm down, i.e. 55 mm on paper, off the DWFX on job
  338000-1): the mapper anchored the crop box to the viewport's box outline, which also
  encloses annotations. It now uses Revit's model→sheet transforms (2026-10-12, rules.md 4.6a;
  awaiting in-Revit verification).
