# DWG Exporter — design

**Date:** 2026-07-15
**Status:** approved design, pre-implementation
**Tool name:** code `DwgExporter`; ribbon button "DWG Export" (internal id `DwgExport`)

## Problem

Revit's native DWG exporter cannot name output files by rule — it invents names like
`Sheet - A101 - <sheet name>.dwg`. The office's deliverable filenames follow the naming
rules already maintained in the **PDF** export setups (e.g.
`<Project Number>-A-BLD_<Building Number>-<Sheet Number>`), so every DWG issue currently
requires hand-renaming. The DWG Exporter batch-exports a sheet set to DWG with filenames
produced by the same naming rules the PDF export uses.

## Decisions (with rationale)

1. **Naming rules come from the document's saved PDF export setups** (`ExportPDFSettings`).
   We read the selected setup's naming rule via `PDFExportOptions.GetNamingRule()` and
   evaluate it per sheet. DWG names automatically match the PDF names; rules keep being
   edited in the native PDF dialog. No rule editor of our own.
2. **No Options section.** The four DWG-supported view-cleanup toggles (hide scope boxes /
   ref planes / unreferenced view tags, preserve coincident lines) already live inside
   every saved DWG export setup; re-surfacing them as overrides invites confusion. The
   selected DWG setup fully controls them.
3. **Export Range = "Current window" + saved view/sheet sets dropdown.** No built-in
   set editor (the native Print/PDF dialogs edit `ViewSheetSet`s; they're shared).
   "Visible portion of current window" has no DWG meaning — dropped.
4. **Modal dialog inside the command.** `Execute` shows the window with `ShowDialog()`;
   WPF's modal loop is a nested message pump on the Revit UI thread, so the Export
   button's click handler still runs inside the command's API context and can call the
   Revit API directly. No `ExternalEvent` plumbing. Matches the native exporters' UX
   (Revit blocked during export, per-sheet progress in the dialog).
5. **One `Document.Export` call per sheet** with the exact evaluated filename — the
   multi-view overload invents its own names. Also gives per-sheet progress and
   per-sheet error capture.
6. **Visible immediately:** registered on the RVTuk ribbon next to Area Calc (not behind
   `RegisterUnreleasedTools`).
7. **Scope:** Revit 2024 + 2025 (`RVTuk.Revit` only). Not added to KKarea/2023. DWG only
   (no DXF twin for now). No "combine into one file" (meaningless for DWG).

## Window

Modal `DwgExportWindow`, styled like the other RVTuk windows:

```
┌─ DWG Export ────────────────────────────────┐
│ Export Range                                │
│  ○ Current window                           │
│  ● Selected views/sheets  [Sheets for Publish ▾]
│                                             │
│ File Naming (from PDF export setup)         │
│  [KKarc - Sheets (No Revision)        ▾]    │
│  Pattern:  <Project Number>-A-BLD_<Building Number>-<Sheet Number>
│  Example:  1234-A-BLD_2-A-101.dwg           │
│                                             │
│ DWG Export Setup                            │
│  [KKarc standard DWG                  ▾]    │
│                                             │
│ Location:  [D:\User\Documents ] [Browse…]   │
│                                             │
│  [▓▓▓▓▓░░░░░ 12/50  A-112 exported]         │
│                     [Export]  [Close]       │
└─────────────────────────────────────────────┘
```

- **File Naming** lists the document's saved `ExportPDFSettings` by name. Below the
  dropdown: a read-only pattern line, plus a live **Example** — the rule evaluated
  against the first sheet of the chosen range, so the real filename is visible before
  export. If the document has no PDF setups, one built-in fallback rule
  `<Sheet Number> - <Sheet Name>` is offered.
- **DWG Export Setup** lists the document's saved `ExportDWGSettings` (same list as the
  native exporter's "Select Export Setup"). If none exist, a single `<Revit defaults>`
  entry uses stock `DWGExportOptions`.
- Progress row (bar + "12/50 A-112 exported") appears during export; the window stays
  open afterwards showing the summary.

## Components

| Layer | Piece | Responsibility |
|-------|-------|----------------|
| Core (`src/RVTuk.Core/DwgExporter/`) | `FileNameComposer` | Join resolved rule parts (prefix / value / suffix / separator per field), sanitize illegal filename characters, detect duplicate names across the sheet list. Pure, fully tested. |
| Core | `DwgExportConfig` | Persist last-used folder, PDF-setup name, DWG-setup name, set name, range radio (global config file, same pattern as `RishuiZaminConfig`). Matched by name next session; missing names silently fall back to defaults. |
| UI (`src/RVTuk.UI/DwgExporter/`) | `DwgExportViewModel`, `Views/DwgExportWindow` | Revit-free. Receives plain descriptor lists (PDF setup names + pattern strings, DWG setup names, sheet-set names + counts, initial folder) and two delegates: `evaluateExample(pdfSetup, range) → string` and `runExport(request, progress) → result`. |
| Revit (`src/RVTuk.Revit/DwgExporter/`) | `Commands/DwgExportCommand` | Gather document data, build the descriptor lists, show the dialog modally, wire the delegates. |
| Revit | `NamingRuleEvaluator` | Walk the naming rule's `TableCellCombinedParameterData` entries; resolve each parameter's value from the sheet or Project Info (by category id); hand resolved parts to `FileNameComposer`. Empty values render as empty strings (native behaviour). |
| Revit | `SheetDwgExporter` | Loop the range; per sheet: evaluate name, `doc.Export(folder, name, {sheetId}, options)` with the options from the selected `ExportDWGSettings`; collect per-sheet failures. |
| Revit | `Application.cs` | One new ribbon button "DWG\nExport" (drawn icon, like the others) → `DwgExportCommand`. No external events, no static window reference. |

## Export flow

1. Validate: location exists (offer to create it); the chosen range resolves to at least
   one view/sheet.
2. Pre-evaluate **all** filenames. Duplicate names → abort with the list of clashing
   sheets (never let sheets overwrite each other). Files already on disk → one prompt:
   "N files already exist — overwrite?".
3. Export sheet-by-sheet inside per-sheet `try/catch`. Progress bar advances each sheet;
   the dispatcher is pumped so the UI repaints during the synchronous loop.
4. Summary shown in the window: exported N, failed M with per-sheet reasons.

## Testing

- Core xunit tests in `tests/RVTuk.Core.Tests/DwgExporter/`: composer join/sanitize
  (Hebrew text, illegal characters, empty values, separators), duplicate detection,
  config round-trip.
- Revit side verified manually: deploy, open a project with KKarc PDF setups, export a
  sheet set, confirm filenames match the PDF export's output names.

## Out of scope (recorded for the backlog)

- DXF output flavour (same window, `ExportDXFSettings`).
- Built-in view/sheet set editor (pencil button).
- Naming-rule editor / per-export rule overrides.
- KKarea (Revit 2023) hosting.
