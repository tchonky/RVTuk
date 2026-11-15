# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RVTuk is a Revit add-in toolkit for Knafo Klimor Architects LTD. It supports Revit 2024, 2025 and 2027 simultaneously via separate build configurations (2027 builds and deploys since November 2026; its tools have not yet been checked one by one in Revit 2027). Revit 2023 and 2026 are served by **KKarea**, a separate minimal add-in in this repo (`src\KKarea.Revit`) that hosts only the Rishui Zamin (Area Calc) tool — RVTuk itself was dropped from 2023 and does not ship for 2026.

> **Product vision, audience, and roadmap live in [`VISION.md`](VISION.md).** This file is the technical reference (build, architecture, threading, deploy).

## Terminology

| Term | Meaning |
|------|---------|
| **Toolkit** | RVTuk itself: one add-in, one ribbon, one install. |
| **Tool** | A user-facing feature with its own button/pane and UI: **Family Browser**, **Rishui Zamin** (ribbon label "Area Calc"), **Auto Dimensions**, **Topo Tools**, **Room Floors** (ribbon label "Room Floor"), **Round Positions**, **Neo Properties**. |
| **Layer** | A C# project: `RVTuk.Core` (logic) → `RVTuk.UI` (WPF) → `RVTuk.Revit` (Revit host). A tool is a vertical slice across the layers. |
| **Host** | A project that loads into Revit: `RVTuk.Revit` (2024/2025/2027) and `KKarea.Revit` (2023/2026, ships only the Rishui Zamin tool). |

Canonical tool names in code and folders: `FamilyBrowser`, `RishuiZamin`,
`AutoDimensions`, `TopoTools`, `RoomFloors`, `RoundPositions`, `NeoProperties`. Inside every project (and each test project) the
structure is one folder per tool plus `Shared/`, and **namespace = root namespace +
folder path, exactly**. A file lives in a tool folder iff only that tool uses it.

**v1 launch surface:** the **Family Browser**, **Rishui Zamin** (ribbon "Area Calc"), **DWG Exporter** (ribbon "DWG Export"), and **Auto Dimensions** are registered on the single "RVTuk" ribbon panel. **Topo Tools** joins them on that panel, gated by `RegisterTopoTools` (on). It was intended for Revit's **Massing & Site** tab; that is impossible and was verified in Revit 2024 — the API places a custom panel only on **Add-Ins**, on **Analyze**, or on a tab the add-in creates itself, and the string overload does not resolve built-in tabs. The undocumented `Autodesk.Windows` ribbon can reach them but is unsupported and may break on any update, so it was declined. **Don't retry this**; if the placement ever matters, the supported move is a top-level `RVTuk` tab for the whole toolkit. **Room Floors** (ribbon "Room Floor") sits on the same panel, gated by `RegisterRoomFloors` (on), and so does **Round Positions** (ribbon "Round Positions"): `RegisterRoundPositions` (on) gates its pane and external events, not its button, which is always added and, with the flag off, shows a message instead of the pane. Neo Properties is code-complete but hidden behind the `RegisterNeoProperties` flag in `src\RVTuk.Revit\Application.cs` (`RegisterAutoDimensions`, next to it, gates Auto Dimensions and is on). The Project Comparator was stripped from this repo entirely in July 2026 — code, tests, and specs — and continues as its own separate project; recover the pre-strip sources from git history (the `family-browser-polish` branch history) if the new project needs them.

Features:

- **Family Library Indexer** — scans a folder of `.rfa` files, extracts metadata (category, parameters, thumbnails) via the Revit API, and stores it in a shared database.
- **Family Browser** — a searchable/filterable window over that index, with per-family rich-text instructions, tags, favourites, and custom thumbnails, plus a merged "Load/Update family into the active project" button. Settings (library root folder, ignored subfolders, ignored file patterns, deep scan) and a Help/About panel are accessed from within the Family Browser itself, toggled via footer buttons — there is no separate ribbon Config window.
- **Rishui Zamin** (ribbon button "Area Calc") — reads the Areas on the open sheet and exports the paired `.dxf` + `.dat` files the רישוי זמין area-calculation robot expects (`RZ_FRAME`/`RZ_FLOOR`/`RZ_AREA` layers + attribute blocks). The window's "Setup Usage Keys" action binds the `RZ_*` shared parameters to Areas and creates/tops-up the usage key schedules (`UsageKeyScheduleBuilder`) — no separate ribbon command needed. Also shipped as **KKarea**, a standalone add-in for Revit 2023 and 2026 (see Project Overview). See [`docs/tools/rishui-zamin/notes.md`](docs/tools/rishui-zamin/notes.md).
- **DWG Exporter** (ribbon "DWG Export") — batch-exports a view/sheet set to DWG with filenames produced by the naming rules of the document's saved PDF export setups (so DWG names match the PDF export's names exactly), plus a native DWG export setup for layers/lines. Sheets and non-sheet views take separate naming rules, chosen per item. One run can span several open models (extra models match the active model's set and setups by name; a missing setup either skips that model or is created in it) and can write PDFs to their own folder. An optional eTransmit-style zip bundles the run's DWGs with the images, xrefs and SHX fonts they need — contents found by snapshotting the output folder before/after, since there is no DWG reader. Modal dialog shown inside the command's API context — no external events. Revit 2024/25 only. See [`docs/tools/dwg-exporter/README.md`](docs/tools/dwg-exporter/README.md).
- **Auto Dimensions** (ribbon "Auto Dimensions") — draw a detail line on `_DP-Dim Outer` or `_DP-Dim Inner` as a positional reference (outer strings outrank inner ones when both can see the same opening), plus `_DP-Dim Ref` lines drawn from a wall end to a string to mark ends the automatic pass cannot see; the tool dimensions every wall, door and window crossing it — in the host model and in loaded Revit links — re-runnable after model changes without re-picking references. Its only entry point is a dockable pane: tick the reference categories and, per level, which views receive the dimensions (a single-view run is just that one view ticked). See [`docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md`](docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md) [`docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`](docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md) and [`docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md`](docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md).
- **Topo Tools** (ribbon "Topo Tools", on the RVTuk panel — the **Massing & Site** tab is unreachable from the API; see the v1 launch surface above) — draw detail lines on the dedicated "_DP-Topo Line" style in a plan view, give each a height (a shared/survey elevation) in the pane, and the tool puts points at that height along each line on the toposolids beneath them — re-runnable after the lines move, change height or are deleted. Its only entry point is a dockable pane. Revit 2024/2025 only: `Toposolid` does not exist in 2023, so KKarea never ships it. Two API limits shape the whole tool: **the Lines category cannot take a visible parameter** (`AllowsBoundParameters == false`, so heights live in the tool's own Extensible Storage and are typed in the pane, not in Properties), and **there is no `Toposolid.AddPoints`** — points go through `GetSlabShapeEditor()`, whose `SlabShapeVertex` is a position rather than an element, so each toposolid carries an Extensible Storage ledger of source-line-id → owned points. See [`docs/tools/topo-tools/README.md`](docs/tools/topo-tools/README.md). The pane's **⇄** button converts the selected floors into toposolids and toposolids into floors (Revit's last-placed type; outline, top-surface points, parameters — one-sided ones parked in Extensible Storage for the return trip — and RVTuk storage carry across; dependents are confirmed in a dialog first). See [`docs/tools/topo-tools/specs/2026-09-29-floor-toposolid-convert-design.md`](docs/tools/topo-tools/specs/2026-09-29-floor-toposolid-convert-design.md). All four line styles (three for Auto Dimensions, one for Topo Tools) are created by `RVTuk.Revit/Shared/LineStyleCreator` on each pane's first refresh — there is no setup button.
- **Room Floors** (ribbon "Room Floor") — Automatic Ceiling for floors: click inside rooms in a floor plan and each gets a finish floor following its finish-face outline (columns become holes), at the room's level with its top at the room's base. Uses the project's default floor type. Each floor carries an Extensible Storage link to its room's UniqueId, so clicking the room again replaces the floor while keeping the type and offset set in Properties; a floor that hosts elements is never replaced. A plain external command — no pane, no external events. Revit 2024/2025 only. See [`docs/tools/room-floors/README.md`](docs/tools/room-floors/README.md).
- **Round Positions** (ribbon "Round Positions", on the RVTuk panel) — pulls model geometry that has drifted off round numbers back onto a whole- or half-centimetre grid anchored at Revit's internal origin. Its only entry point is a dockable pane: scan the active model for off-grid elements, read them as a Category → Level tree with counts, then fix a node or everything (one undo step); elements that cannot be fixed are listed with the reason. It moves things and never reshapes them: lengths and angles never change. Only **Walls** are handled so far (exactly-orthogonal straight walls, moved parallel to themselves); framing, lines, levels, grids, reference planes, point families and wall-hosted openings are designed but not built. The "allow half centimetres" choice persists as the settings key `RoundPositionsWholeCentimetresOnly`. Revit 2024/2025 only (it lives in `RVTuk.Revit`). See [`docs/tools/round-positions/README.md`](docs/tools/round-positions/README.md).
- **Neo Properties** (hidden for v1) — a dockable pane mirroring the selected element's parameters like the native Properties palette, but with pinned parameters shown first and remaining groups in a fixed custom order. Read-only, single-element only. See [`docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md`](docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md).

### Future features

- **Productivity tools** — interactive room renumbering; auto-dimensioning (folds in the separate `KKimensions` project).
- **Instructions** — editable in-Revit pages with visual tips and how-to guidance.

## Build

A solution file (`RVTuk.sln`) is present with five solution configurations — `Release2023` through `Release2027` (there is no standard `Debug`/`Release`). Build the whole solution per config:

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2026
dotnet build RVTuk.sln -c Release2027
```

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

`Release2023` and `Release2026` build only `RVTuk.Core`, `RVTuk.UI`, and `KKarea.Revit`; `RVTuk.Revit` does not build for those years. `Release2027` builds `RVTuk.Core`, `RVTuk.UI` and `RVTuk.Revit` (KKarea does not build for 2027). Revit 2027 runs on .NET 10 (its API package targets `net10.0-windows`), so that config needs the .NET 10 SDK. The 2027 API dropped `SlabShapeEditor.DrawPoint` and `DrawSplitLine`; `RVTuk.Revit\TopoTools\SlabShape.cs` uses `AddPoint`/`AddSplitLine` there (`#if REVIT2027`), and whether 2027's `AddPoint` accepts points on an edge is not yet checked in Revit. The net48-vs-net8 code split (JSON serializer choice, and the native `e_sqlite3.dll` pre-load on net48) is gated on the compiler-provided `NETFRAMEWORK` symbol, not on `REVIT2024`; the `REVIT<year>` constants are for year-specific Revit API differences only (e.g. the `ElementId` shims in the shared area sources).

## Tests

Core's xunit suite lives in `tests\RVTuk.Core.Tests` (repositories, indexer, config/migration, Rishui Zamin writers/validator), mirroring the per-tool folders. Run it before committing Core changes:

```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```

On a non-Windows SDK (CI, cloud agents) add `-p:EnableWindowsTargeting=true` — the suite builds and runs fine on Linux. The WPF/Revit projects (`RVTuk.UI`, `RVTuk.Revit`, `KKarea.Revit`) can only be compiled on Windows.

`tests\RVTuk.UI.Tests` holds WPF tests that run without Revit (instruction XAML read/write). It follows the configuration — `-c Release2024` tests net48, `-c Release2025` tests net8 — and both must pass before committing a change to instruction reading or writing:
```powershell
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025
```
Its fixtures are sanitized (`FamilyBrowser\Fixtures\sanitize_instructions.py`): never commit a real database row. Tests that open a database are net8-only (`#if !NETFRAMEWORK`), because SQLite does not initialise in the net48 test host: opening a connection throws a `FileLoadException` for `System.Memory` inside `SQLitePCL.Batteries_V2.DoDynamic_cdecl`. The cause is not settled yet (copying `e_sqlite3.dll` flat does not help); see "Phase 0 record" in [`docs/toolkit/specs/2026-11-12-addin-split-design.md`](docs/toolkit/specs/2026-11-12-addin-split-design.md).

## Deployment

`Deploy.ps1` (must run as Administrator) builds/copies the right binary set per Revit version, strips the net48 BCL polyfill DLLs Revit already preloads, and generates the `.addin` manifest XML:

```powershell
# From the RVTuk folder, in an elevated shell:
.\Deploy.ps1            # all versions
.\Deploy.ps1 2024       # only Revit 2024 (optional version filter)
```

Deploys to `C:\ProgramData\Autodesk\Revit\Addins\{year}\{RVTuk|KKarea}\` for Revit up to 2026 and to `C:\Program Files\Autodesk\Revit\Addins\{year}\RVTuk\` from 2027 — Revit 2027 no longer reads machine-wide add-ins from ProgramData. The rule lives once per script: `Get-AddinsBase` in `Deploy.ps1` and `MachineAddinsBase` in the setup exe. Restart Revit after deploying. Each version deploys independently: a year whose Revit is currently open (DLLs locked) or whose build output is missing is skipped with a warning while the others proceed. 2023 and 2026 deploy **KKarea** (entry class `KKarea.Revit.Application`, its own stable ClientId `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC`, the same for every year) to `...\Addins\{year}\KKarea\`; 2024, 2025 and 2027 deploy RVTuk.

`Deploy.ps1` copies the build output flat plus the native `e_sqlite3.dll` (from `runtimes\win-x64\native`). For this to work the build output must actually contain the dependency closure: net48 copies NuGet deps to `bin` automatically, but **net8 libraries do not** — so `RVTuk.Revit` (net8) sets `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to pull `Microsoft.Data.Sqlite` + `e_sqlite3.dll` into `bin`. Without it, the Revit 2025 deploy would contain only the three project DLLs and fail to load.

The `.addin` manifest registers the add-in with:
- **Entry class**: `RVTuk.Revit.Application`
- **Client ID**: `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5`
- **Vendor ID**: `KnafoKlimor`

### Installer for other computers

`Build-Installer.ps1` produces `dist\RVTukSetup.exe` — a standalone net48 console exe (`installer\RVTuk.Setup`, deliberately **not** in `RVTuk.sln` since it has no `Release{year}` configs) with the per-year payload embedded as `payload.zip`. It wipes each project's `bin\{year}` and rebuilds so stale DLLs from dropped packages can't ship, stages with the same copy/strip rules as `Deploy.ps1` (the version table and staging rules in the two scripts must stay in sync), and writes the same `.addin` manifests. On a target machine the exe self-elevates, installs only for the Revit years whose machine-wide `Addins\{year}` folder exists (ProgramData up to 2026, Program Files from 2027), and skips years whose Revit is running. Flags: `--uninstall`, `--all` (ignore year detection), `--target <dir>` (redirect to another folder for testing, skips elevation), `--no-pause`.

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

**KKarea.Revit** (`src\KKarea.Revit`) is a fourth project: a standalone add-in for Revit 2023 (net48) and 2026 (net8) hosting only Rishui Zamin; a `RevitYear` property in its csproj picks the TFM, API package and `REVIT<year>` constant from the configuration. It references Core + UI and compiles the Revit-side area sources (`AreaExtractor`, `UsageKeyScheduleBuilder`, the three area external-event handlers from `RVTuk.Revit\RishuiZamin\`) as **linked shared source** from `RVTuk.Revit` against each year's API — it must never reference `RVTuk.Revit` itself. `#if REVIT2023` branches in those shared files cover the 2023 API gaps (`ElementId.IntegerValue` vs `.Value`, int-only `ElementId` ctor).

**RVTuk.Core** holds data models, the SQLite schema/repositories, OLE thumbnail read/write, metadata-XML parsing, and config. Keep it free of Revit API and WPF types so it can be reasoned about in isolation. It multi-targets `net48` (Release2023/2024), `net8.0-windows` (Release2025/2026) and `net10.0-windows` (Release2027) and **does** carry NuGet dependencies, which differ per target:
- net48: `Microsoft.Data.Sqlite`, GAC `System.Drawing`. No `System.Text.Json` (its transitive polyfills clash with Revit's preloaded assemblies — JSON uses `DataContractJsonSerializer`).
- net8: `Microsoft.Data.Sqlite`, `System.Text.Json`, `System.Drawing.Common`.
- net10: `Microsoft.Data.Sqlite`, `System.Drawing.Common` 10.x (`System.Text.Json` is in-box).
- both: `OpenMCDF` pinned to `3.1.2` (matches the version other Revit add-ins preload).

**SQLite provider:** all configs use `Microsoft.Data.Sqlite`. `System.Data.SQLite` was dropped because its native win32 VFS **cannot open databases over some UNC shares** (`\\server\share`) — it throws `unable to open database file` even when the file is readable, while `Microsoft.Data.Sqlite`'s bundled `e_sqlite3` opens the same file fine. The repositories alias `SQLiteConnection`/`SQLiteCommand` to the `Microsoft.Data.Sqlite` types. `FamilyBrowser/Database/SqliteNative.EnsureLoaded()` (called by every repository ctor) pre-loads `e_sqlite3.dll` by full path on net48, because Revit resolves native libs relative to `Revit.exe`, not the add-in folder; `Deploy.ps1` copies `e_sqlite3.dll` flat into the add-in folder. Keep SQL portable (e.g. one statement per `ExecuteScalar`).

**RVTuk.UI** multi-targets the same frameworks, uses WPF (`UseWPF`) and WinForms (`UseWindowsForms`), and contains all user-facing windows/controls. Depends on Core only — it must not reference any Revit type. Revit interactions are passed in as plain `Func<>`/`Action` delegates from the Revit project.

**RVTuk.Revit** is the only project that references the Revit API (`Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`, pinned `2024.*` / `2025.*` / `2027.*` by the csproj's `RevitYear`, `compile`-only with `ExcludeAssets="runtime"`). It hosts the ribbon, commands, and the `ExternalEvent` handlers, and wires UI delegates to that API.

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
- Changes to the `ClientId` GUID in `Deploy.ps1` will break existing installations — it must stay stable.
- The `Manifests/` folder is reserved for hand-edited `.addin` files if needed; `Deploy.ps1` generates them dynamically.
- Dates are persisted as UTC ISO-8601 (`DateTime.ToString("o")`); read them back through `DbConvert.ParseUtc` so the instant is preserved regardless of the machine's local time zone.
- Relative-path DB keys go through `PathUtil.GetRelativePath` (no framework-conditional logic) so net48 and net8 produce identical keys for the shared database.
