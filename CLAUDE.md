# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RVTuk is a Revit add-in toolkit for Knafo Klimor Architects LTD. It supports Revit 2024 and 2025 simultaneously via separate build configurations. Revit 2023 is served by **KKarea**, a separate minimal add-in in this repo (`src\KKarea.Revit`) that hosts only the Rishui Zamin (Area Calc) tool — RVTuk itself was dropped from 2023.

> **Product vision, audience, and roadmap live in [`VISION.md`](VISION.md).** This file is the technical reference (build, architecture, threading, deploy).

## Terminology

| Term | Meaning |
|------|---------|
| **Toolkit** | RVTuk itself: one add-in, one ribbon, one install. |
| **Tool** | A user-facing feature with its own button/pane and UI: **Family Browser**, **Rishui Zamin** (ribbon label "Area Calc"), **Auto Dimensions**, **Neo Properties**. |
| **Layer** | A C# project: `RVTuk.Core` (logic) → `RVTuk.UI` (WPF) → `RVTuk.Revit` (Revit host). A tool is a vertical slice across the layers. |
| **Host** | A project that loads into Revit: `RVTuk.Revit` (2024/25) and `KKarea.Revit` (2023, ships only the Rishui Zamin tool). |

Canonical tool names in code and folders: `FamilyBrowser`, `RishuiZamin`,
`AutoDimensions`, `NeoProperties`. Inside every project (and the test project) the
structure is one folder per tool plus `Shared/`, and **namespace = root namespace +
folder path, exactly**. A file lives in a tool folder iff only that tool uses it.

**v1 launch surface:** the **Family Browser**, **Rishui Zamin** (ribbon "Area Calc"), **DWG Exporter** (ribbon "DWG Export"), and **Auto Dimensions** are registered on the single "RVTuk" ribbon panel. Neo Properties is code-complete but hidden behind the `RegisterNeoProperties` flag in `src\RVTuk.Revit\Application.cs` (`RegisterAutoDimensions`, next to it, gates Auto Dimensions and is on). The Project Comparator was stripped from this repo entirely in July 2026 — code, tests, and specs — and continues as its own separate project; recover the pre-strip sources from git history (the `family-browser-polish` branch history) if the new project needs them.

Features:

- **Family Library Indexer** — scans a folder of `.rfa` files, extracts metadata (category, parameters, thumbnails) via the Revit API, and stores it in a shared database.
- **Family Browser** — a searchable/filterable window over that index, with per-family rich-text instructions, tags, favourites, and custom thumbnails, plus a merged "Load/Update family into the active project" button. Settings (library root folder, ignored subfolders, ignored file patterns, deep scan) and a Help/About panel are accessed from within the Family Browser itself, toggled via footer buttons — there is no separate ribbon Config window.
- **Rishui Zamin** (ribbon button "Area Calc") — reads the Areas on the open sheet and exports the paired `.dxf` + `.dat` files the רישוי זמין area-calculation robot expects (`RZ_FRAME`/`RZ_FLOOR`/`RZ_AREA` layers + attribute blocks). The window's "Setup Usage Keys" action binds the `RZ_*` shared parameters to Areas and creates/tops-up the usage key schedules (`UsageKeyScheduleBuilder`) — no separate ribbon command needed. Also shipped as **KKarea**, a standalone Revit 2023 add-in (see Project Overview). See [`docs/tools/rishui-zamin/notes.md`](docs/tools/rishui-zamin/notes.md).
- **DWG Exporter** (ribbon "DWG Export") — batch-exports a view/sheet set to DWG with filenames produced by the naming rules of the document's saved PDF export setups (so DWG names match the PDF export's names exactly), plus a native DWG export setup for layers/lines. Modal dialog shown inside the command's API context — no external events. Revit 2024/25 only. See [`docs/tools/dwg-exporter/README.md`](docs/tools/dwg-exporter/README.md).
- **Auto Dimensions** (ribbon "Auto Dimensions") — draw a detail line on the dedicated "Dimensions_Line" style as a positional reference; the tool dimensions every wall, door and window crossing it — in the host model and in loaded Revit links — re-runnable after model changes without re-picking references. Its only entry point is a dockable pane: tick the reference categories and, per level, which views receive the dimensions (a single-view run is just that one view ticked). See [`docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md`](docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md) [`docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`](docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md) and [`docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md`](docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md).
- **Neo Properties** (hidden for v1) — a dockable pane mirroring the selected element's parameters like the native Properties palette, but with pinned parameters shown first and remaining groups in a fixed custom order. Read-only, single-element only. See [`docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md`](docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md).

### Future features

- **Productivity tools** — interactive room renumbering; auto-dimensioning (folds in the separate `KKimensions` project).
- **Instructions** — editable in-Revit pages with visual tips and how-to guidance.

## Build

A solution file (`RVTuk.sln`) is present with three solution configurations — `Release2023`, `Release2024`, `Release2025` (there is no standard `Debug`/`Release`). Build the whole solution per config:

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
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

All three configs use `Microsoft.Data.Sqlite`. Build outputs land in each project's `bin\{2023|2024|2025}\Release{...}\{tfm}\`, e.g. `src\RVTuk.Revit\bin\2024\Release2024\net48\`.

`Release2023` builds only `RVTuk.Core`, `RVTuk.UI`, and `KKarea.Revit` (the Revit 2023 host); `RVTuk.Revit` does not build for 2023. The net48-vs-net8 code split (JSON serializer choice, and the native `e_sqlite3.dll` pre-load on net48) is gated on the compiler-provided `NETFRAMEWORK` symbol, not on `REVIT2024`; the `REVIT<year>` constants are for year-specific Revit API differences only (e.g. the `ElementId` shims in the shared area sources).

## Tests

Core's xunit suite lives in `tests\RVTuk.Core.Tests` (repositories, indexer, config/migration, Rishui Zamin writers/validator), mirroring the per-tool folders. Run it before committing Core changes:

```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```

On a non-Windows SDK (CI, cloud agents) add `-p:EnableWindowsTargeting=true` — the suite builds and runs fine on Linux. The WPF/Revit projects (`RVTuk.UI`, `RVTuk.Revit`, `KKarea.Revit`) can only be compiled on Windows.

## Deployment

`Deploy.ps1` (must run as Administrator) builds/copies the right binary set per Revit version, strips the net48 BCL polyfill DLLs Revit already preloads, and generates the `.addin` manifest XML:

```powershell
# From the RVTuk folder, in an elevated shell:
.\Deploy.ps1            # all versions
.\Deploy.ps1 2024       # only Revit 2024 (optional version filter)
```

Deploys to `C:\ProgramData\Autodesk\Revit\Addins\{2024|2025}\RVTuk\`. Restart Revit after deploying. Each version deploys independently: a year whose Revit is currently open (DLLs locked) or whose build output is missing is skipped with a warning while the others proceed. 2023 deploys **KKarea** (entry class `KKarea.Revit.Application`, its own stable ClientId `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC`) to `...\Addins\2023\KKarea\`; 2024/2025 deploy RVTuk as before.

`Deploy.ps1` copies the build output flat plus the native `e_sqlite3.dll` (from `runtimes\win-x64\native`). For this to work the build output must actually contain the dependency closure: net48 copies NuGet deps to `bin` automatically, but **net8 libraries do not** — so `RVTuk.Revit` (net8) sets `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to pull `Microsoft.Data.Sqlite` + `e_sqlite3.dll` into `bin`. Without it, the Revit 2025 deploy would contain only the three project DLLs and fail to load.

The `.addin` manifest registers the add-in with:
- **Entry class**: `RVTuk.Revit.Application`
- **Client ID**: `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5`
- **Vendor ID**: `KnafoKlimor`

### Installer for other computers

`Build-Installer.ps1` produces `dist\RVTukSetup.exe` — a standalone net48 console exe (`installer\RVTuk.Setup`, deliberately **not** in `RVTuk.sln` since it has no `Release{year}` configs) with the per-year payload embedded as `payload.zip`. It wipes each project's `bin\{year}` and rebuilds so stale DLLs from dropped packages can't ship, stages with the same copy/strip rules as `Deploy.ps1` (the version table and staging rules in the two scripts must stay in sync), and writes the same `.addin` manifests. On a target machine the exe self-elevates, installs only for the Revit years whose `Addins\{year}` folder exists, and skips years whose Revit is running. Flags: `--uninstall`, `--all` (ignore year detection), `--target <dir>` (redirect to another folder for testing, skips elevation), `--no-pause`.

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

Inside each project (and `tests\RVTuk.Core.Tests`) the structure is **one folder per
tool** (`FamilyBrowser/`, `RishuiZamin/`, `AutoDimensions/`, `NeoProperties/`) plus
`Shared/` for genuinely cross-tool code (`AppConfig`/`ConfigManager`, `PathUtil`, the
UI's `ViewModelBase`/`RelayCommand`/converters/theme). Namespaces follow folders
exactly (e.g. `RVTuk.Core.FamilyBrowser.Database`, `RVTuk.UI.RishuiZamin.Views`,
`RVTuk.Core.Shared.Config`). `RVTuk.Revit.Application` stays at the project root —
it is the `.addin` entry class.

**KKarea.Revit** (`src\KKarea.Revit`) is a fourth project: a standalone Revit 2023 add-in hosting only Rishui Zamin. It references Core + UI and compiles the Revit-side area sources (`AreaExtractor`, `UsageKeyScheduleBuilder`, the three area external-event handlers from `RVTuk.Revit\RishuiZamin\`) as **linked shared source** from `RVTuk.Revit` against the 2023 API — it must never reference `RVTuk.Revit` itself. `#if REVIT2023` branches in those shared files cover the 2023 API gaps (`ElementId.IntegerValue` vs `.Value`, int-only `ElementId` ctor).

**RVTuk.Core** holds data models, the SQLite schema/repositories, OLE thumbnail read/write, metadata-XML parsing, and config. Keep it free of Revit API and WPF types so it can be reasoned about in isolation. It multi-targets `net48` (Release2024) and `net8.0-windows` (Release2025) and **does** carry NuGet dependencies, which differ per target:
- net48: `Microsoft.Data.Sqlite`, GAC `System.Drawing`. No `System.Text.Json` (its transitive polyfills clash with Revit's preloaded assemblies — JSON uses `DataContractJsonSerializer`).
- net8: `Microsoft.Data.Sqlite`, `System.Text.Json`, `System.Drawing.Common`.
- both: `OpenMCDF` pinned to `3.1.2` (matches the version other Revit add-ins preload).

**SQLite provider:** all configs use `Microsoft.Data.Sqlite`. `System.Data.SQLite` was dropped because its native win32 VFS **cannot open databases over some UNC shares** (`\\server\share`) — it throws `unable to open database file` even when the file is readable, while `Microsoft.Data.Sqlite`'s bundled `e_sqlite3` opens the same file fine. The repositories alias `SQLiteConnection`/`SQLiteCommand` to the `Microsoft.Data.Sqlite` types. `FamilyBrowser/Database/SqliteNative.EnsureLoaded()` (called by every repository ctor) pre-loads `e_sqlite3.dll` by full path on net48, because Revit resolves native libs relative to `Revit.exe`, not the add-in folder; `Deploy.ps1` copies `e_sqlite3.dll` flat into the add-in folder. Keep SQL portable (e.g. one statement per `ExecuteScalar`).

**RVTuk.UI** multi-targets the same frameworks, uses WPF (`UseWPF`) and WinForms (`UseWindowsForms`), and contains all user-facing windows/controls. Depends on Core only — it must not reference any Revit type. Revit interactions are passed in as plain `Func<>`/`Action` delegates from the Revit project.

**RVTuk.Revit** is the only project that references the Revit API (`Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`, pinned `2024.*` / `2025.*`, `compile`-only with `ExcludeAssets="runtime"`). It hosts the ribbon, commands, and the `ExternalEvent` handlers, and wires UI delegates to that API.

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
