# Multi-model export, per-kind naming rules, split output folders — design

**Date:** 2026-08-03
**Status:** approved design
**Parent tool:** DWG Exporter (`DwgExporter`) — the three items from the 2026-08-03 backlog batch.

## Problem

Three requests, all landing on the same window:

1. **Sheets and views need different naming rules.** One rule serves both today, but a
   sheet parameter means nothing on a view and vice versa, so whichever kind the rule
   wasn't written for comes out wrong. Non-sheet views currently dodge this by ignoring
   the rule entirely and using the view name.
2. **Exporting from more than one open model in one run.** Today the tool only sees the
   active document.
3. **PDFs and DWGs into different folders.** Today both formats share one folder.

## Decisions

### A. Two naming rules, chosen per item

1. **Two dropdowns, always visible** — "Naming — sheets" and "Naming — views", each
   listing the model's `ExportPDFSettings`. Which applies is decided per item at plan
   time: `ViewSheet` → sheets rule, any other view → views rule.
2. **The views dropdown carries an extra `<View Name>` entry and defaults to it**, so
   behaviour is unchanged until the user picks a real setup.
3. **PDF export runs in two passes**, one per kind, each with its own `PDFExportOptions`
   (`Combine` forced off, as before). Revit still evaluates the rule itself, so `.pdf` and
   `.dwg` basenames keep pairing.
4. **`<View Name>` is a real rule, not a special case in the export loop**: a one-field
   `TableCellCombinedParameterData` over `BuiltInParameter.VIEW_NAME`. Its page settings
   (paper, quality, raster) are borrowed from the *sheets* setup — the user's chosen
   output quality shouldn't change just because an item is a view.
5. This closes the caveat the 2026-07-16 combined-export spec recorded: a non-sheet
   current view no longer gets a rule-based PDF name and a view-name DWG name.
6. `NamingRuleEvaluator.ResolveForSheet` generalises to `ResolveForView`, probing the view
   then Project Information — the same two sources, just a wider element type. Behaviour
   for `ViewSheet` is unchanged.
7. **Empty-result guard:** when a views rule resolves to nothing but blank fields, the
   file falls back to the view name rather than `FileNameComposer.Sanitize`'s generic
   `"Sheet"`, and the view is listed in the run summary. A rule written for sheets applied
   to views must not silently collapse every file onto one name.

### B. Split output folders

8. **One "Location" row plus a "Separate folder for PDFs" checkbox** that reveals a second
   row. Unticked — the default — means one folder, exactly as today.
9. The overwrite pre-flight checks `.dwg` in the DWG folder and `.pdf` in the PDF folder,
   independently.
10. The PDF folder is remembered per model, mirroring the existing DWG folder (see M below).

### C. Multi-model export

11. **A "Models" section** lists every open model — `Application.Documents` minus
    `IsLinked` and `IsFamilyDocument` documents — with a checkbox each. The active model is
    ticked and cannot be unticked.
12. **The section is disabled for the "Current window" range**, which only means something
    for the active model.
13. **Extra models resolve the active model's choices by name** in their own document: the
    view/sheet set, both naming setups, the DWG setup. Setups are per-document elements
    with no cross-document identity, so name matching is the only available link.
14. **A radio pair governs the miss case:** *Skip the model* (default) or *Copy the setup
    into the model*. Copying opens a transaction in that model and creates a real
    `ExportPDFSettings` / `ExportDWGSettings` with the same name and options, so it persists
    and appears in Revit's own dialogs afterwards.
15. **Two misses can never be copied and always skip**, with the reason reported:
    - **No matching view/sheet set** — a `ViewSheetSet` holds references to views that do
      not exist in another document; there is nothing to copy.
    - **A read-only model** (`Document.IsReadOnly`) — no transaction is possible.
16. **Naming rules are remapped when copied.** A rule field's `ParamId` is a per-document
    `ElementId`. Built-in parameters have negative ids and are the same everywhere; a
    shared or project parameter must be looked up by name in the target document.
    `CategoryId` is a `BuiltInCategory` (negative) and carries over unchanged.
17. **An unresolvable rule parameter skips that model** rather than creating a setup that
    would silently produce blank fields. Whether a parameter resolves can only be known
    against the live document, so this decision is made Revit-side at copy time, not by the
    Core resolver.
18. **Models do not get their own folders** — every model writes to the same destination
    (which is still split per *format* by B, if that checkbox is on). So the
    duplicate-filename pre-flight now spans models and names the offending model when it
    aborts.
19. **The model ticks are not persisted.** Which models are open differs from session to
    session; restoring a stale tick list would be noise.

> **Flagged, and accepted by the user:** decision 14 writes into and dirties a model the
> user did not open for editing. The read-only guard (15) and the skip-on-unresolvable-
> parameter rule (17) are what keep it from producing a wrong result silently; the dirtying
> itself is intended.

### D. Adjacent defect fixed

20. **`AppConfig`'s per-model folder lists are made null-tolerant.** net48's
    `DataContractJsonSerializer` deserializes through `GetUninitializedObject`, so property
    initializers never run and a list absent from the JSON stays `null` — meaning any
    config file written before `DwgExportFolders` existed makes `GetDwgExportFolder` throw
    on Revit 2024. The existing round-trip test doesn't catch it because it uses
    `System.Text.Json`, which does run initializers. `GetDwgExportFolder` /
    `SetDwgExportFolder` (and the new PDF pair) create the list on demand, and a test
    covers the null case directly. `IgnoredSubfolders` / `IgnoredFilePatterns` have the
    same latent trap but are outside this work — backlog item.

## Changes by layer

### Core (`src/RVTuk.Core/DwgExporter/`)

| File | Change |
|------|--------|
| `DwgExportTypes.cs` | `DwgExportRequest`: `PdfSetupName` → `SheetNamingSetupName`; adds `ViewNamingSetupName`, `PdfOutputFolder`, `SeparatePdfFolder`, `ExtraModelKeys`, `CopyMissingSetups`, and a derived `PdfFolder` (falls back to `OutputFolder`). `PlannedExportFile` gains `ModelTitle`. `DwgExportDefaults` gains `ViewNameNamingName = "<View Name>"`. |
| `ModelSetupResolver.cs` *(new)* | Pure. Given a `ModelSetupInventory` (key, title, read-only flag, and the three lists of names that model actually has) plus the request, returns a `ModelExportPlan`: `CanRun`, `SkipReason`, `PdfSetupsToCopy`, `CopyDwgSetup`. All the branching in decisions 13–15 lives here and is unit-tested without Revit. |
| `DwgExportPlanner.cs` | Unchanged signature; it already takes a flat file list and an injected existence predicate. The Revit side concatenates every model's files into it, so cross-model duplicates fall out for free. |
| `DwgExportSettingsStore.cs` *(new)* | `Read(AppConfig, modelKey)` / `Write(AppConfig, modelKey, settings)` over a `DwgExportSettings` DTO. Moves last-used persistence out of the view-model (which otherwise grows past 450 lines) into something testable without disk. |

Requirement rules the resolver applies:

- The sheets naming setup is required whenever it isn't the fallback pseudo-setup
  (`<Sheet Number> - <Sheet Name>`, offered only when the *active* model has no PDF setups
  at all — then nothing needs matching or copying).
- The views naming setup is required unless it is `<View Name>`.
- Both are required regardless of format, because the DWG filenames are evaluated from
  them too.
- The DWG setup is required only when `ExportDwg` is on and it isn't `<Revit defaults>`.

### UI (`src/RVTuk.UI/DwgExporter/`)

| File | Change |
|------|--------|
| `ViewModels/ModelSelectionItem.cs` *(new)* | `ViewModelBase` wrapper over the Core model DTO adding two-way `IsSelected`; the active model's is locked on. |
| `ViewModels/DwgExportViewModel.cs` | Splits `SelectedPdfSetup` into `SelectedSheetNaming` / `SelectedViewNaming`; adds `Models`, `CopyMissingSetups`, `SeparatePdfFolder`, `PdfOutputFolder`; `MultiModelEnabled => UseSheetSet`. Persistence delegates to `DwgExportSettingsStore`. |
| `Views/DwgExportWindow.xaml` | Naming section gets a second labelled combo; new Models section (scrollable checkbox list, max height ~110, plus the skip/copy radios); Location section gets the checkbox and the revealed second row. |

### Revit (`src/RVTuk.Revit/DwgExporter/`)

| File | Change |
|------|--------|
| `OpenModels.cs` *(new)* | Enumerates and filters `Application.Documents`; `KeyOf(doc)` = `PathName`, or `Title` while unsaved — the same key the config already uses. |
| `SetupTransfer.cs` *(new)* | `Remap(rule, source, target, out unresolved)` rebuilds a naming rule against another document (fresh `TableCellCombinedParameterData` objects; the source's are never mutated). `CopyPdfSetup` / `CopyDwgSetup` create the element inside their own `Transaction` on the target. |
| `NamingRuleEvaluator.cs` | `ResolveForSheet` → `ResolveForView(Document, View, rule)`; adds `ViewNameRule()` building the `VIEW_NAME` one-field rule. |
| `SheetDwgExporter.cs` | Takes a `Document` rather than the `UIDocument` so any open model can be planned and exported (the "current window" path keeps a `UIDocument` overload). Filenames pick the sheets or views rule per item. Export resolves DWG options plus up to two `PDFExportOptions`, and writes each format to its own folder. |
| `DwgExportRunner.cs` *(new)* | The bodies of the three delegates, including multi-model orchestration: resolve each ticked model, perform any approved copies, plan every model's files, then export model by model. The command file keeps only WPF bootstrap, inventory gathering, view-model construction and the native-dialog hand-off. |
| `Commands/DwgExportCommand.cs` | Reduced to that wiring. |

### Config (`src/RVTuk.Core/Shared/Config/AppConfig.cs`)

New keys, all chosen so a **missing** key means today's behaviour (the net48
uninitialized-object rule from the 2026-07-16 spec):

| Key | Absent means |
|-----|--------------|
| `DwgExportViewNamingSetupName` (string) | `""` → `<View Name>` |
| `DwgExportSeparatePdfFolder` (bool) | `false` → one folder |
| `DwgExportCopyMissingSetups` (bool) | `false` → skip the model |
| `DwgExportPdfFolder` (string) + `DwgExportPdfFolders` (list) | empty → the DWG folder |

`DwgExportPdfSetupName` keeps its name and now means the *sheets* rule — old configs
restore their selection unchanged.

## Error handling

- Multi-model is unreachable from the "Current window" range (section disabled).
- A skipped model is reported in the run summary with its reason: missing set, read-only,
  missing setup with copying off, or an unresolvable rule parameter.
- Copy failures (transaction refused, name invalid) demote that model to skipped; other
  models still run.
- Duplicate filenames across the whole run abort before anything is written, naming the
  model each clashing file came from.
- Per-item, per-format failures stay independent, as today.
- With only the active model ticked, one folder, and `<View Name>` for views, the run must
  be byte-identical to today's.

## Testing

**Core (`tests/RVTuk.Core.Tests/DwgExporter/`)**

- `ModelSetupResolverTests` — all names matched → runs, copies nothing; missing PDF setup
  with copying off → skipped, reason names the setup; same with copying on → runs, setup
  queued for copy; same on a read-only model → skipped; missing sheet set → skipped
  whatever the copy policy; DWG setup not required when `ExportDwg` is off; the fallback
  pseudo-setup and `<Revit defaults>` never require a copy.
- `DwgExportPlannerTests` — extended: a name produced by two different models is reported
  once as a duplicate; the existence predicate is consulted for both folders.
- `DwgExportSettingsStoreTests` — a blank config yields `<View Name>`, copying off,
  separate-folder off; round trip; per-model PDF folder falls back to the global one, then
  to the DWG folder.
- `AppConfigTests` — a config whose folder lists are `null` (the net48 shape) neither
  throws on read nor on write.

**In Revit (manual, after deploy)** — a checklist item in the plan, covering: two naming
rules over a set mixing sheets and views; `.dwg`/`.pdf` basenames still pairing per kind;
split folders; a second model matched by name; a second model needing a copied setup, with
the setup verified present in that model's native dialog afterwards; a read-only second
model reported rather than attempted; and a cross-model duplicate aborting.

## Out of scope

- Per-model output folders or subfolders (one shared folder was chosen).
- Per-model overrides of set/setup choices — extra models follow the active model's picks.
- Copying view/sheet sets between models.
- The `IgnoredSubfolders` / `IgnoredFilePatterns` null-list trap (same root cause as D,
  different feature — backlog).
