# DWG Exporter — backlog

## Ideas / future

- Companion AutoCAD script for layout paper size: Revit's API can't set the exported
  layout's page setup (see README "Known limitation"), but we can measure the real
  title-block size (as the Rishui Zamin extractor already does) and emit one script
  alongside the DWGs that stamps every layout's paper size — exact ISO names for
  standard sheets, explicit width/height for custom ones (office is "mostly ISO, some
  custom"). Declined for now (2026-07-15) in favour of documenting the limitation.

- DXF output flavour: same window, list `ExportDXFSettings` alongside the DWG setups.
- Built-in view/sheet set editor (the PDF dialog's pencil button) instead of relying on
  the native Print/PDF dialogs to manage `ViewSheetSet`s.
- Naming-rule editor / per-export rule overrides (today the rules are read-only from the
  PDF export setups, by design).

## To verify (in Revit, after deploy)

- Run the manual checklist in [plans/2026-07-15-dwg-exporter.md](plans/2026-07-15-dwg-exporter.md)
  Task 6 — especially the naming-rule **separator placement** check against the native
  PDF export's filenames (fix lives in `FileNameComposer.Compose` if they differ).

## Done

- 2026-07-15 — implemented across all three layers + ribbon button; Core suite green
  (182 tests). Pending in-Revit verification.
- 2026-07-15 — design approved: PDF-setup naming rules, modal in-command dialog, no
  Options section ([spec](specs/2026-07-15-dwg-exporter-design.md)).
