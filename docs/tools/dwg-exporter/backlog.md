# DWG Exporter — backlog

## Ideas / future

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
