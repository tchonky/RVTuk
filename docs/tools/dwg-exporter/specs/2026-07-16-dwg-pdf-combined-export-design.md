# DWG + PDF combined export — design

**Date:** 2026-07-16
**Status:** approved design
**Parent tool:** DWG Exporter (`DwgExporter`) — first item from the 2026-07-16 backlog batch.

## Problem

Issuing a sheet set today means running the RVTuk DWG export *and* the native PDF export
separately. Both use the same naming rules (the tool reads them from the PDF setups), so
one command should produce both file sets side by side with matching names.

## Decisions

1. **Two format checkboxes** — `DWG` / `PDF`, either alone or both (Export disabled when
   neither). The window becomes the office sheet exporter; PDF-only runs are valid.
2. **Always one PDF per sheet** — `PDFExportOptions.Combine` is forced `false` regardless
   of what the chosen setup says, so PDF names mirror DWG names one-to-one. Combining
   into a single PDF stays a native-dialog job.
3. **Revit names the PDFs itself**: per sheet, one
   `Document.Export(folder, {sheetId}, PDFExportOptions)` call with the selected setup's
   own options — the naming rule is evaluated by Revit, guaranteeing byte-for-byte parity
   with the native PDF export. (This also functions as a live check of our DWG-side rule
   evaluator: the `.dwg` and `.pdf` basenames must come out identical.)
4. **Fallback rule built in code** — when the document has no saved PDF setups, the
   pseudo-setup's PDF options get an explicitly constructed naming rule
   (Sheet Number, " - ", Sheet Name) so PDF and DWG names still pair up.
5. **Window title** becomes "RVTuk — Sheet Export (DWG / PDF)"; ribbon id/label stay
   `DwgExport` / "DWG Export" (stable id, no re-learning).
6. **Checkbox states persist** like the other last-used fields. Stored as *inverted /
   additive* booleans (`DwgExportDwgOff`, `DwgExportPdfOn`) because net48's
   `DataContractJsonSerializer` deserializes via uninitialized objects — property
   initializers don't run, so a `= true` default would silently flip to `false` when
   loading a config file written before this feature. `false`-means-default is safe on
   both serializers.

## Changes by layer

| Layer | Change |
|-------|--------|
| Core | `DwgExportRequest` gains `ExportDwg` (default true) + `ExportPdf` (default false). `AppConfig` gains the two inverted persistence bools. No planner changes — the existence callback stays injected, the Revit side makes it format-aware. |
| UI | Formats row (two checkboxes) above the DWG Export Setup section; DWG setup combo disabled when DWG unchecked; `CanExport` requires at least one format; states load/save through config. Title change. |
| Revit | `SheetDwgExporter.Export` resolves `DWGExportOptions` and/or `PDFExportOptions` once, then per sheet exports each requested format inside its own try/catch (`"A-101 (PDF): reason"` error entries). `GetPdfOptions` forces `Combine=false` and builds the fallback naming rule via `TableCellCombinedParameterData`. File-existence check covers `.dwg` and/or `.pdf` per the chosen formats. Progress label gains a format tag: `12/50  A-101 (DWG+PDF)`. |

## Error handling

- Neither format checked → Export button disabled (never reaches the pipeline).
- Per-sheet, per-format failures are independent: a PDF failure doesn't stop the DWG of
  the same sheet, nor the rest of the batch. Summary counts files, not sheets.
- Overwrite prompt covers both extensions; the duplicate-name abort is unchanged
  (format-independent).

## Known caveat (documented, not handled)

For a non-sheet current view, Revit's PDF naming rule may name the file differently than
the DWG side (which uses the view name). Sheets — the actual use case — always pair.

## Testing

Core: config-bool semantics including the missing-key/old-file case (`{}` deserializes to
DWG-on/PDF-off), request flag defaults, round-trip. Revit side: deploy, export a set with
both formats, verify `.dwg`/`.pdf` basenames match exactly.
