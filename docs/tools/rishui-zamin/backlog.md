# Rishui Zamin — Backlog

Bugs, improvements, and ideas for the Rishui Zamin (Area Calc) tool, including its
KKarea host (Revit 2023). Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] Validation warnings are never shown: `RishuiZaminExporter.Export` drops
  `ValidationResult.Warnings` and the UI never reads them, so the frame-cap, outside-frame
  and missing-number/name warnings are computed and discarded. The validator's page-1 length
  cap (14,500 mm) is also looser than the live robot's 14,000 mm (rules.md §4.4).

## ✨ Improvements

- [ ] Check setting usage codes from the Area Calc window in Revit 2024 and in 2023 (KKarea):
  the checks in Task 6 Step 3 of
  [plans/2026-11-22-area-usage-assign.md](plans/2026-11-22-area-usage-assign.md). Built and
  unit-tested 2026-11-22; not yet run in Revit. Also note there whether clearing a key leaves
  `RZ_USAGE_TYPE` with its old value (spec §7). Add, from the branch review: a workshared
  area borrowed by another user (the status line must not say done); two open copies of one
  project, switching before the click (must refuse); typing, Enter and Esc in the picker with
  a Hebrew keyboard; a collapsed floor stays collapsed and the list keeps its place after an
  assignment.
- [ ] Area Calc window, smaller points from the branch review (2026-11-22): the deleted-area
  message gives the element id, not the area's number and name; a Shift-range can take in
  rows of collapsed floors; clearing a code in a project with no key schedules still offers
  to run setup; the picker's highlight can stop following ↑/↓ after a press-and-drag-off;
  an area with an invalid New code (999) and a valid Old code groups under the Old code's
  group, while the validator and the "without a usage code" count call it uncoded.
- [ ] Check the Table tab's plot area and limits in Revit 2024 and 2023 (KKarea); built and
  unit-tested 2026-11-23, not yet run in Revit
  ([plans/2026-11-23-area-table.md](plans/2026-11-23-area-table.md) Task 3):
  1. type plot area and limits, close and reopen the project → the values come back;
  2. each commit is one Ctrl+Z, named "Area Calc — plot area and limits";
  3. workshared, Project Information borrowed by another user (also after your last sync) →
     the message names them and the value stays, marked not saved; changed in central but not
     reloaded → "Reload Latest";
  4. two projects open: switch, Refresh → the other project's values (or empty boxes) show;
  5. KKarea (2023): checks 1 and 2;
  6. type a value and close the window with X while the box still has the caret → is it
     saved? (Enter always saves.)
- [ ] Table tab, smaller points from the branch review (2026-11-23):
  - a Refresh replaces an invalid box (e.g. "35,5") with the stored value without saying so;
  - after "Not saved…", the only retry is Enter in a box, and the line does not say so;
  - no test for a commit arriving while a save is still running (`_commitAgain`);
  - the save waits on `ExternalEvent.Raise()` with no timeout and ignores a Denied result (the
    same pattern as the other Area Calc handlers);
  - the "(ללא מפלס)" (no level) row sorts among the floors by its first area's elevation, not last;
  - saving borrows Project Information in a workshared model until the next sync; a
    `DataStorage` element of our own would not.
- [ ] Setup Usage Keys still matches key rows by exact Key Name, so a row renamed by hand is
  added again; the window's assignment already finds rows by their code (spec §5.7).

- [ ] Split crop regions: `AreaExtractor` maps every point with the view's first
  model→projection transform. A view with a split crop has one transform per region; pick
  the region whose boundary contains the point (`TransformWithBoundary.GetBoundary`).

## 🚀 Ideas

*(none tracked)*

## ✅ Done

- [x] v1: DXF+DAT export, usage-key schedules, marker forms (branch history: `RZarea`).
- [x] KKarea standalone Revit 2023 host
  (see [specs/2026-07-06-kkarea-revit2023-design.md](specs/2026-07-06-kkarea-revit2023-design.md)).
- [x] KKarea builds and deploys for Revit 2026 (net8) and 2027 (net10): `Release2026` /
  `Release2027` configs, Deploy.ps1 and the installer (2026-11-15). Not yet loaded in Revit.
- [x] Window UI redesign
  (see [specs/2026-07-05-area-submission-ui-redesign-design.md](specs/2026-07-05-area-submission-ui-redesign-design.md)).
- [x] `.dat` wrote the plot scale (`DWFX_SCALE 100` at 1:100) once the scale became
  auto-detected; the robot rejected it as error #41. It now always writes `10`
  (2026-10-12, rules.md §8).
- [x] Areas exported in the wrong sheet position (27 cm left, 554 cm down, i.e. 55 mm on paper, off the DWFX on job
  338000-1): the mapper anchored the crop box to the viewport's box outline, which also
  encloses annotations. It now uses Revit's model→sheet transforms (2026-10-12, rules.md 4.6a;
  verified in Revit on job 338000-1).
