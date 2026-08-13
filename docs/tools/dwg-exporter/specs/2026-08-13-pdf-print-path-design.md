# Hebrew-safe PDF output via a print driver — design

**Date:** 2026-08-13
**Status:** approved design
**Parent tool:** DWG Exporter (`DwgExporter`)

## Problem

Revit's native PDF exporter mis-orders bidirectional text. A sheet that reads correctly on
screen comes out of `doc.Export(..., PDFExportOptions)` with the Latin runs relocated and the
neutral characters (parentheses, quotes) moved.

The user's own evidence pins the mechanism down. In a three-line schedule cell:

- The **pure-Hebrew line survives** almost intact — only a quote mark drifts.
- The two lines **containing Latin runs break**.
- The Latin runs keep their internal spelling — `LIQUI HARD`, not `DRAH IUQIL`.

That is not a font, encoding or glyph problem. It is the paragraph **base direction**: the
screen renderer auto-detects RTL via Windows text shaping, and the vector PDF writer assumes
LTR. Under an LTR base a pure-RTL run still reads correctly and only its edge neutrals move,
while a mixed line falls apart — exactly what the images show.

Autodesk documents this family of bugs
([Hebrew text position](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Hebrew-text-changes-position-when-edited-in-Revit.html),
[Hebrew tags in DWF export](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Wrong-display-of-the-tags-in-the-Hebrew-text-when-exporting-to-DWF-from-Revit.html)).
It is Revit's, not ours — we pass the setup's options through untouched.

## What was ruled out, and why

Recorded so none of it is retried.

| Approach | Outcome |
|---|---|
| `PDFExportOptions.AlwaysUseRaster` | Works, rejected by the user — files too large, not sharp. |
| Windows "language for non-Unicode programs" = Hebrew | Tested 2026-08-13. No effect. |
| **Microsoft Print to PDF** | Fixes the text, but unusable. Its GPD declares sizes with no classic `DMPAPER` id (`A0`, `A1`, a `600x91` = 6000×910mm entry), and Revit — which enumerates via `DeviceCapabilities` — never sees them. Revit's list tops out at **"E size sheet", 864 × 1118 mm**; not even A0 fits. The driver also declares no `CUSTOMSIZE` block, so it rejects user-created forms outright. Raising the limit means editing `V4Dirs\…\77e218f1.gpd`, a **generated cache with a CRC in the registry** (`V4_Merged_ConfigFile_CRC`) that Windows may regenerate; the durable version means patching a signed DriverStore package. Dead end. |
| **clawPDF** | Rejected before installing: AGPL-3.0, last code push May 2023, **unsigned MSI** installing a printer driver — a non-starter for fleet deployment. |
| Fixing the text at source with bidi marks (RLM `U+200F`) | **Never tested.** Still the only approach that would keep native vector export, and the only one that would work if a sheet ever exceeds what forms can express. Tracked in [backlog.md](../backlog.md). |

## What was established empirically

Verified 2026-08-13 on Windows 11 26200 with PDF24 Creator 11.30.0.

1. **The print path renders bidi correctly.** Confirmed by the user against a real sheet
   through Microsoft Print to PDF. This is the whole basis of the design.
2. **PDF24 exposes 76 paper sizes** including A0, A1, ISO B0, 2A and 4A (1682 × 2381 mm),
   against Microsoft's ~27.
3. **PDF24 accepts server-level custom forms, and honours them at full size.** `AddForm` at
   900 × 5000 mm succeeded, and driver negotiation through a `PreviewPrintController`
   returned `900 × 5000` — no clamping. The same form is **not exposed at all** by Microsoft
   Print to PDF. Revit lists it (user-confirmed).
4. **The 3,276.7 mm ceiling does not apply to named forms.** `DEVMODE.dmPaperWidth` /
   `dmPaperLength` are `short` in tenths of a millimetre, capping *ad-hoc* custom sizes at
   32767 tenths — which is why doPDF documents a 3276 mm maximum. A **named form** is
   selected through `dmFormName` and stores its size in `FORM_INFO_1.Size` as `DWORD`s in
   thousandths of a millimetre. No 16-bit field is involved. Forms beat custom sizes.
5. **`AddForm` requires administrator.** RVTuk runs inside Revit as a normal user, so **the
   add-in cannot register forms at runtime.** This is almost certainly behind the ProSheets
   FAQ entry "Printer Permission Issue (Failed to access print parameters)".
6. **There is no "driver only" PDF24.** The printer is a named pipe
   (`\\.\pipe\PDFPrint`, Local Monitor) serviced by `pdf24.exe -service`, which drives
   Ghostscript. Remove the service and the printer silently produces nothing. Footprint is
   1,080 MB / 2,336 files; the documented MSI properties strip it to roughly 456 MB.

## Decisions

1. **Native export stays; printing is added as a second PDF engine, chosen per run.** Native
   is faster, vector, smaller, and bounded by nothing. Printing exists for sheets carrying
   mixed Hebrew/Latin text. Default is `Native` — the absent-key value — so nothing changes
   for an existing user until they choose otherwise.
2. **A dedicated printer instance, `RVTuk PDF`,** cloned from the PDF24 driver and configured
   for automatic save with no dialog. The user's own `PDF24` printer is never reconfigured —
   PDF24 pops a save-assistant dialog by default, and for a 200-sheet batch that is fatal.
   Precedent: DiRoots ship `diroots.prosheets` for exactly this reason.
3. **Name the file through the print job name, with staging as the fallback.** PDF24 derives
   the output path from the print job name — set the job name to a full path and that is where
   the PDF is written ([PDF24 help](https://help.pdf24.org/en/forums/topic/print-to-pdf24-assistant-from-visual-basic-with-filename/)),
   and the auto-save template's `$fileName` resolves to whatever the printer interface
   supplied. If Revit lets us set the job name, each PDF names itself and there is nothing to
   correlate.

   If it does not, the fallback is a private staging folder: print, wait for a new `.pdf`,
   move it to the computed name. That works but forces decision 4 to be serial. **Settle the
   job-name question first** — it decides the shape of the whole export loop.

4. **Submit every print job first, then export the DWGs, then collect.** `SubmitPrint` returns
   as soon as the job reaches the spooler, so the PDFs are rendered by PDF24's own process
   while Revit gets on with DWG work in the API context. That is real parallelism for free —
   no background thread inside Revit — and by the time the DWGs are done most PDFs have
   landed. Collection then waits only on the stragglers.

   **This depends entirely on decision 3 resolving in favour of job names.** Without them,
   files can only be matched to sheets by order of appearance, and a single failed job
   desynchronises every sheet after it — producing correct-looking names over the wrong
   drawings, which is far worse than being slow. If job names are unavailable, printing stays
   serial and this decision is dropped.
5. **Naming is unchanged.** `NamingRuleEvaluator` already evaluates the PDF setup's rule to
   produce `PlannedExportFile.FileName` for the DWGs. The print path reuses that same string,
   so `.dwg`/`.pdf` basenames stay in lockstep exactly as they do today. This was expected to
   be the expensive part of the change and is already built.
6. **Forms are registered by an elevated helper, never by the add-in.** `RVTuk.FormSetup.exe`
   ships alongside RVTuk, self-elevates, and registers forms via `AddForm`. `RVTukSetup.exe`
   invokes it with a default list at install time; the tool offers a "Register paper sizes for
   this model" action that invokes it with the sizes read from the open model's title blocks.
   One code path, two callers.
7. **Paper size matches at 1:1, never scaled.** A sheet's `SHEET_WIDTH`/`SHEET_HEIGHT` picks
   the smallest registered form that fits it in either orientation. No fit → that sheet is an
   error naming the exact size needed and how to register it, rather than a silently scaled
   drawing.
8. **PDF24 is a prerequisite the installer satisfies by downloading**, not by embedding. The
   MSI is 150 MB+; downloading keeps `RVTukSetup.exe` small, always installs the current
   version, and avoids redistributing someone else's software. No internet → a clear message
   naming pdf24.org, not a silent failure.
9. **Detect before installing.** Many machines already have PDF24 — including any with
   ProSheets, which installs the **Inno Setup EXE build** (`unins000.exe`, an `_is1` registry
   key, blank `WindowsInstaller`), not the MSI. Skip when present; never reinstall over it.

## Changes by layer

### Core (`src/RVTuk.Core/DwgExporter/`)

| File | Responsibility |
|------|----------------|
| `PaperSizeMatcher.cs` *(new)* | `PaperForm` (`Name`, `WidthMm`, `HeightMm`) and `Match(sheetWidthMm, sheetHeightMm, forms)` → the smallest form containing the sheet in either orientation, or null. Pure, and the piece most worth unit-testing: fit, rotated fit, tie-breaking on area, and no-fit. |
| `DwgExportTypes.cs` | `PdfEngine` enum (`Native`, `Print`); `DwgExportRequest.PdfEngine` (default `Native`); `DwgExportRequest.PrinterName`. `DwgExportResult` gains nothing — printing failures are ordinary error lines. |
| `DwgExportSettingsStore.cs` | `DwgExportPdfEngine` (string, absent → `Native`) and `DwgExportPrinterName` (absent → `RVTuk PDF`). Same net48 uninitialized-object rule as every other key. |

### Revit (`src/RVTuk.Revit/DwgExporter/`)

| File | Responsibility |
|------|----------------|
| `PrinterForms.cs` *(new)* | `EnumForms` via P/Invoke — **read-only, no elevation** — returning `IReadOnlyList<PaperForm>` for a printer. Also `TitleBlockSizes(doc)`, reading `SHEET_WIDTH`/`SHEET_HEIGHT` off each sheet so the helper can be told what to register. |
| `SheetPdfPrinter.cs` *(new)* | The `PrintManager` path. `Submit(doc, file, form)` configures the printer and job name and calls `SubmitPrint()` — it does **not** wait. `Collect(expected, timeout)` waits for the outstanding files and reports what never arrived. Splitting submit from collect is what lets decision 4 overlap the two formats. |
| `SheetDwgExporter.cs` | `Export` branches on `request.PdfEngine`: `Native` keeps today's `doc.Export(...)` call at [line 152](../../../src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs); `Print` submits via `SheetPdfPrinter` instead. The DWG half is untouched. |
| `DwgExportRunner.cs` | Owns the new run order — submit every print job, export the DWGs, then collect. Per-drawing transmittal snapshots must stay bracketed around **the DWG export only**, since printed PDFs now land at unpredictable moments; harmless either way, because `TransmittalBuilder` already excludes `.pdf` by extension. |

### UI (`src/RVTuk.UI/DwgExporter/`)

A **PDF engine** choice in the format row, enabled only when PDF is ticked:

- **Fast (native)** — today's behaviour. Vector, any size, *mis-orders Hebrew mixed with English*.
- **Hebrew-safe (print via `RVTuk PDF`)** — slower, needs a registered paper size per sheet.

The label has to say what the trade is; "native vs print" means nothing to the person choosing.
Plus a **Register paper sizes for this model…** button that launches the elevated helper, with
a UAC prompt, and reports what it registered.

### Installer (`installer/`)

| Piece | Responsibility |
|------|----------------|
| `RVTuk.FormSetup` *(new project)* | Self-elevating console exe. `--register <name>:<w>x<h>` (repeatable), `--defaults`, `--list`, `--remove <name>`. Wraps `OpenPrinter`(server, `SERVER_ACCESS_ADMINISTER`) + `AddForm`/`DeleteForm`/`EnumForms`. Not in `RVTuk.sln`, same as `RVTuk.Setup` — it has no `Release{year}` configs. |
| `RVTuk.Setup` | Detects PDF24 (both the MSI and Inno registry shapes); if absent, downloads the official MSI and runs it silently; creates the `RVTuk PDF` printer instance and configures it for automatic save; invokes `RVTuk.FormSetup --defaults`. `--uninstall` removes the printer instance and RVTuk's own forms, and leaves PDF24 alone — it may predate us. |

Silent install line, all documented PDF24 properties:

```
msiexec /i pdf24-creator.msi /qn ADDLOCAL=ALL REMOVE=WebView2 FAXPRINTER=No
        DESKTOPICONS=No AUTOUPDATE=No EXTENDSHELLCONTEXTMENU=No REGISTERREADER=No
```

`REMOVE=WebView2` alone drops 624 MB of the 1,080 MB footprint.

## Data flow — a run producing both formats

```
plan every file          → PlannedExportFile.FileName        (unchanged, shared by both formats)
PrinterForms.EnumForms   → registered forms for "RVTuk PDF"          once per run

for each file:                                                       ── submit phase
  read the sheet size    → SHEET_WIDTH / SHEET_HEIGHT
  PaperSizeMatcher.Match → the form, or an error naming the size
  job name               → <PdfFolder>\<FileName>.pdf
  SubmitPrint()          → returns at once; PDF24 renders out of process

for each file:                                                       ── DWG phase
  doc.Export(...)        → unchanged, runs while the PDFs spool

collect                  → wait for the outstanding .pdf paths, timeout → errors
transmittal              → snapshots bracket the DWG phase only
```

## Error handling

- **No form fits the sheet** → an error naming the sheet, its size in mm, and the
  "Register paper sizes" action. That sheet is not submitted; the run continues.
- **A file never arrives before the collect timeout** → an error naming the sheet. Because
  each job carries its own destination path, one missing file cannot misname any other — which
  is the entire reason decision 4 is safe.
- **PDF24 is not installed** → the print engine is disabled in the dialog with the reason
  shown, rather than failing at run time.
- **A PDF already exists at the target path** → caught by the existing pre-flight
  (`OutputFileExists`), unchanged.
- **A printed PDF lands in a shared DWG/PDF folder** → already excluded from transmittals by
  extension; no change needed.
- **Engine is `Native`** → not one line of this executes.

## Open questions — spike before implementing

1. **Can Revit's print job name be set?** This is the pivotal one — decisions 3 and 4 both
   collapse without it, and the export loop goes back to serial print-and-rename.
   `PrintManager` exposes no job-name property; find out what Revit puts in
   `DOCINFO.lpszDocName` and whether `PrintToFileName` or anything else influences it. Verify
   by printing one sheet and seeing where PDF24 writes it. **Do this spike first.**
2. **How to print a single arbitrary sheet without dirtying the model.** `PrintRange.Select`
   drives `ViewSheetSetting.CurrentViewSheetSet`, and saving a set creates a `ViewSheetSet`
   element — a document modification in a model the user did not open for editing.
   `PrintRange.Current` avoids it but needs each sheet activated, which is slow and moves the
   user's active view. Setting the in-session set without `SaveAs` may thread the needle.
3. **Spooler queue depth.** Decision 4 queues every sheet at once, and a 5000 × 900 sheet
   spools a lot of PostScript. Check whether a 200-sheet set needs a bounded submit window
   rather than a free-for-all.
4. **No modal dialog mid-batch.** `PrintToFileName` is documented as unreliable, with reports
   of Revit ignoring it and showing a Save dialog. One modal in a 200-sheet run is a
   showstopper, so confirm the configured `RVTuk PDF` instance never prompts.
5. **Configuring the `RVTuk PDF` instance for automatic save non-interactively.** PDF24 stores
   per-user settings under `HKCU\SOFTWARE\PDF24` and machine-wide ones under
   `HKLM\SOFTWARE\PDF24`; find which keys hold a printer instance's auto-save profile, and
   whether the installer can write them rather than driving the PDF24 GUI.

## Testing

**Core (`tests/RVTuk.Core.Tests/DwgExporter/`)**

- `PaperSizeMatcherTests` — exact fit; sheet fits only rotated; two forms fit and the smaller
  by area wins; nothing fits returns null; a sheet larger than every form returns null.
- `DwgExportSettingsStoreTests` — `PdfEngine` defaults to `Native`, round-trips, and an
  unrecognised stored value falls back to `Native` rather than throwing.

**In Revit (manual)** — the Hebrew schedule sheet from the original report, printed and
compared against the native export; a 900 × 1200 sheet; a 900 × 5000 sheet, with the output
PDF's page dimensions *measured* rather than eyeballed; a sheet whose size has no registered
form, confirming the error names the size; a mixed DWG+PDF run, confirming basenames still
pair; a run with per-drawing transmittals, confirming no `.pdf` enters an archive.

The batch case needs deliberate attention, because decision 4's failure mode is silent. Run a
set of **at least 20 sheets with visibly different content**, then open every PDF and check the
drawing matches its filename — not just that the right number of files appeared. Include one
sheet guaranteed to fail (no registered form) and confirm the sheets after it are still named
correctly.

**Installer (manual)** — a machine with no PDF24; a machine with PDF24 from ProSheets (Inno
build), confirming it is detected and not reinstalled; `--uninstall` leaving PDF24 in place.

## Out of scope

- Replacing native export. It stays the default and the only path for anything a form cannot
  express.
- Fixing the text at source with bidi control marks. Still untested and still the only fix
  that would keep vector output — [backlog.md](../backlog.md).
- Bundling the PDF24 MSI into the payload zip.
- Any attempt to install "just the driver" — established fact 6 above shows there is no such
  thing.
- Auto-detecting which sheets contain mixed-direction text to pick the engine per sheet. The
  choice is per run; revisit only if runs get slow enough to matter.
