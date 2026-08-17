# DWG Exporter — Hebrew-safe PDF via a print driver — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second PDF engine to the DWG Exporter that produces PDFs by *printing* through a PDF24 printer instance instead of `doc.Export`, so sheets mixing Hebrew with English come out with the text in the right order. Native export stays the default and is untouched.

**Architecture:** The decision logic that needs no live document — matching a sheet's size to a registered paper form, the engine setting and its persistence — is pure code in `RVTuk.Core.DwgExporter`. The Revit layer gains `PrinterForms` (read the spooler's form list, read title-block sizes) and `SheetPdfPrinter` (submit/collect against `PrintManager`), and `DwgExportRunner` gains a new run order: submit every print job, export the DWGs while they render, then collect. A new self-elevating console exe, `RVTuk.FormSetup`, registers paper forms, because `AddForm` needs administrator and the add-in never has it.

**Tech Stack:** C# (multi-targeting `net48` for Revit 2024 and `net8.0-windows` for Revit 2025), WPF/MVVM, xunit, Revit API (`Nice3point.Revit.Api.RevitAPI`, compile-only), Win32 spooler API via P/Invoke.

**Spec:** [`../specs/2026-08-13-pdf-print-path-design.md`](../specs/2026-08-13-pdf-print-path-design.md)

## Global Constraints

- `RVTuk.Core` must never reference the Revit API or WPF. `RVTuk.UI` must never reference the Revit API.
- Namespace = root namespace + folder path, exactly. New Core files go in `RVTuk.Core.DwgExporter`, new Revit files in `RVTuk.Revit.DwgExporter`, new UI files in `RVTuk.UI.DwgExporter.ViewModels`.
- Source files under `src/` use **block** namespaces; test files under `tests/` use **file-scoped** namespaces.
- Nullable reference types are enabled across the solution.
- **Any new `AppConfig` key must have "absent from the file" and "the out-of-box default" be the same value.** net48's `DataContractJsonSerializer` builds `AppConfig` through `FormatterServices.GetUninitializedObject`, so property initializers never run. Both new keys are strings whose absent value is `""`, resolved to their real defaults in `DwgExportSettingsStore`.
- The DWG Exporter is Revit **2024/2025 only** — not compiled into `KKarea.Revit` (2023).
- **With `PdfEngine.Native` selected, not one line of the new code may execute.** That is today's behaviour and it is the default.
- Printing must not modify the document. Established fact 14 in the spec proves the recipe that achieves this; deviating from it risks dirtying a model the user never opened for editing.

**Commands used throughout:**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

```bash
dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024
```

```bash
dotnet build RVTuk.sln -c Release2025
```

## The validated Revit call order

Every line below was confirmed by macro spike on 2026-08-13. Do not improvise around it — three of these calls throw if made in the wrong combination.

```csharp
var pm = doc.PrintManager;              // can throw "Failed to access print parameters" - see Task 4
pm.SelectNewPrintDriver("RVTuk PDF");
pm.PrintRange = PrintRange.Select;

var vss = pm.ViewSheetSetting;
vss.CurrentViewSheetSet = vss.InSession;   // NEVER SaveAs - that creates a ViewSheetSet element

// per sheet:
var set = new ViewSet();
set.Insert(sheet);
vss.CurrentViewSheetSet.Views = set;
pm.PrintSetup.CurrentPrintSetting.PrintParameters.PaperSize = form;
pm.CombinedFile = true;                 // one sheet per submission = one output file
pm.PrintToFile = true;                  // Revit forces this true for virtual printers anyway
pm.PrintToFileName = stagingPath;       // THIS becomes the print job name, and PDF24 writes there
pm.Apply();
pm.SubmitPrint();                       // returns as soon as the job reaches the spooler
```

Known traps, all observed:

- `CombinedFile = false` throws while `PrintRange` is `Current`/`Visible`.
- `PrintToFile = false` throws while the printer is virtual.
- `doc.PrintManager` can throw and stay broken until Revit restarts.

---

### Task 1: Core types and config — the engine setting

**Files:**
- Modify: `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs`
- Modify: `src/RVTuk.Core/Shared/Config/AppConfig.cs`
- Modify: `src/RVTuk.Core/DwgExporter/DwgExportSettingsStore.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/DwgExportSettingsStoreTests.cs`

**Interfaces:**
- Produces: `PdfEngine` enum (`Native`, `Print`); `DwgExportRequest.PdfEngine` (default `Native`); `DwgExportRequest.PrinterName` (default `DwgExportDefaults.DefaultPrinterName`); `DwgExportDefaults.DefaultPrinterName == "RVTuk PDF"`; `AppConfig.DwgExportPdfEngine` (string); `AppConfig.DwgExportPrinterName` (string).

- [ ] **Step 1: Write the failing tests**

Append to `tests/RVTuk.Core.Tests/DwgExporter/DwgExportSettingsStoreTests.cs`:

```csharp
[Fact]
public void PdfEngine_AbsentFromConfig_IsNative()
{
    // The absent-key value and the out-of-box default must agree: net48 builds AppConfig
    // via GetUninitializedObject, so a config written before this key existed arrives "".
    var config = new AppConfig();
    var request = DwgExportSettingsStore.Load(config);
    Assert.Equal(PdfEngine.Native, request.PdfEngine);
}

[Fact]
public void PdfEngine_RoundTrips()
{
    var config = new AppConfig();
    DwgExportSettingsStore.Save(config, new DwgExportRequest { PdfEngine = PdfEngine.Print });
    Assert.Equal(PdfEngine.Print, DwgExportSettingsStore.Load(config).PdfEngine);
}

[Fact]
public void PdfEngine_UnrecognisedStoredValue_FallsBackToNative()
{
    // Never throw on a config someone hand-edited or a value from a future version.
    var config = new AppConfig { DwgExportPdfEngine = "Quantum" };
    Assert.Equal(PdfEngine.Native, DwgExportSettingsStore.Load(config).PdfEngine);
}

[Fact]
public void PrinterName_AbsentFromConfig_IsTheDefaultInstance()
{
    var request = DwgExportSettingsStore.Load(new AppConfig());
    Assert.Equal(DwgExportDefaults.DefaultPrinterName, request.PrinterName);
}
```

- [ ] **Step 2: Implement** — add the enum and both properties, then parse in the store with `Enum.TryParse(..., out var e) ? e : PdfEngine.Native`, and `string.IsNullOrWhiteSpace(name) ? DefaultPrinterName : name`.
- [ ] **Step 3: Verify** — `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
- [ ] **Step 4: Commit** — `feat(dwg-exporter): PdfEngine setting on the request and in config`

---

### Task 2: `PaperSizeMatcher` — pick the form a sheet fits on

The only genuinely algorithmic piece, and pure. Do this before any Revit work.

**Files:**
- Create: `src/RVTuk.Core/DwgExporter/PaperSizeMatcher.cs`
- Test: `tests/RVTuk.Core.Tests/DwgExporter/PaperSizeMatcherTests.cs`

**Interfaces:**
- Produces: `PaperForm` (`Name`, `WidthMm`, `HeightMm`); `PaperSizeMatcher.Match(double sheetWidthMm, double sheetHeightMm, IEnumerable<PaperForm> forms)` → the best `PaperForm` or `null`.

**Rules:**
- A form fits if the sheet fits in **either** orientation, within a **0.5 mm tolerance** (a title block reported as 840.9 mm must match an 841 mm form).
- Among fitting forms, **smallest area wins** — never scale, never pad more than necessary.
- Ties broken by smallest longest-edge, then by ordinal name, so the result is deterministic.
- No fit → `null`. The caller turns that into an error naming the size.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/DwgExporter/PaperSizeMatcherTests.cs`:

```csharp
namespace RVTuk.Core.Tests.DwgExporter;

using RVTuk.Core.DwgExporter;
using Xunit;

public class PaperSizeMatcherTests
{
    private static PaperForm Form(string name, double w, double h) =>
        new PaperForm { Name = name, WidthMm = w, HeightMm = h };

    private static readonly PaperForm[] Standard =
    {
        Form("A4", 210, 297), Form("A3", 297, 420), Form("A2", 420, 594),
        Form("A1", 594, 841), Form("A0", 841, 1189),
    };

    [Fact]
    public void ExactSize_PicksThatForm() =>
        Assert.Equal("A1", PaperSizeMatcher.Match(594, 841, Standard)!.Name);

    [Fact]
    public void SlightlyUnderSize_StillMatchesWithinTolerance() =>
        Assert.Equal("A1", PaperSizeMatcher.Match(593.7, 840.8, Standard)!.Name);

    [Fact]
    public void LandscapeSheet_MatchesPortraitForm() =>
        Assert.Equal("A1", PaperSizeMatcher.Match(841, 594, Standard)!.Name);

    [Fact]
    public void SmallestFittingForm_Wins() =>
        // Fits A2, A1 and A0. Must not pad onto A0.
        Assert.Equal("A2", PaperSizeMatcher.Match(400, 500, Standard)!.Name);

    [Fact]
    public void NothingBigEnough_ReturnsNull() =>
        Assert.Null(PaperSizeMatcher.Match(900, 5000, Standard));

    [Fact]
    public void CustomLongSheet_MatchesItsOwnForm()
    {
        var forms = Standard.Append(Form("RVTuk 900x5000", 900, 5000));
        Assert.Equal("RVTuk 900x5000", PaperSizeMatcher.Match(900, 5000, forms)!.Name);
    }

    [Fact]
    public void EmptyFormList_ReturnsNull() =>
        Assert.Null(PaperSizeMatcher.Match(210, 297, new PaperForm[0]));

    [Fact]
    public void EqualAreaForms_ResolveDeterministically()
    {
        var forms = new[] { Form("Bravo", 300, 400), Form("Alpha", 400, 300) };
        Assert.Equal("Alpha", PaperSizeMatcher.Match(290, 390, forms)!.Name);
        Assert.Equal("Alpha", PaperSizeMatcher.Match(290, 390, forms.Reverse())!.Name);
    }
}
```

- [ ] **Step 2: Implement** `PaperSizeMatcher` to satisfy exactly those rules. Keep it pure — no I/O, no Revit types.
- [ ] **Step 3: Verify** — `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
- [ ] **Step 4: Commit** — `feat(dwg-exporter): PaperSizeMatcher picks the smallest form a sheet fits`

---

### Task 3: `PrinterForms` — read the spooler's forms and the model's sheet sizes

**Files:**
- Create: `src/RVTuk.Revit/DwgExporter/PrinterForms.cs`

**Interfaces:**
- Produces: `PrinterForms.ForPrinter(string printerName)` → `IReadOnlyList<PaperForm>`; `PrinterForms.TitleBlockSizes(Document doc, IEnumerable<ViewSheet> sheets)` → sheet → (width mm, height mm).

**Notes:**
- Enumerate with `System.Drawing.Printing.PrinterSettings.PaperSizes` rather than P/Invoking `EnumForms`. It is the same `DeviceCapabilities` source Revit itself reads, needs no elevation, and `PaperSize.Width`/`Height` are hundredths of an inch — multiply by `0.254` for mm.
- Sheet size comes from the built-in `SHEET_WIDTH` / `SHEET_HEIGHT` parameters, which are in feet; convert with `UnitUtils`.
- Return an empty list rather than throwing when the printer is absent; Task 7 turns that into a disabled UI option.

- [ ] **Step 1: Implement** as described.
- [ ] **Step 2: Verify** — `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
- [ ] **Step 3: Commit** — `feat(dwg-exporter): read registered paper forms and title-block sizes`

---

### Task 4: `SheetPdfPrinter` — submit and collect

The heart of the change. Follow "The validated Revit call order" above literally.

**Files:**
- Create: `src/RVTuk.Revit/DwgExporter/SheetPdfPrinter.cs`

**Interfaces:**
- Produces: `PrintJobTicket` (`Guid`, `StagingPath`, `PlannedExportFile`); `SheetPdfPrinter.Prepare(Document doc, string printerName)` (once-per-run setup, returns a disposable/handle); `Submit(...)` → `PrintJobTicket`; `Collect(IEnumerable<PrintJobTicket> outstanding, string pdfFolder, TimeSpan timeout)` → moved files plus error strings.

**Notes:**
- `Submit` **must not wait**. It issues a GUID, sets `PrintToFileName` to `<staging>\<guid>.pdf`, calls `SubmitPrint()`, and returns.
- `Collect` polls for each ticket's staging file, waits for its size to stop changing (the spooler writes progressively), then moves it to `<pdfFolder>\<FileName>.pdf`.
- Only GUIDs this run issued are collected. A stray file in staging is ignored, never adopted.
- Wrap the `doc.PrintManager` access specifically: on failure, throw a typed exception carrying the three known causes (printer changed under a running Revit → restart Revit; no Windows default printer; missing "Manage this printer" / "Manage documents" rights). Task 6 turns that into a run-aborting message.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify** — builds under both configs.
- [ ] **Step 3: Commit** — `feat(dwg-exporter): print sheets to PDF via PrintManager, submit and collect`

---

### Task 5: `SheetDwgExporter` — branch on the engine

**Files:**
- Modify: `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs`

**Notes:**
- `Export` currently calls `doc.Export(request.PdfFolder, ..., pdfOptions)` at line 152. Under `PdfEngine.Print` that call is replaced by a `SheetPdfPrinter.Submit`, and the returned ticket is handed back to the runner.
- The DWG half is untouched. So is the whole `PdfEngine.Native` path.
- `GetPdfOptions` is still needed under `Native` only — do not compute it for a printed run.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify** — builds; existing tests still pass.
- [ ] **Step 3: Commit** — `feat(dwg-exporter): route PDF output through the chosen engine`

---

### Task 6: `DwgExportRunner` — the new run order

**Files:**
- Modify: `src/RVTuk.Revit/DwgExporter/DwgExportRunner.cs`

**Notes:**
- New order for a printed run: **submit all print jobs (bounded window, start at 10 outstanding) → export the DWGs → collect the PDFs**. PDF24 renders in its own process while Revit does DWG work.
- Mixed paper sizes need no grouping (spec fact 11), so submit in plan order.
- Create and sweep a private staging folder. Anything left at the end is deleted.
- **Per-drawing transmittal snapshots must bracket the DWG phase only.** Printed PDFs now land at unpredictable moments; `TransmittalBuilder` already excludes `.pdf` by extension, so this is about keeping the diffs meaningful rather than correctness.
- Progress reporting covers both phases — a run that looks frozen during collection is a support call.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify** — builds; existing tests still pass.
- [ ] **Step 3: Commit** — `feat(dwg-exporter): submit prints, export DWGs, then collect`

---

### Task 7: The dialog — engine choice and paper-size registration

**Files:**
- Modify: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml` (+ `.cs`)
- Modify: `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`
- Modify: `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs` (wire the delegates)

**Notes:**
- Two radio buttons in the format row, enabled only when PDF is ticked. Label them by **what they do**, not by mechanism:
  - *Fast (native)* — vector, any size, **mis-orders Hebrew mixed with English**
  - *Hebrew-safe (print)* — slower, needs a registered paper size per sheet
- A **Register paper sizes for this model…** button launches `RVTuk.FormSetup` with the sizes from `PrinterForms.TitleBlockSizes`, and reports what it registered.
- When the printer is missing, disable the print option and say why inline. Never let a run fail at the point of printing for a reason the dialog could have shown.
- The Revit layer passes these in as `Func<>`/`Action` delegates — `RVTuk.UI` must not reference the Revit API.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify** — `dotnet build RVTuk.sln -c Release2025`
- [ ] **Step 3: Commit** — `feat(dwg-exporter): choose the PDF engine and register paper sizes from the dialog`

---

### Task 8: `RVTuk.FormSetup` — the elevated form registrar

**Files:**
- Create: `installer/RVTuk.FormSetup/` (csproj, `Program.cs`, `SpoolerForms.cs`)

**Notes:**
- Self-elevating net48 console exe. **Not in `RVTuk.sln`** — same reasoning as `RVTuk.Setup`: it has no `Release{year}` configs.
- Flags: `--register "<name>:<w>x<h>"` (repeatable, mm), `--defaults`, `--list`, `--remove <name>`.
- Wraps `OpenPrinter`(server, `SERVER_ACCESS_ADMINISTER`) + `AddForm` / `DeleteForm`. `FORM_INFO_1.Size` is in **thousandths of a millimetre** — this is the field that escapes the 3,276.7 mm `DEVMODE` ceiling, so it must stay a `DWORD`.
- Idempotent: `AddForm` returning win32 `1902` (already exists) is success, not failure.
- Exit non-zero with a readable message when elevation is refused, so the caller can report it.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify manually** — `--register "RVTuk 900x5000:900x5000"`, then `--list`, then confirm it appears in Revit's paper list, then `--remove`.
- [ ] **Step 3: Commit** — `feat(installer): RVTuk.FormSetup registers paper forms with elevation`

---

### Task 9: `RVTuk.Setup` — PDF24 and the printer instance

**Files:**
- Modify: `installer/RVTuk.Setup/` (detection, install, printer instance)
- Modify: `Build-Installer.ps1` (stage `RVTuk.FormSetup.exe` into the payload)

**Notes:**
- **Detect first.** PDF24 may already be present, including as the Inno build ProSheets installs — that shows an `_is1` registry key with a blank `WindowsInstaller` value, not an MSI product code. Check both shapes and skip when found.
- If absent, download the official MSI and run:

  ```
  msiexec /i pdf24-creator.msi /qn ADDLOCAL=ALL REMOVE=WebView2 FAXPRINTER=No
          DESKTOPICONS=No AUTOUPDATE=No EXTENDSHELLCONTEXTMENU=No REGISTERREADER=No
  ```

  `REMOVE=WebView2` alone drops 624 MB of the 1,080 MB footprint. No internet → a clear message naming pdf24.org, never a silent failure.
- Create the `RVTuk PDF` printer instance and apply the reference configuration from the spec. **"Use a file name chooser before saving the file" must be off** — that is the modal that would stop a 200-sheet batch, and it was observed blocking a single sheet during the spike.
- Invoke `RVTuk.FormSetup --defaults`.
- `--uninstall` removes the printer instance and RVTuk's own forms, and **leaves PDF24 alone** — it may predate us.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Verify manually** — a machine without PDF24; a machine with the ProSheets Inno build; `--uninstall` leaving PDF24 installed.
- [ ] **Step 3: Commit** — `feat(installer): install PDF24 and the RVTuk PDF printer instance`

---

### Task 10: Docs and the in-Revit checklist

**Files:**
- Modify: `docs/tools/dwg-exporter/README.md`, `docs/tools/dwg-exporter/backlog.md`, `CLAUDE.md`

- [ ] **Step 1:** Replace the README's "Known limitation: Hebrew mixed with English in PDFs" section with how the feature works and how to choose an engine.
- [ ] **Step 2:** Add the checklist below to `backlog.md` under "To verify".
- [ ] **Step 3:** Extend the `CLAUDE.md` DWG Exporter bullet with the print engine and its PDF24 prerequisite.
- [ ] **Step 4: Commit** — `docs(dwg-exporter): document the Hebrew-safe print engine`

```markdown
## To verify (in Revit, after deploy) — print engine, 2026-08-17

- [ ] The original Hebrew schedule sheet, printed — text order matches what Revit shows,
      compared side by side against the same sheet exported natively.
- [ ] A 900x1200 sheet, and a 900x5000 sheet — page dimensions **measured**, not eyeballed.
- [ ] A sheet whose size has no registered form — the error names the size in mm and points
      at "Register paper sizes", and the rest of the run continues.
- [ ] A batch of 20+ sheets spanning three paper sizes — open every PDF and check both the
      drawing matches its filename **and** the page size is right. Counting files proves
      nothing; both failure modes here are silent.
- [ ] The same batch with one sheet deliberately unregistered — every sheet after it is
      still correct on both counts.
- [ ] Hebrew sheet names produce correct Hebrew filenames.
- [ ] DWG + PDF in one run — basenames still pair exactly.
- [ ] A run with per-drawing transmittals — no .pdf enters any archive.
- [ ] The model is NOT modified by a print run — check the save indicator on a clean model.
- [ ] No modal dialog appears at any point during a batch.
- [ ] Engine left on "Fast (native)" — output byte-identical to before this batch.
```

---

## Deploy and verify

After Task 10, deploy and work through the checklist above.

```bash
powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoExit','-Command','cd D:\Coding\RVTuk; .\Deploy.ps1'"
```

Restart Revit before testing. Report any checklist item that fails against
`docs/tools/dwg-exporter/backlog.md` rather than marking the batch done.

## Still open (tuning only — neither blocks implementation)

- **Submit window size.** Task 6 starts at 10 outstanding jobs on the strength of one 44 MB
  sample. Measure the **spool folder on disk**, not print-queue depth — a job leaves the queue
  when the spooler finishes writing to the pipe while PDF24 goes on converting outside it,
  which is why an observed max depth of 1 meant nothing.
- **Where PDF24 stores per-instance auto-save settings.** Per-user under `HKCU\SOFTWARE\PDF24`,
  machine-wide under `HKLM\SOFTWARE\PDF24`. Task 9 needs the exact keys. Note the installer runs
  elevated while these may be per-user, so this may have to become a first-run step inside
  RVTuk rather than install-time setup.
