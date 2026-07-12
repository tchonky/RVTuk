# 🏗️ RVTuk

A Revit add-in toolkit for **Knafo Klimor Architects LTD**. One ribbon, one install, a
growing set of tools for managing the family library and automating repetitive work.

- 🗂️ **Family Browser** — search, browse, load, and manage your Revit family library from a dark-themed side panel. Syncs the library database on demand and supports deep-scan indexing (category, parameters, thumbnail) using the Revit engine.
- 📐 **Area Calc** (Rishui Zamin) — reads the Areas on the open sheet and exports the paired `.dxf` + `.dat` files the רישוי זמין area-calculation robot expects.

(The Project Comparator moved to its own separate project. Auto Dimensions and Neo
Properties exist in the codebase but are hidden until release.)

## 🧩 Supported Revit Versions

| Configuration  | Revit Version | Target Framework | Add-in |
|---------------|--------------|-----------------|--------|
| Release2024   | 2024         | net48           | RVTuk |
| Release2025   | 2025         | net8.0-windows  | RVTuk |
| Release2023   | 2023         | net48           | KKarea (Area Calc only) |

## 📁 Project Structure

```
RVTuk/
├── src/
│   ├── RVTuk.Core/          # Business logic, database (SQLite), config — no Revit/WPF deps
│   ├── LibraryBrowser/
│   │   └── RVTuk.UI/        # WPF windows and view models
│   ├── RVTuk.Revit/         # Revit add-in host (ribbon, external events, commands)
│   └── KKarea.Revit/        # Standalone Revit 2023 host for Area Calc only
├── tests/
│   └── RVTuk.Core.Tests/    # xunit suite for Core
├── installer/
│   └── RVTuk.Setup/         # Standalone installer exe (built by Build-Installer.ps1)
├── Deploy.ps1                     # Build + deploy to Revit add-ins directory (dev machine)
├── Build-Installer.ps1            # Build dist\RVTukSetup.exe for other computers
└── CLAUDE.md                      # AI assistant instructions
```

## 🔨 Building

```powershell
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024
```

## 🚀 Deploying

Run as Administrator from the repo root:

```powershell
.\Deploy.ps1
```

Copies DLLs to `C:\ProgramData\Autodesk\Revit\Addins\{2024|2025}\RVTuk\` and writes the `.addin` manifest. Restart Revit after deploying.

## 📦 Installing on other computers

Build the standalone installer once, then hand out the exe:

```powershell
.\Build-Installer.ps1        # produces dist\RVTukSetup.exe
```

On the target computer just double-click `RVTukSetup.exe` — it asks for admin rights, detects which Revit versions are installed (2023 gets KKarea, 2024/2025 get RVTuk), and installs for each of them. Skips any Revit that is currently running; close it and re-run. `RVTukSetup.exe --uninstall` removes the add-ins again. No other software is needed on the target machine.

## ✨ Family Browser Features

- 🌙 Dark theme matching Revit's UI
- 🔍 Search and filter by category
- 🔄 **Sync** — fast filesystem scan: adds new families, removes deleted ones, checks project version status
- ⚙️ **Settings** — configure the library root folder and launch a deep scan
- 🧠 **Deep Scan** — extracts full metadata from every family using the Revit engine; run after adding many new families
- 📦 Load or update families directly into the active Revit project
- 📝 Per-family instructions editor (rich text)
- 📋 Parameter table viewer
