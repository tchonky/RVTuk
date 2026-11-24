# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RVTuk is a Revit add-in toolkit for Knafo Klimor Architects LTD. It supports Revit 2024, 2025, 2026 and 2027 simultaneously via separate build configurations (2027 since November 2026: checked in Revit 2027 on 2026-11-15 that the add-in loads and every ribbon button shows; the tools themselves have not yet been checked one by one there; 2026 since 2026-11-16: seen that day to start in Revit 2026 and 2027 with its three panes (the Topo Tools pane has since become the Topo Edit ribbon session, not yet checked in 2026/2027) and the Family Browser in the dark theme; the tools themselves have not yet been checked one by one in 2026 either). Revit 2023 is served by **KKarea**, a separate minimal add-in in this repo (`src\KKarea.Revit`) that hosts only the Rishui Zamin (Area Calc) tool — RVTuk itself was dropped from 2023.

> **Product vision, audience, and roadmap live in [`VISION.md`](VISION.md).** This file is the technical reference (build, architecture, threading, deploy).

## Terminology

| Term | Meaning |
|------|---------|
| **Toolkit** | RVTuk itself: one add-in, one ribbon, one install. |
| **Tool** | A user-facing feature with its own button/pane and UI: **Family Browser**, **Rishui Zamin** (ribbon label "Area Calc"), **Auto Dimensions**, **Topo Tools**, **Room Floors** (ribbon label "Room Floor"), **Round Positions**, **Neo Properties**. |
| **Layer** | A C# project: `RVTuk.Core` (logic) → `RVTuk.UI` (WPF) → `RVTuk.Revit` (Revit host). A tool is a vertical slice across the layers. |
| **Host** | A project that loads into Revit: `RVTuk.Revit` (2024–2027) and `KKarea.Revit` (2023, ships only the Rishui Zamin tool). |

Canonical tool names in code and folders: `FamilyBrowser`, `RishuiZamin`,
`AutoDimensions`, `TopoTools`, `RoomFloors`, `RoundPositions`, `NeoProperties`. Inside every project (and each test project) the
structure is one folder per tool plus `Shared/`, and **namespace = root namespace +
folder path, exactly**. A file lives in a tool folder iff only that tool uses it.

**v1 launch surface:** the **Family Browser**, **Rishui Zamin** (ribbon "Area Calc"), **DWG Exporter** (ribbon "DWG Export"), and **Auto Dimensions** are registered on the single "RVTuk" ribbon panel. **Topo Tools** joins them on that panel, gated by `RegisterTopoTools` (on). It was intended for Revit's **Massing & Site** tab; that is impossible and was verified in Revit 2024 — the API places a custom panel only on **Add-Ins**, on **Analyze**, or on a tab the add-in creates itself, and the string overload does not resolve built-in tabs. The undocumented `Autodesk.Windows` ribbon can reach them but is unsupported and may break on any update, so it was declined for the panel. **Don't retry this** for panel placement; if it ever matters, the supported move is a top-level `RVTuk` tab for the whole toolkit. **One exception, decided 2026-11-16 and extended 2026-11-17:** through `Autodesk.Windows`, the Topo Tools start button is also put on Revit's contextual **Modify** tab (while exactly one non-subdivision toposolid is selected), and during a Topo Edit session the Topo Edit panel goes there instead (a copy sharing its buttons, whatever is selected). Both are made as Revit's own panel class (`UIFramework.RvtRibbonPanel`, by reflection), leave the tab in Revit's edit modes, and are built to fail safe — any failure is logged and switches that part off for the rest of the Revit session. When a session starts, the ribbon also switches to the Add-Ins tab once, through `Autodesk.Windows` and under the same fail-safe. **Floor ⇄ Topo** is no longer on the Modify tab. The Add-Ins panels stay, so a Revit update that breaks it costs a shortcut, not the tool. **Room Floors** (ribbon "Room Floor") sits on the same panel, gated by `RegisterRoomFloors` (on), and so does **Round Positions** (ribbon "Round Positions"): `RegisterRoundPositions` (on) gates its pane and external events, not its button, which is always added and, with the flag off, shows a message instead of the pane. Neo Properties is code-complete but hidden behind the `RegisterNeoProperties` flag in `src\RVTuk.Revit\Application.cs` (`RegisterAutoDimensions`, next to it, gates Auto Dimensions and is on). The Project Comparator was stripped from this repo entirely in July 2026 — code, tests, and specs — and continues as its own separate project; recover the pre-strip sources from git history (the `family-browser-polish` branch history) if the new project needs them.

Features:

- **Family Library Indexer** — scans a folder of `.rfa` files, extracts metadata (category, parameters, thumbnails) via the Revit API, and stores it in a shared database.
- **Family Browser** — a searchable/filterable window over that index, with per-family rich-text instructions, tags, favourites, and custom thumbnails, plus a merged "Load/Update family into the active project" button. Settings (library root folder, ignored subfolders, ignored file patterns, deep scan) and a Help/About panel are accessed from within the Family Browser itself, toggled via footer buttons — there is no separate ribbon Config window. The office library lives on Autodesk Docs via Desktop Connector: ACC permissions make one maintainer the only writer, and view-only users browse a verified local copy of the DB with personal favourites (see `docs/tools/family-browser/design.md` §6a).
- **Rishui Zamin** (ribbon button "Area Calc") — reads the Areas on the open sheet and exports the paired `.dxf` + `.dat` files the רישוי זמין area-calculation robot expects (`RZ_FRAME`/`RZ_FLOOR`/`RZ_AREA` layers + attribute blocks). The window's "Setup Usage Keys" action binds the `RZ_*` shared parameters to Areas and creates/tops-up the usage key schedules (`UsageKeyScheduleBuilder`) — no separate ribbon command needed. The window also sets areas' New/Old usage codes through those key schedules, and its Table tab lays the areas out per floor and usage, checked against the plan's allowed % of plot (kept in the project's Extensible Storage). Also shipped as **KKarea**, a standalone add-in for Revit 2023 (see Project Overview). See [`docs/tools/rishui-zamin/notes.md`](docs/tools/rishui-zamin/notes.md).
- **DWG Exporter** (ribbon "DWG Export") — batch-exports a view/sheet set to DWG with filenames produced by the naming rules of the document's saved PDF export setups (so DWG names match the PDF export's names exactly), plus a native DWG export setup for layers/lines. Sheets and non-sheet views take separate naming rules, chosen per item. One run can span several open models (extra models match the active model's set and setups by name; a missing setup either skips that model or is created in it) and can write PDFs to their own folder. An optional eTransmit-style zip bundles the run's DWGs with the images, xrefs and SHX fonts they need — contents found by snapshotting the output folder before/after, since there is no DWG reader. Modal dialog shown inside the command's API context — no external events. Revit 2024–2027. See [`docs/tools/dwg-exporter/README.md`](docs/tools/dwg-exporter/README.md).
- **Auto Dimensions** (ribbon "Auto Dimensions") — draw a detail line on `_DP-Dim Outer` or `_DP-Dim Inner` as a positional reference (outer strings outrank inner ones when both can see the same opening), plus `_DP-Dim Ref` lines drawn from a wall end to a string to mark ends the automatic pass cannot see; the tool dimensions every wall, door and window crossing it — in the host model and in loaded Revit links — re-runnable after model changes without re-picking references. Its only entry point is a dockable pane: tick the reference categories and, per level, which views receive the dimensions (a single-view run is just that one view ticked). See [`docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md`](docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md) [`docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`](docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md) and [`docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md`](docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md).
- **Topo Tools** (ribbon "Topo Tools", on the RVTuk panel — the **Massing & Site** tab is unreachable from the API; see the v1 launch surface above) — an edit session on one toposolid or floor. Select it in a plan view and press Topo Tools; a temporary **Topo Edit** panel appears (Cancel ✗ above Accept ✓ at its left end, then a stack of Draw Lines, Pick Lines (a split button also holding Pick in Steps) and the Height box, then an Update split button holding Heights and Labels; points every 100 cm). Lines are detail lines on the dedicated "_DP-Topo Line" style, restyled by an optional `IUpdater` active only during a session; each carries its height (a shared/survey elevation in metres, measured like a Survey Point level, whatever the project's units) as a `_DP-Topo Height` text note at its middle, editable in the drawing. Update and Accept put points at that height along each line on the toposolid or floor being edited, re-runnable after the lines move, change height or are deleted. The element's own outline corners follow the lines too: each takes the height the lines imply, and goes back to its own height (kept in a separate Extensible Storage record, `RVTukTopoToolsOutlineHeights`) when no line influences it any more. There is no pane. Revit 2024–2027: `Toposolid` does not exist in 2023, so KKarea never ships it. Two API limits shape the whole tool: **the Lines category cannot take a visible parameter** (`AllowsBoundParameters == false`, so the height is a text note linked to its line through Extensible Storage), and **there is no `Toposolid.AddPoints`** — points go through `GetSlabShapeEditor()`, whose `SlabShapeVertex` is a position rather than an element, so each element carries an Extensible Storage ledger of source-line-id → owned points. See [`docs/tools/topo-tools/README.md`](docs/tools/topo-tools/README.md). The separate **Floor ⇄ Topo** button on the RVTuk panel converts the selected floors into toposolids and toposolids into floors (Revit's last-placed type; outline, top-surface points, parameters — one-sided ones parked in Extensible Storage for the return trip — and RVTuk storage carry across; dependents are confirmed in a dialog first). See [`docs/tools/topo-tools/specs/2026-09-29-floor-toposolid-convert-design.md`](docs/tools/topo-tools/specs/2026-09-29-floor-toposolid-convert-design.md). All four line styles (three for Auto Dimensions, one for Topo Tools) are created by `RVTuk.Revit/Shared/LineStyleCreator` when first needed — Topo Tools makes its own, and the `_DP-Topo Height` text type, when a session starts; there is no setup button.
- **Room Floors** (ribbon "Room Floor") — Automatic Ceiling for floors: click inside rooms in a floor plan and each gets a finish floor following its finish-face outline (columns become holes), at the room's level with its top at the room's base. Uses the project's default floor type. Each floor carries an Extensible Storage link to its room's UniqueId, so clicking the room again replaces the floor while keeping the type and offset set in Properties; a floor that hosts elements is never replaced. A plain external command — no pane, no external events. Revit 2024–2027. See [`docs/tools/room-floors/README.md`](docs/tools/room-floors/README.md).
- **Round Positions** (ribbon "Round Positions", on the RVTuk panel) — pulls model geometry that has drifted off round numbers back onto a whole- or half-centimetre grid anchored at Revit's internal origin. Its only entry point is a dockable pane: scan the active model for off-grid elements, read them as a Category → Level tree with counts, then fix a node or everything (one undo step); elements that cannot be fixed are listed with the reason. It moves things and never reshapes them: lengths and angles never change. Only **Walls** are handled so far (exactly-orthogonal straight walls, moved parallel to themselves); framing, lines, levels, grids, reference planes, point families and wall-hosted openings are designed but not built. The "allow half centimetres" choice persists as the settings key `RoundPositionsWholeCentimetresOnly`. Revit 2024–2027 (it lives in `RVTuk.Revit`). See [`docs/tools/round-positions/README.md`](docs/tools/round-positions/README.md).
- **Neo Properties** (hidden for v1) — a dockable pane mirroring the selected element's parameters like the native Properties palette, but with pinned parameters shown first and remaining groups in a fixed custom order. Read-only, single-element only. See [`docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md`](docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md).

### Future features

- **Productivity tools** — interactive room renumbering; auto-dimensioning (folds in the separate `KKimensions` project).
- **Instructions** — editable in-Revit pages with visual tips and how-to guidance.

## Branches

Decided by Daniel on 2026-11-19: one long-lived branch per tool, plus `master`.

| Tool | Branch |
|------|--------|
| Family Browser | `family-browser` |
| Rishui Zamin (Area Calc, KKarea) | `rishui-zamin` |
| DWG Exporter | `dwg-exporter` |
| Auto Dimensions | `auto-dimensions` |
| Topo Tools (with Floor ⇄ Topo) | `topo-tools` |
| Room Floors | `room-floors` |
| Round Positions | `round-positions` |
| Neo Properties | `neo-properties` |

- Work on a tool happens on its branch. Merge it into `master` when it is ready, then bring the branch up to date with `master` before the next piece of work. Do not create other branches, one per task or feature.
- Work that touches no single tool (build scripts, installer, Shared code, toolkit docs) goes on `master`.
- Work in `D:\Coding\RVTuk` itself, switching branches there. Never create worktree folders. `Deploy.ps1` builds whatever branch is checked out, so say which branch to deploy.
- Branches kept for Daniel to review, outside this scheme: `parked/addin-split-tools-step` (the parked Tools add-in step), `topo-probe-2026-11-17` (the throwaway Topo probe, never merged), and `archive/project-review-improvements-24x1gs` (on GitHub only). Do not delete them, and do not build on them, without his say-so.

## Build

A solution file (`RVTuk.sln`) is present with five solution configurations — `Release2023` through `Release2027` (there is no standard `Debug`/`Release`). Build the whole solution per config:

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2026
dotnet build RVTuk.sln -c Release2027
```

The matrix lives in the root Directory.Build.props; a project built with a configuration outside the five (or none) fails the build, but `RVTuk.sln` without `-c` silently picks Release2023, which builds no tests, so always pass `-c` (or run `.\build\Test-All.ps1`). Tool projects opt out: `installer\` has an empty Directory.Build.props, so RVTuk.Setup still builds with `-c Release`. NuGet assets are per configuration, in obj\Release{year}\.

Or build a single project (its project references are built transitively). The projects live under `src\`:

```powershell
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024
```

Each config maps to a target framework and a per-year `DefineConstants` symbol (see Architecture); the JSON serializer and native-load path are switched separately, by target framework (see below):

| Config        | TFM               | Constant    | SQLite provider           | JSON                      |
|---------------|-------------------|-------------|---------------------------|---------------------------|
| `Release2024` | `net48`           | `REVIT2024` | `Microsoft.Data.Sqlite`   | `DataContractJsonSerializer` |
| `Release2025` | `net8.0-windows`  | `REVIT2025` | `Microsoft.Data.Sqlite`   | `System.Text.Json`        |
| `Release2023` | `net48`           | `REVIT2023` | `Microsoft.Data.Sqlite`   | `DataContractJsonSerializer` |
| `Release2026` | `net8.0-windows`  | `REVIT2026` | `Microsoft.Data.Sqlite`   | `System.Text.Json`        |
| `Release2027` | `net10.0-windows` | `REVIT2027` | `Microsoft.Data.Sqlite`   | `System.Text.Json` (in-box) |

All configs use `Microsoft.Data.Sqlite`. Build outputs land in each project's `bin\{year}\Release{year}\{tfm}\`, e.g. `src\RVTuk.Revit\bin\2024\Release2024\net48\`.

Release2023 builds RVTuk.Core, RVTuk.UI and KKarea.Revit. Release2024 to Release2027 build RVTuk.Core, RVTuk.UI and RVTuk.Revit; KKarea.Revit builds under Release2023 only. The test projects build under Release2024, Release2025 and Release2027. global.json pins the .NET 10 SDK (10.0.x) for every configuration; Revit 2027 itself runs on .NET 10 (its API package targets `net10.0-windows`). The 2026 API dropped `SlabShapeEditor.DrawPoint` and `DrawSplitLine`; `RVTuk.Revit\TopoTools\SlabShape.cs` uses `AddPoint` from 2026 (`#if REVIT2026 || REVIT2027`) and `AddSplitLine` after 2024, and whether that `AddPoint` accepts points on an edge is not yet checked in Revit. The net48-vs-net8 code split (JSON serializer choice, and the native `e_sqlite3.dll` pre-load on net48) is gated on the compiler-provided `NETFRAMEWORK` symbol, not on `REVIT2024`; the `REVIT<year>` constants are for year-specific Revit API differences only (e.g. the `ElementId` shims in the shared area sources).

## Tests

Core's xunit suite lives in `tests\RVTuk.Core.Tests` (repositories, indexer, config/migration, Rishui Zamin writers/validator), mirroring the per-tool folders. Run everything before committing: `.\build\Test-All.ps1` — every test on net48 (Release2024), net8 (Release2025) and net10 (Release2027); it also refuses untracked .dxf/.dat/.dwfx/.rfa files. One project:

```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025
```

On a non-Windows .NET 10 SDK (CI, cloud agents) add `-c Release2025 -p:EnableWindowsTargeting=true` — the suite builds and runs fine on Linux. The WPF/Revit projects (`RVTuk.UI`, `RVTuk.Revit`, `KKarea.Revit`) can only be compiled on Windows.

`tests\RVTuk.UI.Tests` holds WPF tests that run without Revit (instruction XAML read/write). It follows the configuration like every project — `-c Release2024` tests net48, `-c Release2025` net8, `-c Release2027` net10 — and all must pass before committing a change to instruction reading or writing:
```powershell
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2027
```
Its fixtures are sanitized (`FamilyBrowser\Fixtures\sanitize_instructions.py`): never commit a real database row. Database tests run on every framework: each test project carries an xunit.runner.json that switches shadow copy off, because the shadow folder has no e_sqlite3.dll.

The setup exe's tests (`installer\RVTuk.Setup.Tests`, net48, outside `RVTuk.sln`) run at the end of `Test-All.ps1`; alone: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release`.

`.\build\Test-Staging.ps1` (PowerShell 7) stages all five years and checks every add-in folder: the exact file set, the entry class, the Revit API year, the framework, the theme, addin.json, and that RVTuk.sln builds each add-in for the years its addin.props names. Run it after any change to a project file, to an addin.props or to the staging function.

## Deployment

`Deploy.ps1` (must run as Administrator) builds and stages the add-ins from the working tree with `build\Staging.ps1` into `installer\payload-staging`, then hands that folder to the setup exe's install engine (`RVTukSetup.exe --install … --source …`), which copies the files and writes the `.addin` manifests:

```powershell
# From the RVTuk folder, in an elevated shell:
.\Deploy.ps1                # every add-in, every Revit year found on the PC
.\Deploy.ps1 2024           # only Revit 2024
.\Deploy.ps1 2024 2027      # only Revit 2024 and 2027
.\Deploy.ps1 -AddIn KKarea  # only KKarea
.\Deploy.ps1 -Clean 2025    # remove the add-ins from Revit 2025
```

It deploys for the Revit years found on the PC; a year without Revit is no longer written to, unless named. Deploys to `C:\ProgramData\Autodesk\Revit\Addins\{year}\{RVTuk|KKarea}\` for Revit up to 2026 and to `C:\Program Files\Autodesk\Revit\Addins\{year}\RVTuk\` from 2027 — Revit 2027 no longer reads machine-wide add-ins from ProgramData. The rule lives only in the setup exe (`RealMachine.YearFolder`). Restart Revit after deploying. A year is skipped, and the script exits 1, when its Revit is open or a file of the add-in is in use; a skipped year is left exactly as it was, while the others proceed. A year whose build fails is warned about, left out, and makes the script exit 1 (after the years that did build are deployed). `-Clean` removes the add-ins instead (`--remove`). 2023 deploys **KKarea** (entry class `KKarea.Revit.Application`, its own stable ClientId `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC`) to `...\Addins\2023\KKarea\`; 2024, 2025, 2026 and 2027 deploy RVTuk.

**Signing (since 2026-11-23).** So Revit loads RVTuk without the "Always Load" prompt, after updates too, staging signs each add-in's own DLLs (its `AddInAssemblies`; third-party DLLs are left as they are) with the office's self-signed code-signing certificate, and puts its public part in the payload as `signing.cer`. The setup exe adds that to the machine's Trusted Root and Trusted Publishers stores before installing (`Engine.TrustPublisher`; `--target` leaves the stores alone). The certificate was made once with `build\New-SigningCertificate.ps1`: its public part is the committed `installer\RVTukSigning.cer`, its private key lives only in Daniel's Windows certificate store on his PC (`Cert:\CurrentUser\My`, not exportable), valid until 2036-11-23. Without that key, `Deploy.ps1` stages unsigned with a warning, and `Build-Installer.ps1` (`-Strict`) refuses to build. Lost key: run the script again with `-Replace` and commit the new `.cer`. Never sign anything else with it: every office PC trusts it. Whether Revit 2024–2027 really load it without asking is not yet checked in Revit.

Staging (`Copy-AddInOutput`) copies the build output flat plus the native `e_sqlite3.dll` (from `runtimes\win-x64\native`). For this to work the build output must actually contain the dependency closure: net48 copies NuGet deps to `bin` automatically, but **net8 libraries do not** — so `RVTuk.Revit` (net8) sets `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to pull `Microsoft.Data.Sqlite` + `e_sqlite3.dll` into `bin`. Without it, the Revit 2025 deploy would contain only the three project DLLs and fail to load.

The `.addin` manifest registers the add-in with:
- **Entry class**: `RVTuk.Revit.Application`
- **Client ID**: `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5`
- **Vendor ID**: `KnafoKlimor`

Each add-in's host project folder holds an `addin.props` (`src\RVTuk.Revit\addin.props`, `src\KKarea.Revit\addin.props`): its key, name, manifest file name, frozen ClientId, entry class, version, Revit years, assemblies (host first) and whether it ships the third-party DLLs. It is read by the scripts, not imported by the project; the scripts' add-in table comes from these files. `build\Staging.ps1` (dot-sourced, Windows PowerShell 5.1 and PowerShell 7) reads them (`Get-AddIns`), copies a build output with the copy/strip rules (`Copy-AddInOutput`) and stages a setup payload (`New-Staging`).

### Installer for other computers

`Build-Installer.ps1` produces `dist\RVTukSetup.exe` — a standalone net48 console exe (`installer\RVTuk.Setup`, deliberately **not** in `RVTuk.sln` since it has no `Release{year}` configs) with the payload embedded as `payload.zip`. It wipes each add-in project's `bin\{year}` so stale DLLs from dropped packages can't ship, then `build\Staging.ps1` (`New-Staging -Build -Strict`) builds and stages the payload: `catalog.json` (the add-ins, their years and versions) plus `<AddIn>\<year>\` with each add-in's files and its `addin.json`. The exe writes the `.addin` manifests itself. On a target machine the exe self-elevates and installs into the machine-wide Addins folder (ProgramData up to 2026, Program Files from 2027) of each Revit year whose `Revit.exe` it finds. A year is skipped when its Revit is open **or any file of the add-in is in use**; nothing in that year is changed, and the exe then exits 1. Every package is marked as a dev build and always replaces what is installed (the release step that would number packages belonged to the add-in split, which was stopped on 2026-11-16 — see the note at the top of `docs/toolkit/specs/2026-11-12-addin-split-design.md`). Flags: `--install <names|all>` (the default: all), `--remove <names|all>`, `--uninstall` (= `--remove all`), `--years <y,y>` (names the years outright, instead of detecting them), `--source <dir>` (a payload folder instead of the embedded zip), `--target <dir>` (install under another folder for testing, skips elevation), `--quiet`, `--no-pause`. `--all` is gone. `--remove all` also removes a folder of ours that the catalog does not know, and so does `--remove` given such a folder's name, but only when its `addin.json` and its manifest are owned by the Administrators group, the system account or the current user.

## Architecture

Three projects with a strict dependency order (no circular references):

```
RVTuk.Core        — pure business logic, no Revit or UI dependency
       ↑                  src\RVTuk.Core
RVTuk.UI          — WPF dialogs/views (MVVM), depends on Core only
       ↑                  src\RVTuk.UI
RVTuk.Revit       — Revit add-in host: IExternalApplication entry point,
                          ribbon setup, external-event handlers; depends on Core + UI
                          src\RVTuk.Revit
```

Inside each project (and `tests\RVTuk.Core.Tests` and `tests\RVTuk.UI.Tests`) the structure is **one folder per
tool** (`FamilyBrowser/`, `RishuiZamin/`, `AutoDimensions/`, `TopoTools/`, `RoomFloors/`, `RoundPositions/`, `NeoProperties/`, …) plus
`Shared/` for genuinely cross-tool code (`AppConfig`/`ConfigManager`, `PathUtil`, the
UI's `ViewModelBase`/`RelayCommand`/converters/theme). Namespaces follow folders
exactly (e.g. `RVTuk.Core.FamilyBrowser.Database`, `RVTuk.UI.RishuiZamin.Views`,
`RVTuk.Core.Shared.Config`). `RVTuk.Revit.Application` stays at the project root —
it is the `.addin` entry class.

**KKarea.Revit** (`src\KKarea.Revit`) is a fourth project: a standalone add-in for Revit 2023 (net48) hosting only Rishui Zamin; the root `Directory.Build.props` picks its year (`RevitYear`, hence the API package), TFM and `REVIT<year>` constant from the configuration. It references Core + UI and compiles the Revit-side area sources (`AreaExtractor`, `UsageKeyScheduleBuilder`, the three area external-event handlers from `RVTuk.Revit\RishuiZamin\`) as **linked shared source** from `RVTuk.Revit` against each year's API — it must never reference `RVTuk.Revit` itself. `#if REVIT2023` branches in those shared files cover the 2023 API gaps (`ElementId.IntegerValue` vs `.Value`, int-only `ElementId` ctor).

**RVTuk.Core** holds data models, the SQLite schema/repositories, OLE thumbnail read/write, metadata-XML parsing, and config. Keep it free of Revit API and WPF types so it can be reasoned about in isolation. It multi-targets `net48` (Release2023/2024), `net8.0-windows` (Release2025/2026) and `net10.0-windows` (Release2027) and **does** carry NuGet dependencies, which differ per target:
- net48: `Microsoft.Data.Sqlite`, GAC `System.Drawing`. No `System.Text.Json` (its transitive polyfills clash with Revit's preloaded assemblies — JSON uses `DataContractJsonSerializer`).
- net8: `Microsoft.Data.Sqlite`, `System.Drawing.Common` 8.x (`System.Text.Json` is in-box).
- net10: `Microsoft.Data.Sqlite`, `System.Drawing.Common` 10.x (`System.Text.Json` is in-box).
- both: `OpenMCDF` pinned to `3.1.2` (chosen to match what other Revit add-ins preloaded; that no longer holds — pyRevit now preloads 3.1.4 — see the thumbnail bug in `docs/tools/family-browser/backlog.md`).

**SQLite provider:** all configs use `Microsoft.Data.Sqlite`. `System.Data.SQLite` was dropped because its native win32 VFS **cannot open databases over some UNC shares** (`\\server\share`) — it throws `unable to open database file` even when the file is readable, while `Microsoft.Data.Sqlite`'s bundled `e_sqlite3` opens the same file fine. The repositories alias `SQLiteConnection`/`SQLiteCommand` to the `Microsoft.Data.Sqlite` types. `FamilyBrowser/Database/SqliteNative.EnsureLoaded()` (called by every repository ctor) pre-loads `e_sqlite3.dll` by full path on net48, because Revit resolves native libs relative to `Revit.exe`, not the add-in folder; staging (`Copy-AddInOutput` in `build\Staging.ps1`) copies `e_sqlite3.dll` flat into the add-in folder. Keep SQL portable (e.g. one statement per `ExecuteScalar`).

**RVTuk.UI** multi-targets the same frameworks, uses WPF (`UseWPF`) and WinForms (`UseWindowsForms`), and contains all user-facing windows/controls. Depends on Core only — it must not reference any Revit type. Revit interactions are passed in as plain `Func<>`/`Action` delegates from the Revit project.

**RVTuk.Revit** is the only project that references the Revit API (`Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`, pinned to the configuration's year (`2024.*`–`2027.*`) by `RevitYear` from `Directory.Build.props`, `compile`-only with `ExcludeAssets="runtime"`). It hosts the ribbon, commands, and the `ExternalEvent` handlers, and wires UI delegates to that API.

### Docs layout

Everything about one tool lives in `docs/tools/<tool>/` — `README.md` (what it is,
status, entry points), `backlog.md` (its bugs/ideas/done), living design docs, and
dated `specs/` + `plans/`. **New specs and plans go there** (toolkit-wide ones go to
`docs/toolkit/specs|plans/`) — this overrides any default location a planning skill
suggests. `docs/BACKLOG.md` holds toolkit-wide items only. `docs/future/` holds specs
for tools not yet built; `docs/archive/` holds retired/superseded docs, kept verbatim
(historical docs reference pre-reorg paths — that's intentional).

## Threading Model

- Revit API calls **must** run on Revit's main thread, marshaled via `ExternalEvent` + a `ManualResetEventSlim` ping-pong (the handlers in `RVTuk.Revit`). Never call the Revit API from a background thread.
- Background work (file I/O, SQLite, OLE parsing, scans) runs on the `ThreadPool`.
- WPF property updates from background threads go through the `Dispatcher`.
- The load/update handlers are shared singletons and `ExternalEvent.Raise()` coalesces, so serialize concurrent loads (the Family Browser does this with a lock) — don't fire several raises at once.

## Revit Add-in Notes

- The Revit API NuGet packages (Nice3point wrappers) are compile-time references only; the actual DLLs come from the Revit installation at runtime. Do not copy Revit API DLLs into the output.
- Changes to the ClientId GUID in an addin.props will break existing installations — it must stay stable (FrozenIdentifierTests pins both).
- The `Manifests/` folder is reserved for hand-edited `.addin` files if needed; the setup exe's engine writes the manifests from the catalog (`Engine.ManifestXml`).
- Dates are persisted as UTC ISO-8601 (`DateTime.ToString("o")`); read them back through `DbConvert.ParseUtc` so the instant is preserved regardless of the machine's local time zone.
- Relative-path DB keys go through `PathUtil.GetRelativePath` (no framework-conditional logic) so net48 and net8 produce identical keys for the shared database.
