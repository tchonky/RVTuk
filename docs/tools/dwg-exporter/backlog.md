# DWG Exporter — backlog

## 🚀 New features (requested 2026-07-16)

- [x] **Naming-rule editor** — shipped 2026-07-16 as a **hand-off**, not a custom
  editor ([spec](specs/2026-07-16-native-dialog-handoff-design.md)): "Edit…" posts
  Revit's own PDF Export dialog (`PostableCommand.ExportPDF`), whose pencil button is
  the real rule editor. The window closes first (posted commands run after the modal
  command ends); reopening re-reads setups and restores selections. Custom in-tool
  editors stay off the table unless the hop proves unusable.
- [x] **View/sheet set editor** — hand-off on the range row: posts **Publish Settings**
  (`PostableCommand.PublishSettings`, a dedicated set manager) and falls back to the
  PDF Export dialog (its pencil also manages sets) when Revit greys Publish Settings
  out for the current model. Naming rules can't move there — the rule editor exists
  only inside the PDF Export dialog.
- [x] **DWG export settings editor** — shipped as a hand-off straight to the native
  "Modify DWG/DXF Export Setup" dialog (`PostableCommand.ExportOptionsExportSetupsDWGOrDXF`).
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
