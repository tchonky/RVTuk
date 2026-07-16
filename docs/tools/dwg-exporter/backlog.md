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
- [ ] **Export DWG + PDF together** — one command produces both file sets, same
  filenames (they already share the naming rules), same range, side by side.

## 🔬 To research (feasibility, requested 2026-07-16)

- [ ] **Model-space export matching the sheet** — check if the DWG can be exported so
  the *Model* tab shows the sheet as it looks in Revit (viewports in the same positions),
  not just the paper-space layout. Today only the layout matches the Revit sheet.
- [ ] **Layer customisation beyond the native mapping** — check what's possible past the
  native export setup, e.g.: separate CAD layers per wall layer (structure/finish/…);
  preserving coincident lines while doing so; keeping nested families on the host
  family's layer (e.g. a door with a nested detail family exporting as one door layer).

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
