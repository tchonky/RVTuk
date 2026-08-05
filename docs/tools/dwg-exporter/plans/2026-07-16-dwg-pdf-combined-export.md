# DWG + PDF Combined Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add DWG/PDF format checkboxes to the DWG Export dialog so one command exports either or both file sets with matching filenames.

**Architecture:** No new components — the existing per-sheet export loop gains an optional per-sheet PDF `Document.Export` call driven by the already-selected PDF setup (`Combine` forced off so Revit's own naming-rule evaluation yields one PDF per sheet, names identical to the native PDF export).

**Tech Stack:** Same as the tool: C# net48/net8.0-windows, WPF, Revit API 2024/2025 (`PDFExportOptions`, `TableCellCombinedParameterData`), xunit.

**Spec:** `docs/tools/dwg-exporter/specs/2026-07-16-dwg-pdf-combined-export-design.md`

## Global Constraints

- Same as the parent tool's plan (`2026-07-15-dwg-exporter.md`): namespaces = folders; Core Revit/WPF-free; UI Revit-free; KKarea untouched; `ElementId.Value`; all three configs must build.
- **Serializer constraint:** net48 `DataContractJsonSerializer` deserializes into uninitialized objects (property initializers don't run) — persisted booleans whose default is `true` MUST be stored inverted so a missing key means the default on both serializers.

## File Structure

| File | Change |
|------|--------|
| Modify `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs` | `ExportDwg`/`ExportPdf` on the request. |
| Modify `src/RVTuk.Core/Shared/Config/AppConfig.cs` | `DwgExportDwgOff` / `DwgExportPdfOn` persisted bools. |
| Modify `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs` | Format properties, CanExport, config load/save, request flags. |
| Modify `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml` | Formats row, title, DWG-setup combo enablement. |
| Modify `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs` | `GetPdfOptions` (Combine off + fallback rule), dual-format loop, format-aware existence check. |
| Modify `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs` | Format-aware existence closure. |
| Modify tests `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs` | Old-config semantics + round-trip. |
| Modify tests `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs` | Request flag defaults. |
| Modify docs (`docs/tools/dwg-exporter/README.md`, `backlog.md`) | Final task. |

---

### Task 1: Core — request flags + persisted format bools (TDD)

**Files:**
- Modify: `src/RVTuk.Core/DwgExporter/DwgExportTypes.cs` (inside `DwgExportRequest`)
- Modify: `src/RVTuk.Core/Shared/Config/AppConfig.cs` (after `DwgExportUseCurrentWindow`)
- Test: `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs`, `tests/RVTuk.Core.Tests/DwgExporter/DwgExportPlannerTests.cs`

**Interfaces:**
- Produces: `DwgExportRequest.ExportDwg` (bool, default **true**), `DwgExportRequest.ExportPdf` (bool, default false); `AppConfig.DwgExportDwgOff` / `AppConfig.DwgExportPdfOn` (both bool, default false = "DWG on, PDF off").

- [ ] **Step 1: Failing tests**

Append inside `AppConfigTests`:

```csharp
    [Fact]
    public void DwgExportFormats_OldConfigWithoutKeys_MeansDwgOnPdfOff()
    {
        // Uninitialized-object deserialization (net48 DCJS) must land on the same
        // defaults, which is why the stored booleans are inverted/additive.
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{}")!;

        Assert.False(loaded.DwgExportDwgOff);
        Assert.False(loaded.DwgExportPdfOn);
    }

    [Fact]
    public void DwgExportFormats_RoundTrip()
    {
        var config = new AppConfig { DwgExportDwgOff = true, DwgExportPdfOn = true };
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;

        Assert.True(loaded.DwgExportDwgOff);
        Assert.True(loaded.DwgExportPdfOn);
    }
```

Append inside `DwgExportPlannerTests`:

```csharp
    [Fact]
    public void DwgExportRequest_Defaults_DwgOnPdfOff()
    {
        var request = new DwgExportRequest();

        Assert.True(request.ExportDwg);
        Assert.False(request.ExportPdf);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~DwgExportFormats|FullyQualifiedName~DwgExportRequest_Defaults"`
Expected: build FAILURE (members missing).

- [ ] **Step 3: Implement**

In `DwgExportTypes.cs`, add to `DwgExportRequest` (after `OutputFolder`):

```csharp
        /// <summary>Formats to produce. At least one must be true (the dialog enforces it).</summary>
        public bool ExportDwg { get; set; } = true;
        public bool ExportPdf { get; set; }
```

In `AppConfig.cs`, add after `DwgExportUseCurrentWindow`:

```csharp
        /// <summary>Last-used format checkboxes, stored inverted/additive (false = the
        /// out-of-box state "DWG on, PDF off"): net48's DataContractJsonSerializer skips
        /// property initializers, so a true-default property would flip to false when
        /// loading a config file written before this feature existed.</summary>
        public bool DwgExportDwgOff { get; set; }
        public bool DwgExportPdfOn { get; set; }
```

- [ ] **Step 4: Run full Core suite** — `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` — Expected: PASS.

- [ ] **Step 5: Commit** — `git add src/RVTuk.Core tests/RVTuk.Core.Tests && git commit -m "feat(dwg-exporter): request format flags + persisted format checkboxes"`

---

### Task 2: UI — format checkboxes

**Files:**
- Modify: `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`
- Modify: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml`

**Interfaces:**
- Consumes: Task 1's members.
- Produces: VM properties `ExportDwgFormat` / `ExportPdfFormat` (bool, two-way bound).

- [ ] **Step 1: View model**

Add after the `UseSheetSet` property:

```csharp
        private bool _exportDwgFormat;
        public bool ExportDwgFormat
        {
            get => _exportDwgFormat;
            set { SetProperty(ref _exportDwgFormat, value); OnPropertyChanged(nameof(CanExport)); }
        }

        private bool _exportPdfFormat;
        public bool ExportPdfFormat
        {
            get => _exportPdfFormat;
            set { SetProperty(ref _exportPdfFormat, value); OnPropertyChanged(nameof(CanExport)); }
        }
```

In the constructor's config-restore block add:

```csharp
            _exportDwgFormat = !config.DwgExportDwgOff;
            _exportPdfFormat = config.DwgExportPdfOn;
```

Extend `CanExport` with `&& (ExportDwgFormat || ExportPdfFormat)`; extend `BuildRequest` with `ExportDwg = ExportDwgFormat, ExportPdf = ExportPdfFormat,`; extend `SaveLastUsed` with:

```csharp
                config.DwgExportDwgOff = !ExportDwgFormat;
                config.DwgExportPdfOn = ExportPdfFormat;
```

- [ ] **Step 2: XAML**

Title → `Title="RVTuk — Sheet Export (DWG / PDF)"`. Insert a Formats section between File Naming and DWG Export Setup:

```xml
        <!-- ── Formats ──────────────────────────────────────────────────── -->
        <Border Style="{StaticResource Section}">
            <StackPanel>
                <TextBlock Text="Formats" Style="{StaticResource SectionHeader}"/>
                <StackPanel Orientation="Horizontal">
                    <CheckBox Content="DWG" IsChecked="{Binding ExportDwgFormat}"/>
                    <CheckBox Content="PDF (one file per sheet)" IsChecked="{Binding ExportPdfFormat}"
                              Margin="18,0,0,0"/>
                </StackPanel>
            </StackPanel>
        </Border>
```

DWG setup combo gets `IsEnabled="{Binding ExportDwgFormat}"`.

- [ ] **Step 3: Build all three UI configs** — Release2024/2025/2023 — Expected: 0 errors.

- [ ] **Step 4: Commit** — `git add src/RVTuk.UI && git commit -m "feat(dwg-exporter): DWG/PDF format checkboxes in the export window"`

---

### Task 3: Revit — dual-format export loop

**Files:**
- Modify: `src/RVTuk.Revit/DwgExporter/SheetDwgExporter.cs`
- Modify: `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs`

**Interfaces:**
- Consumes: Tasks 1–2. Revit API: `Document.Export(string, IList<ElementId>, PDFExportOptions)` (bool return), `PDFExportOptions.Combine`, `PDFExportOptions.SetNamingRule(IList<TableCellCombinedParameterData>)`, `TableCellCombinedParameterData.Create()`.
- Produces: `SheetDwgExporter.OutputFileExists(string folder, string fileName, DwgExportRequest request)` replaces `DwgFileExists`.

- [ ] **Step 1: `SheetDwgExporter.Export` — resolve options per format, export per sheet per format**

Replace the `Export` method body: resolve `DWGExportOptions? dwgOptions` (only when `request.ExportDwg`) and `PDFExportOptions? pdfOptions` (only when `request.ExportPdf`), format tag `" (DWG)"` / `" (PDF)"` / `" (DWG+PDF)"` for progress, then per sheet:

```csharp
        public static DwgExportResult Export(
            Document doc,
            DwgExportRequest request,
            List<(ElementId Id, PlannedExportFile File)> files,
            Action<int, int, string> progress)
        {
            var dwgOptions = request.ExportDwg ? GetDwgOptions(doc, request.DwgSetupName) : null;
            var pdfOptions = request.ExportPdf ? GetPdfOptions(doc, request.PdfSetupName) : null;
            var tag = request.ExportDwg && request.ExportPdf ? " (DWG+PDF)"
                : request.ExportPdf ? " (PDF)" : " (DWG)";
            var result = new DwgExportResult();

            for (int i = 0; i < files.Count; i++)
            {
                var (id, file) = files[i];
                if (dwgOptions != null)
                {
                    try
                    {
                        var ok = doc.Export(request.OutputFolder, file.FileName,
                            new List<ElementId> { id }, dwgOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.ViewLabel + " (DWG): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.ViewLabel + " (DWG): " + ex.Message);
                    }
                }
                if (pdfOptions != null)
                {
                    try
                    {
                        // Revit evaluates the setup's naming rule itself, so the .pdf
                        // basename matches the native PDF export byte for byte.
                        var ok = doc.Export(request.OutputFolder,
                            new List<ElementId> { id }, pdfOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.ViewLabel + " (PDF): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.ViewLabel + " (PDF): " + ex.Message);
                    }
                }
                progress(i + 1, files.Count, file.ViewLabel + tag);
            }

            return result;
        }
```

- [ ] **Step 2: `GetPdfOptions` + format-aware existence check**

Add to `SheetDwgExporter` (next to `GetDwgOptions`), and replace `DwgFileExists`:

```csharp
        /// <summary>Options for the per-sheet PDF calls: the chosen setup's own options with
        /// Combine forced off (one PDF per sheet, names mirror the DWGs 1:1). The fallback
        /// pseudo-setup gets an explicit "Sheet Number - Sheet Name" rule so the pairing
        /// holds in documents without saved PDF setups.</summary>
        private static PDFExportOptions GetPdfOptions(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName)
            {
                var number = TableCellCombinedParameterData.Create();
                number.ParamId = new ElementId(BuiltInParameter.SHEET_NUMBER);
                number.Separator = " - ";
                var name = TableCellCombinedParameterData.Create();
                name.ParamId = new ElementId(BuiltInParameter.SHEET_NAME);

                var fallback = new PDFExportOptions { Combine = false };
                fallback.SetNamingRule(new List<TableCellCombinedParameterData> { number, name });
                return fallback;
            }

            var settings = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportPDFSettings))
                .Cast<ExportPDFSettings>()
                .FirstOrDefault(s => s.Name == pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            var options = settings.GetOptions();
            options.Combine = false;
            return options;
        }

        public static bool OutputFileExists(string folder, string fileName, DwgExportRequest request)
            => (request.ExportDwg && File.Exists(Path.Combine(folder, fileName + ".dwg")))
            || (request.ExportPdf && File.Exists(Path.Combine(folder, fileName + ".pdf")));
```

- [ ] **Step 3: Command closure**

In `DwgExportCommand`, the `planExport` closure's existence lambda becomes:

```csharp
                        name => SheetDwgExporter.OutputFileExists(request.OutputFolder, name, request));
```

- [ ] **Step 4: Build all three solution configs** — Expected: 0 errors.

- [ ] **Step 5: Commit** — `git add src/RVTuk.Revit && git commit -m "feat(dwg-exporter): per-sheet PDF export alongside DWG"`

---

### Task 4: Docs + verification

- [ ] **Step 1: Full test suite** — Expected: PASS, no regressions.
- [ ] **Step 2: Docs** — README "What it is" mentions optional PDF output; backlog: tick "Export DWG + PDF together", note the non-sheet-view naming caveat.
- [x] **Step 3: In-Revit checklist (user):** deploy; export a small set with both boxes checked; `.dwg`/`.pdf` basenames must match exactly; PDF-only run works; old config opens with DWG checked/PDF unchecked.
- [ ] **Step 4: Commit** — `git add docs/tools/dwg-exporter && git commit -m "docs(dwg-exporter): DWG+PDF combined export shipped"`
