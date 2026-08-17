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
6. **`PrintToFileName` becomes the print job name, verbatim.** Proven by macro spike on
   2026-08-13: setting `PrintManager.PrintToFileName` to a full path and calling
   `SubmitPrint()` produced a queued job whose `DocumentName` was exactly
   `C:\RVTuk-Spike\spike-0bb1cef8….pdf`. Revit neither normalised nor rejected it — it read
   back identical. **This is the channel**, and it is the same one ProSheets uses.
7. **Revit forces `PrintToFile = true` for virtual printers.** Trying to set it false throws
   *"The PrintToFile property cannot be set to false while the printer is Virtual."* So the
   job-name channel is not an opt-in we have to arrange — for a PDF printer it is the only
   mode Revit will operate in.
8. **`PrintManager` enforces ordering constraints between its own properties**, and throws
   rather than correcting. `CombinedFile = false` is rejected while `PrintRange` is
   `Current`/`Visible` (*"CombinedFile property cannot be set to false when the Print Range is
   Current/Visible!"*). Assume every property pairing needs checking, not just this one.
9. **`doc.PrintManager` itself can throw** `"Failed to access print parameters"` — hit during
   the same spike after printer state was changed underneath a running Revit. It cleared only
   on a Revit restart. ProSheets documents the same error, blaming printer permissions or a
   missing default printer. This is a real-world condition, not a theoretical one.
10. **The PDF24 save dialog is real and blocking.** With auto-save unconfigured, the spike's
    single sheet left `pdf24-DocTool` sitting on a *"Save As"* window. One of those inside a
    200-sheet run would stop everything, which is why the reference configuration below is
    part of the design rather than a nicety.
11. **Mixed paper sizes in one batch come out correct.** A ProSheets export of six sheets on
    2026-08-13 produced three distinct page sizes, interleaved, every one right: A0
    (841 × 1189), A4 (210 × 297) and a custom 317 × 773 — the custom one submitted **two
    seconds after** an A4. The size travels with the job; changing `PrintManager` between
    submissions does not leak the last setting onto earlier ones.
12. **Hebrew filenames survive the rename.** The same run produced `מממ.pdf` and
    `בניין 01,.pdf` intact. Naming the file ourselves after the job completes, rather than
    pushing the real name through the spooler, is what makes this safe.
13. **Revit is the bottleneck, not PDF24 — but not by much.** Gaps between submissions ran
    5–39 s (Revit rendering each sheet); PDF24 took 2–27 s to convert, the A0s at the top of
    that range. The two are comparable, so overlapping them is worth something, though less
    than a naive reading of "SubmitPrint returns immediately" suggests.
14. **`PrintRange.Select` with the in-session set prints any sheet and dirties nothing.**
    Measured 2026-08-13 on a saved model, targeting a sheet that was deliberately *not* the
    active view:

    ```
    BEFORE  IsModified = False   ViewSheetSet count = 9
    AFTER   IsModified = False   ViewSheetSet count = 9
    ```

    The recipe: `pm.PrintRange = PrintRange.Select`, then
    `vss.CurrentViewSheetSet = vss.InSession`, then assign a one-view `ViewSet` to
    `CurrentViewSheetSet.Views`. **Never call `SaveAs`** — that is what would create a
    persistent `ViewSheetSet` element. No transaction was needed, and the queued job still
    carried our full path as its `DocumentName`, so the naming channel works for arbitrary
    sheets and not just the active view.
15. **There is no "driver only" PDF24.** The printer is a named pipe
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
3. **The job name carries a GUID staging path; we do the final rename.** PDF24 derives the
   output path from the print job name, and the auto-save template's `$fileName` resolves to
   what the printer interface supplied. So the job name is our channel — but it carries
   `<staging>\<guid>.pdf`, **not** the final filename.

   The GUID is the correlation key. It is unique per job, so a returning file identifies its
   sheet exactly with no dependence on ordering; it is immune to PDF24's "Strings to erase in
   filenames" post-processing, which can silently alter a name; and it keeps Hebrew and other
   non-ASCII characters out of a path that has to survive the spooler. The final name — the
   one `NamingRuleEvaluator` computed — is applied by our own move, where we control it
   completely.

   This is exactly what ProSheets does, observed 2026-08-13 by pausing `diroots.prosheets` and
   reading the queued job name:
   `C:\Users\danie\AppData\Local\DiRoots\ProSheets\Temp\PDF\5e45beb0-…-0c2116a0429f.pdf`.
   Which also settles the open question — **a Revit add-in can set the job name to a full
   path.** Only the API call used to do it still needs identifying.

4. **Submit print jobs in a bounded window, export the DWGs, then collect.** `SubmitPrint`
   returns as soon as the job reaches the spooler, so PDFs render in PDF24's process while
   Revit gets on with DWG work in the API context — real parallelism with no background thread
   inside Revit.

   Not a free-for-all, though. The observed ProSheets job was **44 MB of spool for one page**;
   a 200-sheet set submitted at once would put roughly 9 GB in the spool folder, and the
   5000 × 900 sheets will be worse. Submit up to N jobs (start at 10), and top the window up
   as jobs drain. Correlation is by GUID, so throttling changes throughput and nothing else.

   **Mixed paper sizes need no special handling** — established fact 11 settles it. Sheets can
   be submitted in whatever order the plan produces them, and each job keeps the size set at
   its own `SubmitPrint()`. Grouping by paper size remains a free optimisation if the
   implementation finds it convenient, since it reduces churn on a `PrintManager` that is
   demonstrably unforgiving about its own state (fact 8), but it is not needed for
   correctness and should not complicate the loop.
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
| `SheetPdfPrinter.cs` *(new)* | The `PrintManager` path. `Submit(doc, file, form)` issues a GUID, sets the job name to `<staging>\<guid>.pdf`, calls `SubmitPrint()` and returns the GUID — it does **not** wait. `Collect(outstanding, timeout)` waits on each GUID, moves the file to its planned name, and reports what never arrived. Splitting submit from collect is what lets decision 4 overlap the two formats; the GUID is what makes it safe. |
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
| `RVTuk.Setup` | Detects PDF24 (both the MSI and Inno registry shapes); if absent, downloads the official MSI and runs it silently; creates the `RVTuk PDF` printer instance and applies the reference configuration below; invokes `RVTuk.FormSetup --defaults`. `--uninstall` removes the printer instance and RVTuk's own forms, and leaves PDF24 alone — it may predate us. |

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

once per run:
  PrintRange             → Select
  CurrentViewSheetSet    → InSession        never SaveAs - that creates an element

for each file, up to N outstanding:                                  ── submit phase
  read the sheet size    → SHEET_WIDTH / SHEET_HEIGHT
  PaperSizeMatcher.Match → the form, or an error naming the size
  CurrentViewSheetSet.Views → a ViewSet holding just this sheet
  CombinedFile           → true (one sheet per submission, one output file)
  guid                   → remember guid → PlannedExportFile
  PrintToFileName        → <staging>\<guid>.pdf   (becomes the job name)
  Apply(); SubmitPrint() → returns at once; PDF24 renders out of process

for each file:                                                       ── DWG phase
  doc.Export(...)        → unchanged, runs while the PDFs spool

collect                  → for each guid still outstanding, wait for <staging>\<guid>.pdf,
                           then move to <PdfFolder>\<FileName>.pdf; timeout → errors
transmittal              → snapshots bracket the DWG phase only
staging swept            → anything left behind is deleted, never adopted
```

## Error handling

- **No form fits the sheet** → an error naming the sheet, its size in mm, and the
  "Register paper sizes" action. That sheet is not submitted; the run continues.
- **A file never arrives before the collect timeout** → an error naming the sheet. Because
  every job is keyed by its own GUID, a missing file can never be confused with another
  sheet's — which is what makes decision 4 safe.
- **An unexpected file appears in staging** → ignored. Only GUIDs this run issued are
  collected, so a leftover from a crashed earlier run cannot be adopted.
- **PDF24 is not installed** → the print engine is disabled in the dialog with the reason
  shown, rather than failing at run time.
- **`doc.PrintManager` throws "Failed to access print parameters"** → abort the run before
  printing anything, with a message naming the three known causes: a printer changed
  underneath a running Revit (restart Revit), no Windows default printer, or missing "Manage
  this printer" / "Manage documents" rights on the printer. Established fact 9 — this happened
  during the spike, so it needs a real message, not a raw exception.
- **A PDF already exists at the target path** → caught by the existing pre-flight
  (`OutputFileExists`), unchanged.
- **A printed PDF lands in a shared DWG/PDF folder** → already excluded from transmittals by
  extension; no change needed.
- **Engine is `Native`** → not one line of this executes.

## Open questions — spike before implementing

> **Every design-shaping spike is closed** (2026-08-13). `PrintToFileName` is the job-name
> channel (facts 6, 7); mixed paper sizes in one batch come out correct (fact 11); and
> `PrintRange.Select` on the in-session set prints any sheet without dirtying the model
> (fact 15). What remains only tunes the implementation — **the design is ready to build
> against.**

1. **Where the window size should sit.** Decision 4 starts at 10 outstanding jobs on the
   strength of one 44 MB sample. Measure a real set — spool size varies hugely with sheet
   content — and check what the spool folder does on the biggest sheets.

   **Do not measure this with print-queue depth.** That was tried and it does not answer the
   question: a job leaves the queue once the spooler has finished writing to the pipe, while
   PDF24 goes on converting outside it. Observed max depth was 1 across nine jobs even though
   a submission at 17:08:08 overlapped a conversion that only finished at 17:08:10. Measure
   the spool folder's size on disk, or PDF24's own process activity, instead.
2. **Writing the auto-save settings non-interactively.** The values are now known (see the
   reference configuration below); what is not known is where PDF24 keeps them.
   Per-user settings live under `HKCU\SOFTWARE\PDF24` and machine-wide ones under
   `HKLM\SOFTWARE\PDF24` — find the per-instance auto-save keys so the installer can write
   them instead of driving the GUI. Note that the installer runs elevated while these may be
   per-user, so this may need a first-run step inside RVTuk rather than install-time setup.

## Reference configuration

The `diroots.prosheets` instance, read from the PDF24 settings GUI on 2026-08-13. `RVTuk PDF`
should be set up the same way — this is a known-working configuration, not a guess.

| Setting | Value |
|---|---|
| PDF Printer Tool | **Automatically save documents after printed** |
| Auto Save → Output Directory | the staging folder |
| Auto Save → Filename | `$fileName` |
| Auto Save → Profile | Best quality |
| Overwrite existing file | on |
| **Use a file name chooser before saving the file** | **off** — this is the modal that would kill a batch |
| Show progress while saving | off |
| Open folder after saving | off |

`$fileName` is what makes the job-name channel work: it resolves to the name the printer
interface supplied. Also note the **"Strings to erase in filenames"** section — PDF24 can
rewrite names on the way out, which is the second reason decision 3 puts a GUID in the job
name rather than the real one.

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

The batch case needs deliberate attention, because decision 4 has two silent failure modes.
Run a set of **at least 20 sheets with visibly different content, spanning at least three
paper sizes**, then check every output on both axes:

- **Right drawing under the right name** — open them, don't just count files.
- **Right page size for each** — measure, don't eyeball. A 900 × 1200 drawing on an A1 page
  looks like a drawing until someone plots it.

Include one sheet guaranteed to fail (no registered form) and confirm the sheets after it are
still correct on both counts.

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
