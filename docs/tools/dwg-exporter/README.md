# DWG Exporter (ribbon button: "DWG Export")

**What it is:** batch-exports a view/sheet set to DWG with filenames produced by the
naming rules of the document's saved **PDF** export setups (`ExportPDFSettings`) — so the
DWGs come out named exactly like the PDFs (e.g.
`<Project Number>-A-BLD_<Building Number>-<Sheet Number>.dwg`), instead of the native
exporter's `Sheet - A101 - ….dwg`. Layers/lines/colors come from a native DWG export
setup (`ExportDWGSettings`) picked in the same window. Format checkboxes let one run
also (or only) produce **PDFs** — one per sheet, named by Revit's own evaluation of the
same naming rule, so `.dwg`/`.pdf` basenames pair exactly (window title: "Sheet Export
(DWG / PDF)").

**Status:** implemented and registered on the ribbon (2026-07-15); pending in-Revit
verification — see the checklist in
[plans/2026-07-15-dwg-exporter.md](plans/2026-07-15-dwg-exporter.md) Task 6.

**Names:** code `DwgExporter`; ribbon button displays "DWG Export" (internal id
`DwgExport`).

## Known limitation: layout paper size

The exported DWG's layout does **not** carry the Revit title-block sheet size — the
Revit API offers no control over the layout page setup (`DWGExportOptions` has no paper
option; confirmed long-standing gap, tracked as an open
[Autodesk Idea](https://forums.autodesk.com/t5/revit-ideas/add-paper-size-option-on-dwg-export-command/idi-p/9560587)).
AutoCAD shows the default plot device's paper until the page setup is set there. Decided
2026-07-15 to accept and document rather than work around; a possible future mitigation
(companion AutoCAD script that stamps each layout with the measured title-block size) is
in [backlog.md](backlog.md).

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/DwgExporter/` (filename composer, config) |
| UI    | `src/RVTuk.UI/DwgExporter/` (Views, ViewModels) |
| Revit | `src/RVTuk.Revit/DwgExporter/` (command, naming-rule evaluator, sheet exporter) |
| Tests | `tests/RVTuk.Core.Tests/DwgExporter/` |

Revit 2024/2025 only — not hosted in KKarea (2023).

## Docs

- [backlog.md](backlog.md) — bugs / improvements / ideas
- [specs/](specs/) — dated designs
  ([2026-07-15 design](specs/2026-07-15-dwg-exporter-design.md))
