# DWG Exporter (ribbon button: "DWG Export")

**What it is:** batch-exports a view/sheet set to DWG with filenames produced by the
naming rules of the document's saved **PDF** export setups (`ExportPDFSettings`) — so the
DWGs come out named exactly like the PDFs (e.g.
`<Project Number>-A-BLD_<Building Number>-<Sheet Number>.dwg`), instead of the native
exporter's `Sheet - A101 - ….dwg`. Sheets and non-sheet views take **separate naming
rules**, chosen per item, so a rule written for sheets is never applied to a view; each
row shows its own pattern and a worked example, and `<View Name>` (the views default)
uses the view's own name. Layers/lines/colors come from a native DWG export setup
(`ExportDWGSettings`) picked in the same window. Format checkboxes let one run also (or
only) produce **PDFs** — one per view, named by Revit's own evaluation of the same rule,
so `.dwg`/`.pdf` basenames pair exactly (window title: "Sheet Export (DWG / PDF)") —
optionally into their own folder. When more than one model is open, a **Models** list lets
the run span them: each extra model matches the active model's set and setups **by name**,
and a model where one is missing is either skipped or has the setup created in it, your
choice. "Edit…" buttons hand off to the native dialogs (PDF Export for naming rules and
view/sheet sets, Modify DWG/DXF Export Setup for DWG setups) — the window closes, you
edit, reopen, and everything is re-read with your selections restored.

**Status:** implemented and registered on the ribbon. Smoke-tested in Revit after the
2026-08-04 batch — a run exports and writes its transmittal zip. The per-case checklists
(multi-model, the two naming rules, split folders, and the bundle's contents) have **not**
been worked through; they live in [backlog.md](backlog.md) under "To verify", along with
the original 2026-07-15 checklist in
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

## Transmittal zip

A DWG from Revit is not self-contained: raster images are external references, and with the
DWG setup's `MergedViews` off, the views on a sheet come out as separate xref'd drawings.
Tick **Bundle DWGs into a zip (eTransmit-style)** and choose a shape:

- **One zip for the run** — a single `<set name>_<date_time>.zip` holding everything.
- **One zip per drawing** — `<drawing name>.zip` each, so a recipient can be sent one sheet
  without the rest.

Either way an archive holds the DWGs, the images and xrefs Revit emitted, any SHX fonts
found on the machine, and a `TRANSMITTAL.txt`.

In per-drawing mode the output folder is re-snapshotted after **each view**, so every
drawing gets exactly the `.dwg` files its own export produced — including the xref'd view
drawings a setup with `MergedViews` off emits alongside a sheet. Images are different:
Revit writes one copy per run, during whichever drawing used it first, so **every archive
gets every image**. Without a DWG reader there is no way to know which drawings reference
which image, and duplicating bytes beats shipping a broken link. A drawing that produced no
DWG of its own — a PDF-only view, say — gets no archive rather than an empty one.

Contents are found by **snapshotting the output folder before and after the run**: anything
new or changed is what Revit produced. There is no DWG reader here, so what a drawing
references is observed rather than parsed — which is also why this catches the xref'd view
drawings whose names Revit invents.

Entries are stored flat, because a DWG references its images and xrefs by bare filename.
`.pdf` is excluded, so a shared DWG/PDF folder doesn't contaminate the bundle. The loose
files are kept, and a zip failure never fails the export.

**Fonts:** SHX files are copied when found under an Autodesk product's `Fonts` folder;
TrueType fonts are named in `TRANSMITTAL.txt` but not copied, because redistributing them
generally breaches their licence — and a receiver almost always has the common ones. Where
no Autodesk font folder exists at all, the report says so rather than implying none were
needed.

## Multi-model runs

Export setups and view/sheet sets are per-document elements with no cross-document
identity, so an extra model resolves the active model's choices **by name**. Two misses
can't be rescued by copying and always skip, with the reason reported:

- **No view/sheet set of that name** — a `ViewSheetSet` references views that don't exist
  in the other document, so there is nothing to copy.
- **A read-only model** — no transaction is possible.

Copying a naming rule remaps its parameters: built-in ones carry over by their negative
id, shared and project parameters are looked up by name in the target. A rule parameter
with no counterpart there skips that model rather than creating a setup that would
silently produce blank fields.

Choosing **Copy the setup into it** writes into and dirties a model you did not open for
editing. That is intended — the copied setup then appears in that model's own Revit
dialogs — but it does mean the model needs saving afterwards.

All models write into the same destination, so a filename produced by two models aborts
the run before anything is written, naming both.

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
