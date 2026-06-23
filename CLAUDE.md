# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReviTchucky is a Revit add-in for Knafo Klimor Architects LTD. It supports Revit 2023, 2024, and 2025 simultaneously via separate build configurations. It currently provides two features:

- **Family Library Indexer** — scans a folder of `.rfa` files, extracts metadata (category, parameters, thumbnails) via the Revit API, and stores it in a shared SQLite database.
- **Family Browser** — a searchable/filterable window over that index, with per-family rich-text instructions and custom thumbnails, plus "load/update family into the active project".

## Build

A solution file (`ReviTchucky.sln`) is present with three solution configurations — `Release2023`, `Release2024`, `Release2025` (there is no standard `Debug`/`Release`). Build the whole solution per config:

```powershell
dotnet build ReviTchucky.sln -c Release2023
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```

Or build a single project (its project references are built transitively). The projects live under `src\`:

```powershell
dotnet build src\ReviTchucky.Revit\ReviTchucky.Revit.csproj -c Release2024
```

Each config maps to a target framework and a `DefineConstants` symbol that switches the SQLite provider (see Architecture):

| Config        | TFM               | Constant    | SQLite provider           |
|---------------|-------------------|-------------|---------------------------|
| `Release2023` | `net48`           | `REVIT2024` | `System.Data.SQLite`      |
| `Release2024` | `net48`           | `REVIT2024` | `System.Data.SQLite`      |
| `Release2025` | `net8.0-windows`  | `REVIT2025` | `Microsoft.Data.Sqlite`   |

(Release2023 intentionally reuses the `REVIT2024` symbol — both are the net48 code path.) Build outputs land in each project's `bin\{2023|2024|2025}\Release{...}\{tfm}\`, e.g. `src\ReviTchucky.Revit\bin\2024\Release2024\net48\`.

## Deployment

`Deploy.ps1` (must run as Administrator) builds/copies the right binary set per Revit version, strips the net48 BCL polyfill DLLs Revit already preloads, and generates the `.addin` manifest XML:

```powershell
# From the ReviTchucky folder, in an elevated shell:
.\Deploy.ps1            # all versions
.\Deploy.ps1 2024       # only Revit 2024 (optional version filter)
```

Deploys to `C:\ProgramData\Autodesk\Revit\Addins\{2023|2024|2025}\ReviTchucky\`. Restart Revit after deploying. Each version deploys independently: a year whose Revit is currently open (DLLs locked) or whose build output is missing is skipped with a warning while the others proceed.

The `.addin` manifest registers the add-in with:
- **Entry class**: `ReviTchucky.Revit.Application`
- **Client ID**: `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5`
- **Vendor ID**: `KnafoKlimor`

## Architecture

Three projects with a strict dependency order (no circular references):

```
ReviTchucky.Core        — pure business logic, no Revit or UI dependency
       ↑                  src\ReviTchucky.Core
ReviTchucky.UI          — WPF dialogs/views (MVVM), depends on Core only
       ↑                  src\LibraryBrowser\ReviTchucky.UI
ReviTchucky.Revit       — Revit add-in host: IExternalApplication entry point,
                          ribbon setup, external-event handlers; depends on Core + UI
                          src\ReviTchucky.Revit
```

**ReviTchucky.Core** holds data models, the SQLite schema/repositories, OLE thumbnail read/write, metadata-XML parsing, and config. Keep it free of Revit API and WPF types so it can be reasoned about in isolation. It multi-targets `net48` (Release2023/2024) and `net8.0-windows` (Release2025) and **does** carry NuGet dependencies, which differ per target:
- net48: `System.Data.SQLite` (full package — needed for the native `SQLite.Interop.dll`), GAC `System.Drawing`. No `System.Text.Json` (its transitive polyfills clash with Revit's preloaded assemblies — JSON uses `DataContractJsonSerializer`).
- net8: `Microsoft.Data.Sqlite`, `System.Text.Json`, `System.Drawing.Common`.
- both: `OpenMCDF` pinned to `3.1.2` (matches the version other Revit add-ins preload).

Provider differences are bridged with `#if REVIT2024` and `SQLiteConnection`/`SQLiteCommand` aliases in the `Database` classes — keep SQL portable across both providers (e.g. one statement per `ExecuteScalar`).

**ReviTchucky.UI** multi-targets the same frameworks, uses WPF (`UseWPF`) and WinForms (`UseWindowsForms`), and contains all user-facing windows/controls. Depends on Core only — it must not reference any Revit type. Revit interactions are passed in as plain `Func<>`/`Action` delegates from the Revit project.

**ReviTchucky.Revit** is the only project that references the Revit API (`Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`, pinned `2023.*` / `2024.*` / `2025.*`, `compile`-only with `ExcludeAssets="runtime"`). It hosts the ribbon, commands, and the `ExternalEvent` handlers, and wires UI delegates to that API.

## Threading Model

- Revit API calls **must** run on Revit's main thread, marshaled via `ExternalEvent` + a `ManualResetEventSlim` ping-pong (the handlers in `ReviTchucky.Revit`). Never call the Revit API from a background thread.
- Background work (file I/O, SQLite, OLE parsing, scans) runs on the `ThreadPool`.
- WPF property updates from background threads go through the `Dispatcher`.
- The load/update handlers are shared singletons and `ExternalEvent.Raise()` coalesces, so serialize concurrent loads (the Family Browser does this with a lock) — don't fire several raises at once.

## Revit Add-in Notes

- The Revit API NuGet packages (Nice3point wrappers) are compile-time references only; the actual DLLs come from the Revit installation at runtime. Do not copy Revit API DLLs into the output.
- Changes to the `ClientId` GUID in `Deploy.ps1` will break existing installations — it must stay stable.
- The `Manifests/` folder is reserved for hand-edited `.addin` files if needed; `Deploy.ps1` generates them dynamically.
- Dates are persisted as UTC ISO-8601 (`DateTime.ToString("o")`); read them back through `DbConvert.ParseUtc` so the instant is preserved regardless of the machine's local time zone.
- Relative-path DB keys go through `PathUtil.GetRelativePath` (no framework-conditional logic) so net48 and net8 produce identical keys for the shared database.
