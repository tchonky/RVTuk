# 🏗️ RVTuk

A Revit add-in toolkit for **Knafo Klimor Architects LTD**: one ribbon, one installer, and a growing set
of tools for the family library, building-permit submissions and the repetitive parts of drafting.
RVTuk runs in Revit 2024–2027; its Area Calc tool also ships on its own for Revit 2023, as **KKarea**.

## 🧰 Tools

| Ribbon button | What it does |
|---|---|
| **[Family Browser](docs/tools/family-browser/README.md)** | Search, browse and load the firm's family library from a dark-themed side panel, with thumbnails, parameters, tags, favourites and per-family how-to notes. One maintainer curates the shared library on Autodesk Docs; everyone else browses a verified local copy. |
| **[Area Calc](docs/tools/rishui-zamin/README.md)** | Exports the Areas on the open sheet as the paired `.dxf` + `.dat` files that the area-calculation robot of Israel's online permit system (רישוי זמין) expects, and sets up the usage-key parameters and schedules it needs. |
| **[DWG Export](docs/tools/dwg-exporter/README.md)** | Batch-exports a view or sheet set to DWG, and optionally PDF, named by the same rules as the project's PDF export setups. One run can span several open models and zip the DWGs with the images, xrefs and fonts they need. |
| **[Auto Dimensions](docs/tools/auto-dimensions/README.md)** | Draw a line on a `_DP-Dim` line style and every wall, door and window it crosses gets dimensioned, in linked models too. Choose the views per level in its pane, and re-run after the model changes without picking references again. |
| **[Topo Tools](docs/tools/topo-tools/README.md)** | An edit session on a toposolid or floor: draw lines, type each line's height, and the surface gets points along them. Move, edit or delete lines and update again. |
| **Floor ⇄ Topo** | Turns the selected floors into toposolids and toposolids into floors, keeping the outline, the surface points and the parameters. |
| **[Room Floor](docs/tools/room-floors/README.md)** | Click inside rooms to give each one a finish floor that follows its finish faces, with columns as holes, like Automatic Ceiling. Click a room again to replace its floor, keeping the type and offset. |
| **[Round Positions](docs/tools/round-positions/README.md)** | Finds walls that sit off a whole- or half-centimetre grid and moves them back onto it in one undo step, never changing a length or an angle. Walls only so far; more categories are designed. |

**[Neo Properties](docs/tools/neo-properties/README.md)**, a Properties palette with your pinned
parameters first, is built but hidden until release.

## 🧩 Supported Revit Versions

| Configuration  | Revit Version | Target Framework | Add-in |
|---------------|--------------|-----------------|--------|
| Release2023   | 2023         | net48           | KKarea (Area Calc only) |
| Release2024   | 2024         | net48           | RVTuk |
| Release2025   | 2025         | net8.0-windows  | RVTuk |
| Release2026   | 2026         | net8.0-windows  | RVTuk |
| Release2027   | 2027         | net10.0-windows | RVTuk |

## 📦 Installing

Build the standalone installer once, then hand out the exe:

```powershell
.\Build-Installer.ps1        # produces dist\RVTukSetup.exe
```

On the target computer just double-click `RVTukSetup.exe` — it asks for admin rights, finds which Revit versions are installed (by their `Revit.exe`; 2023 gets KKarea; 2024, 2025, 2026 and 2027 get RVTuk), and installs for each of them. A year is skipped, with nothing in it changed, when its Revit is open or any file of the add-in is in use; the exe then exits 1 — close Revit and re-run. Until releases exist every package is a dev build and always replaces what is installed. No other software is needed on the target machine.

Flags: `--install <names|all>` (the default: all), `--remove <names|all>`, `--uninstall` (= `--remove all`), `--years <y,y>` (these Revit years instead of the detected ones), `--source <dir>` (a payload folder instead of the embedded one), `--target <dir>` (install under another folder for testing, no admin rights), `--quiet`, `--no-pause`.

## 🔨 Building and Testing

Requires the .NET 10 SDK (pinned in `global.json`). Revit isn't needed to build: the API comes from
compile-only NuGet packages. The solution has five configurations, `Release2023` to `Release2027`, and
no Debug/Release, so always pass `-c`:

```powershell
dotnet build RVTuk.sln -c Release2025
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024
.\build\Test-All.ps1   # all tests: net48, net8, net10
```

## 🚀 Deploying to This Machine

Run as Administrator from the repo root:

```powershell
.\Deploy.ps1                # every add-in, every Revit year found on the PC
.\Deploy.ps1 2024           # only Revit 2024
.\Deploy.ps1 2024 2027      # only Revit 2024 and 2027
.\Deploy.ps1 -AddIn KKarea  # only KKarea
.\Deploy.ps1 -Clean 2025    # remove the add-ins from Revit 2025
```

Stages the add-ins from the working tree with `build\Staging.ps1` and hands them to the setup exe's install engine, which copies them to `C:\ProgramData\Autodesk\Revit\Addins\{year}\{RVTuk|KKarea}\` (Revit 2027: `C:\Program Files\Autodesk\Revit\Addins\2027\RVTuk\`) and writes the `.addin` manifests from the catalog. Only Revit years found on the PC are written to, unless named. A year whose Revit is open or whose files are in use is skipped and left exactly as it was, and the script exits 1. `-Clean` removes. Restart Revit after deploying.

## 📁 Project Structure

```
RVTuk/
├── src/
│   ├── RVTuk.Core/          # Business logic per tool + Shared (SQLite, config) — no Revit/WPF deps
│   ├── RVTuk.UI/            # WPF windows and view models, one folder per tool + Shared
│   ├── RVTuk.Revit/         # Revit add-in host (ribbon, external events, commands), per-tool folders
│   └── KKarea.Revit/        # Standalone Revit 2023 host for Rishui Zamin (Area Calc) only
├── tests/
│   ├── RVTuk.Core.Tests/    # xunit suite for Core, mirrors the per-tool folders
│   └── RVTuk.UI.Tests/      # WPF tests without Revit
├── installer/
│   ├── RVTuk.Setup/         # Standalone installer exe (built by Build-Installer.ps1)
│   └── RVTuk.Setup.Tests/   # xunit tests for the installer engine and command line
├── docs/
│   ├── tools/<tool>/        # Per-tool README, backlog, specs, plans
│   └── toolkit/             # Toolkit-wide specs and plans
├── build/                         # Test-All.ps1, Staging.ps1, Test-Staging.ps1
├── Deploy.ps1                     # Build + deploy to Revit add-ins directory (dev machine)
├── Build-Installer.ps1            # Build dist\RVTukSetup.exe for other computers
└── CLAUDE.md                      # AI assistant instructions
```

## 🤖 How It's Built

RVTuk is developed by [Daniel Polistchuck](https://github.com/tchonky), the firm's BIM lead, working
with Claude Code. [`CLAUDE.md`](CLAUDE.md) is the technical reference (architecture, threading model,
build matrix, deployment), [`VISION.md`](VISION.md) holds the product vision and roadmap, and every tool
keeps its README, backlog, design specs and plans in `docs/tools/<tool>/`. The Core layer has no Revit
or WPF dependency, so its tests run without Revit, on .NET Framework 4.8, .NET 8 and .NET 10.

## ✨ Family Browser Features

- 🌙 Dark theme matching Revit's UI
- 🔍 Search and filter by category
- 🔄 **Sync** — fast filesystem scan: adds new families, removes deleted ones, checks project version status
- ⚙️ **Settings** — configure the library root folder and launch a deep scan
- 🧠 **Deep Scan** — extracts full metadata from every family using the Revit engine; run after adding many new families
- 📦 Load or update families directly into the active Revit project
- 📝 Per-family instructions editor (rich text)
- 📋 Parameter table viewer

## 📄 License

[GPL-3.0](LICENSE)
