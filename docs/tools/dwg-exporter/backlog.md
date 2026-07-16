# DWG Exporter — backlog

## 🚀 New features (requested 2026-07-16)

- [ ] **Naming-rule editor** — modify the naming rules from within the tool, like the
  PDF exporter's pencil button (today the rules are read-only from the PDF export
  setups, by design; this adds create/edit/override).
- [ ] **View/sheet set editor** — change the views/sheets set from within the tool (the
  PDF dialog's pencil button) instead of relying on the native Print/PDF dialogs to
  manage `ViewSheetSet`s.
- [ ] **DWG export settings editor** — change the DWG export setup (layers, lines,
  colors…) from within the tool instead of the native "Modify DWG/DXF Export Setup"
  dialog.
- [x] **Export DWG + PDF together** — one command produces both file sets, same
  filenames (they already share the naming rules), same range, side by side. Shipped
  2026-07-16 ([spec](specs/2026-07-16-dwg-pdf-combined-export-design.md)): DWG/PDF
  format checkboxes, PDF always one file per sheet (Combine forced off), Revit itself
  evaluates the naming rule for PDFs. Caveat: for a non-sheet *current view*, the PDF
  name (rule-based) can differ from the DWG name (view name); sheets always pair.

## 🔬 To research (feasibility, requested 2026-07-16)

- [x] **Model-space export matching the sheet** — researched 2026-07-16, see
  [research/2026-07-16-model-space-and-layers.md](research/2026-07-16-model-space-and-layers.md).
  Verdict: impossible from Revit; AutoCAD's `EXPORTLAYOUT` does exactly this and is
  scriptable per batch (companion-script candidate below).
- [x] **Layer customisation beyond the native mapping** — researched 2026-07-16, same
  doc. Verdicts: per-wall-layer CAD layers — not achievable at export time (mapping is
  per category/subcategory; modifiers are whole-element); preserve coincident lines —
  already native in the DWG setup; nested-family-on-host-layer — solve in the family
  library (subcategory assignment) or via in-block layer remap in a CAD-side script.
- [ ] **Companion CAD-side script (umbrella follow-up)** — one accoreconsole batch
  emitted next to the exports covering the achievable wishes: stamp layout paper sizes,
  `EXPORTLAYOUT` flatten to model space, in-block layer remaps. Decide whether to build
  after reading the research doc.

## Ideas / future

- Companion AutoCAD script for layout paper size: Revit's API can't set the exported
  layout's page setup (see README "Known limitation"), but we can measure the real
  title-block size (as the Rishui Zamin extractor already does) and emit one script
  alongside the DWGs that stamps every layout's paper size — exact ISO names for
  standard sheets, explicit width/height for custom ones (office is "mostly ISO, some
  custom"). Declined for now (2026-07-15) in favour of documenting the limitation.

- DXF output flavour: same window, list `ExportDXFSettings` alongside the DWG setups.

## To verify (in Revit, after deploy)

- Run the manual checklist in [plans/2026-07-15-dwg-exporter.md](plans/2026-07-15-dwg-exporter.md)
  Task 6 — especially the naming-rule **separator placement** check against the native
  PDF export's filenames (fix lives in `FileNameComposer.Compose` if they differ).

## Done

- 2026-07-15 — implemented across all three layers + ribbon button; Core suite green
  (182 tests). Pending in-Revit verification.
- 2026-07-15 — design approved: PDF-setup naming rules, modal in-command dialog, no
  Options section ([spec](specs/2026-07-15-dwg-exporter-design.md)).
