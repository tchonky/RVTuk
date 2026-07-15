# Repo Reorganization — Tool-First Structure & Naming

**Date:** 2026-07-15
**Status:** Approved
**Area:** Toolkit-wide (docs + all four projects)
**Branch:** master (mechanical reorg, phased commits)

## Problem / motivation

The repo grew feature-by-feature and the organization no longer matches how we think
about the product. Three concrete problems:

1. **Two axes are tangled.** Code is organized by *layer* (`Core` → `UI` → `Revit`),
   so each tool's code is scattered across three projects with no consistent tool
   boundary inside any of them.
2. **Naming chaos.** The area tool goes by four names — `AreaSubmission` (namespaces),
   `autoarea` (docs folder), `AreaCalc` (ribbon command), "Rishui Zamin" (prose).
   The Family Browser goes by `LibraryBrowser` (folder), `FamilyBrowser` (views),
   `BrowseLibrary` (command), "Family Explorer" (old docs). And the UI project lives at
   `src\LibraryBrowser\RVTuk.UI` — nested under one tool's name while containing *all*
   tools' UI.
3. **Docs are organized by artifact type + date** (`docs/superpowers/specs/…`,
   `docs/superpowers/plans/…`), so finding everything about one tool means grepping.
   `docs/BACKLOG.md` mixes all tools' bugs together, and its "next session" header is
   stale (references the long-merged `family-explorer` branch).

## Terminology (canonical — used in all docs and code from now on)

| Term | Meaning |
|------|---------|
| **Toolkit** | RVTuk itself: one add-in, one ribbon, one install. |
| **Tool** | A user-facing feature with its own button/pane and UI: **Family Browser**, **Rishui Zamin**, **Auto Dimensions**, **Neo Properties**. |
| **Layer** | A C# project: `RVTuk.Core` (logic) → `RVTuk.UI` (WPF) → `RVTuk.Revit` (Revit host). A tool is a vertical slice across the layers. |
| **Host** | A project that loads into Revit: `RVTuk.Revit` (2024/25) and `KKarea.Revit` (2023, ships only the Rishui Zamin tool). |

**Canonical tool names in code:** `FamilyBrowser`, `RishuiZamin`, `AutoDimensions`,
`NeoProperties`. "Area Calc" survives only as user-facing display text (ribbon label,
window title) — display strings do not change in this reorg.

## Goals

- Everything about one tool findable in one docs folder and one code folder per layer.
- One canonical name per tool, used in folders, namespaces, and class names.
- Zero behavioural change: no assembly renames, no ClientId/config/DB changes; a
  deployed installation cannot tell the difference.

## Non-goals

- No feature work, no bug fixes, no dead-code deletion (candidates get backlog entries).
- No renaming of user-facing display strings.
- No changes to historical specs/plans' *contents* — they keep their old paths/names
  inside as a record of what was true when written.
- The deep identity (assembly names, `RVTuk` brand, ClientId GUIDs) stays untouched
  per VISION.md.

## Design — docs tree

```
docs/
  BACKLOG.md                      — toolkit-wide items only + links to per-tool backlogs
  TEMPLATE-feature-design.md
  toolkit/                        — cross-tool docs
    specs/                        — (tool-visibility spec, this spec)
  tools/
    family-browser/
      README.md                   — what it is, status, code entry points, doc links
      design.md                   — living design (was specs/family-browser-design.md)
      backlog.md                  — its bugs / improvements / ideas / done history
      specs/  plans/              — dated historical specs & plans
    rishui-zamin/
      README.md  backlog.md  notes.md  rules.md  specs/  plans/
    auto-dimensions/
      README.md  backlog.md  specs/  plans/
    neo-properties/
      README.md  backlog.md  specs/  plans/
  future/                         — specs for tools not yet built
  archive/                        — retired/superseded docs (unchanged + additions below)
```

New-spec convention (overrides the superpowers default, noted in CLAUDE.md): specs and
plans for a tool go to `docs/tools/<tool>/specs|plans/`; toolkit-wide ones go to
`docs/toolkit/specs|plans/`. `docs/superpowers/` disappears.

### Full docs move map (all via `git mv`; filenames keep their dates unless noted)

| From | To |
|------|-----|
| `docs/autoarea/rishui-zamin-notes.md` | `docs/tools/rishui-zamin/notes.md` |
| `docs/autoarea/rishui-zamin-rules.md` | `docs/tools/rishui-zamin/rules.md` |
| `superpowers/specs/family-browser-design.md` | `docs/tools/family-browser/design.md` |
| `superpowers/specs/2026-07-07-family-browser-redesign-design.md` | `docs/tools/family-browser/specs/` |
| `superpowers/specs/2026-07-01-scan-checkboxes-design.md` | `docs/tools/family-browser/specs/` |
| `superpowers/specs/2026-07-09-instruction-image-formatting-design.md` | `docs/tools/family-browser/specs/` |
| `superpowers/plans/2026-06-15-family-browser.md` | `docs/tools/family-browser/plans/` |
| `superpowers/plans/2026-06-22-family-explorer.md` | `docs/tools/family-browser/plans/` |
| `superpowers/plans/2026-07-01-scan-checkboxes.md` | `docs/tools/family-browser/plans/` |
| `superpowers/plans/2026-07-09-instruction-image-formatting.md` | `docs/tools/family-browser/plans/` |
| `superpowers/specs/2026-07-01-rishui-zamin-area-submission-design.md` | `docs/tools/rishui-zamin/specs/` |
| `superpowers/specs/2026-07-05-area-submission-ui-redesign-design.md` | `docs/tools/rishui-zamin/specs/` |
| `superpowers/specs/2026-07-06-kkarea-revit2023-design.md` | `docs/tools/rishui-zamin/specs/` |
| `superpowers/plans/2026-07-01-rishui-zamin-area-submission.md` | `docs/tools/rishui-zamin/plans/` |
| `superpowers/plans/2026-07-05-area-submission-ui-redesign.md` | `docs/tools/rishui-zamin/plans/` |
| `superpowers/plans/2026-07-06-kkarea-revit2023.md` | `docs/tools/rishui-zamin/plans/` |
| `superpowers/specs/2026-07-04-auto-dimensions-design.md` | `docs/tools/auto-dimensions/specs/` |
| `superpowers/plans/2026-07-04-auto-dimensions.md` | `docs/tools/auto-dimensions/plans/` |
| `superpowers/specs/2026-07-04-neo-properties-design.md` | `docs/tools/neo-properties/specs/` |
| `superpowers/plans/2026-07-04-neo-properties.md` | `docs/tools/neo-properties/plans/` |
| `superpowers/specs/2026-07-03-tool-visibility-design.md` | `docs/toolkit/specs/` |
| `superpowers/specs/2026-07-01-instructions-design.md` | `docs/future/` |
| `superpowers/specs/2026-07-01-room-renumbering-design.md` | `docs/future/` |
| `superpowers/specs/2026-06-23-project-comparator-design.md` | `docs/archive/` (tool stripped from repo) |
| `superpowers/plans/2026-06-23-project-comparator.md` | `docs/archive/` (tool stripped from repo) |
| `superpowers/specs/2026-07-01-auto-dimensioning-design.md` | `docs/archive/` (draft skeleton superseded by the 07-04 spec) |
| `superpowers/specs/TEMPLATE-feature-design.md` | `docs/TEMPLATE-feature-design.md` |

KKarea's spec/plan live under `rishui-zamin/` because KKarea exists solely to host that
tool for Revit 2023.

### New docs (written fresh)

- **Per-tool `README.md`** (×4): one page — what the tool does, user-facing name vs code
  name, shipped/hidden status, the code folders in each layer, links to design/specs/
  plans/backlog.
- **Per-tool `backlog.md`** (×4): bugs / improvements / ideas / done, seeded by splitting
  today's `BACKLOG.md` (e.g. the OLE-thumbnail bug, deep-scan re-entrancy, tags
  follow-ups → family-browser; empty-but-ready sections for auto-dimensions and
  neo-properties). Family Browser's backlog also gains: "SettingsCommand is no longer on
  the ribbon — decide keep-for-dev or delete."
- **Rewritten `docs/BACKLOG.md`**: toolkit-wide items only (installer, deploy, ribbon,
  3Dconnexion crash note) + links to the four per-tool backlogs; the stale
  "Status / next session" section replaced with current reality (branch `master`,
  v1 = Family Browser + Rishui Zamin shipped, others behind `RegisterUnreleasedTools`).

## Design — code tree

The four projects and their dependency rules are unchanged. Inside each project: one
folder per tool + `Shared/` for genuinely cross-tool code. **Placement rule: a file
lives in a tool folder iff only that tool uses it; `Shared/` requires two consumers
(or being toolkit infrastructure like config). Namespace = root namespace + folder
path, exactly, everywhere.**

```
src/
  RVTuk.Core/
    FamilyBrowser/
      Config/       LibraryFolderValidator
      Database/     BrowserRepository, IndexRepository, SqliteNative, DbConvert
      Extraction/   FamilyIndexer, ThumbnailExtractor, ThumbnailWriter
      Models/       FamilyBrowserItem, FamilyModel, ParameterModel, IndexSummary,
                    ExtractionWorkItem, ProjectFamilyInfo, VersionStatus
      Util/         FamilyVersionCheck, IgnoredFileMatcher, ImageCropGeometry
    RishuiZamin/    (was AreaSubmission/) AreaRecord, RishuiZaminConfig,
                    RishuiZaminExporter, AreaValidator, DatWriter, DxfWriter,
                    UsageCatalog, UsageCodeParser, DxfTemplates/
    AutoDimensions/ (unchanged contents)
    NeoProperties/  (unchanged contents)
    Shared/
      Config/       AppConfig, ConfigManager   (holds FB + RZ settings → shared)
      Util/         PathUtil
  RVTuk.UI/                                    ← moved from src\LibraryBrowser\RVTuk.UI
    FamilyBrowser/
      Views/        FamilyBrowserWindow, InstructionsEditorWindow, ImageCropWindow,
                    IndexProgressWindow, SettingsWindow
      ViewModels/   FamilyBrowserViewModel, FamilyBrowserItemViewModel,
                    CategoryFilterOption, ConfigViewModel, SettingsViewModel,
                    IndexProgressViewModel, InstructionsEditorViewModel
    RishuiZamin/
      Views/        RishuiZaminWindow (was AreaSubmissionWindow)
      ViewModels/   RishuiZaminViewModel (was AreaSubmissionViewModel),
                    AreaRowViewModel, AreaLevelGroupViewModel
    NeoProperties/
      Views/        NeoPropertiesView
      ViewModels/   NeoPropertiesViewModel
    Shared/         (Auto Dimensions has no UI — no folder)
      ViewModels/   ViewModelBase, RelayCommand, RelayCommandT
      Converters/   CountToVisibilityConverter
      Controls/     RichTextBoxHelper
      Helpers/      MarkdownConverter
      Themes/       DarkTheme.xaml
  RVTuk.Revit/
    Application.cs  (root — namespace RVTuk.Revit; .addin entry class, untouched)
    FamilyBrowser/
      Commands/        BrowseLibraryCommand, IndexLibraryCommand, SettingsCommand
      ExternalEvents/  IndexingExternalEventHandler, GetProjectFamiliesEventHandler,
                       LoadFamilyEventHandler, OpenFamilyEditorEventHandler
      Extraction/      FamilyMetadataExtractor
    RishuiZamin/
      Commands/        RishuiZaminCommand (was AreaCalcCommand),
                       SetupRishuiZaminParamsCommand
      ExternalEvents/  AreaExtractEventHandler, SelectAreaEventHandler,
                       SetupUsageKeysEventHandler
      AreaExtractor, UsageKeyScheduleBuilder
      Resources/       RZ_AreaParams.txt
    AutoDimensions/  (unchanged contents — already holds its command)
    NeoProperties/
      Commands/        NeoPropertiesCommand (moves in from Commands/)
      NeoPropertiesPaneProvider, NeoPropertiesSelectionHandler
  KKarea.Revit/     (structure unchanged; csproj link paths + usings updated;
                     its AreaCalcCommand also renamed RishuiZaminCommand)
tests/
  RVTuk.Core.Tests/
    FamilyBrowser/   (root-level FB tests + Util/ImageCropGeometryTests move in)
    RishuiZamin/     (was AreaSubmission/, incl. Fixtures/)
    AutoDimensions/  NeoProperties/  (unchanged)
    Shared/          AppConfigTests
```

### Naming rules

- **Namespaces follow folders exactly**: `RVTuk.Core.Models` →
  `RVTuk.Core.FamilyBrowser.Models`, `RVTuk.Core.AreaSubmission` →
  `RVTuk.Core.RishuiZamin`, `RVTuk.UI.Views` → `RVTuk.UI.FamilyBrowser.Views`,
  `RVTuk.Core.Config` → `RVTuk.Core.Shared.Config`, etc. All `using` directives,
  XAML `x:Class`, and `clr-namespace` references updated. Test namespaces follow
  their folders the same way.
- **Classes carrying a retired tool name are renamed**: `AreaSubmissionConfig` →
  `RishuiZaminConfig`, `AreaSubmissionExporter` → `RishuiZaminExporter`,
  `AreaSubmissionWindow` → `RishuiZaminWindow`, `AreaSubmissionViewModel` →
  `RishuiZaminViewModel`, `AreaCalcCommand` → `RishuiZaminCommand` (both hosts).
  Classes named after **Revit Areas** (the domain object) correctly keep "Area":
  `AreaRecord`, `AreaValidator`, `AreaExtractor`, `AreaRowViewModel`,
  `AreaLevelGroupViewModel`, `AreaExtractEventHandler`, `SelectAreaEventHandler`.
  The static `Application.AreaCalcWindow` property is renamed to
  `RishuiZaminWindow`; the `MarkerForm` enum keeps its name (domain term, carries no
  tool name). Ribbon button internal ids (`"AreaCalc"`) stay — not user-visible, and
  ids are safest left stable.
- All file moves via `git mv` so history follows renames.

### What deliberately does NOT change

Assembly names (`RVTuk.Core/UI/Revit.dll`, `KKarea.Revit.dll`), `.addin` entry classes
(`RVTuk.Revit.Application`, `KKarea.Revit.Application`), both ClientId GUIDs, the SQLite
schema and `.DB` folder, config-file locations, deployed file names (incl. flat
`RZ_AreaParams.txt`), and all user-facing display text.

### Known tricky spots (verified against current source)

1. **Embedded DXF templates** — `RVTuk.Core.csproj` pins explicit `<LogicalName>`s
   (`RVTuk.Core.AreaSubmission.DxfTemplates.{Preamble,Postamble}.dxf`) and
   `DxfWriter.cs:630` builds the same string. Rename both together to
   `RVTuk.Core.RishuiZamin.DxfTemplates.…`; the `DxfWriterTests` exercise the load.
2. **`RZ_AreaParams.txt`** — a `None` item with `Link`/`TargetPath` flattening it into
   the output root; only the `Include` path in `RVTuk.Revit.csproj` changes. Deployed
   name stays `RZ_AreaParams.txt` (`SetupRishuiZaminParamsCommand` loads it by that
   name from the add-in dir).
3. **KKarea linked sources** — five `<Compile Include="..\RVTuk.Revit\…">` links
   repointed to the new `RishuiZamin/` paths; KKarea `using`s updated to
   `RVTuk.Revit.RishuiZamin[.ExternalEvents]`.
4. **Project move** — `src\LibraryBrowser\RVTuk.UI` → `src\RVTuk.UI` touches exactly:
   `RVTuk.sln` (one project path), `RVTuk.Revit.csproj` + `KKarea.Revit.csproj`
   (one `ProjectReference` each). `Deploy.ps1`/`Build-Installer.ps1` reference only the
   host projects' `bin` paths — no script changes needed for the move.
5. **WPF plumbing** — the `DarkTheme.xaml` pack URI
   (`/RVTuk.UI;component/Themes/DarkTheme.xaml`) appears in 5 XAML files and becomes
   `…component/Shared/Themes/DarkTheme.xaml`; `x:Class` and `clr-namespace` updates ride
   along with the namespace pass.
6. **`AppConfig` references `AreaSubmission.MarkerForm`** — follows the namespace/class
   rename mechanically.

## Root-doc updates

- **CLAUDE.md** — add the Terminology table; update every path
  (`src\LibraryBrowser\RVTuk.UI` → `src\RVTuk.UI`, spec links to new locations);
  rename AreaSubmission references; document the new docs convention
  (per-tool `specs/`/`plans/`, toolkit-wide in `docs/toolkit/`); update the
  architecture diagram and feature bullets to lead with canonical tool names.
- **VISION.md** — same terminology; pillar names updated ("Area Calc (Rishui Zamin)" →
  "Rishui Zamin (ribbon: Area Calc)"); Related-docs table points at `docs/tools/…`.
- **README.md** — project-tree diagram and paths refreshed.
- Historical specs/plans keep their old-path contents (records, not live docs).

## Implementation order (one commit per phase, each phase builds green)

1. **Docs reorg** — all `git mv`s, new READMEs/backlogs, BACKLOG rewrite. No build risk.
2. **UI project move** — `src\RVTuk.UI` + sln/csproj path updates. Build all 3 configs.
3. **Core** — per-tool folders, namespace pass, embedded-resource rename. Build + tests.
4. **UI internals** — per-tool folders, namespaces, XAML plumbing. Build.
5. **Revit hosts** — per-tool folders, namespaces, KKarea links. Build all 3 configs.
6. **Class renames** — `AreaSubmission*`/`AreaCalcCommand` → `RishuiZamin*`. Build + tests.
7. **Root docs** — CLAUDE.md / VISION.md / README.md; final sweep.

## Verification

- `dotnet build RVTuk.sln -c Release2023|Release2024|Release2025` — all green.
- `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` — all green
  (DxfWriter tests prove the embedded-resource rename).
- Grep sweep: zero hits for `AreaSubmission`, `LibraryBrowser`, `autoarea`,
  `docs/superpowers` outside `docs/archive/` and per-tool `specs|plans` (historical).
- `Deploy.ps1` staging paths resolve (dry-check the `$srcDir` computation per year).
- Final in-Revit smoke test after an elevated deploy: both ribbon buttons open, a family
  loads, an Area Calc export writes `.dxf`+`.dat` (user-run).
