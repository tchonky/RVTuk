# RVTuk

A Revit add-in toolkit for Knafo Klimor Architects LTD. Currently includes:

- **Family Browser** — search, browse, load, and manage your Revit family library from a dark-themed side panel. Syncs the library database on demand and supports deep-scan indexing (category, parameters, thumbnail) using the Revit engine.

## Supported Revit Versions

| Configuration  | Revit Version | Target Framework |
|---------------|--------------|-----------------|
| Release2023   | 2023         | net48           |
| Release2024   | 2024         | net48           |
| Release2025   | 2025         | net8.0-windows  |

## Project Structure

```
RVTuk/
├── src/
│   ├── RVTuk.Core/          # Business logic, database (SQLite), config — no Revit/WPF deps
│   ├── LibraryBrowser/
│   │   └── RVTuk.UI/        # WPF windows and view models
│   └── RVTuk.Revit/         # Revit add-in host (ribbon, external events, commands)
├── Deploy.ps1                     # Build + deploy to Revit add-ins directory
└── CLAUDE.md                      # AI assistant instructions
```

## Building

```powershell
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2023
```

## Deploying

Run as Administrator from the repo root:

```powershell
.\Deploy.ps1
```

Copies DLLs to `C:\ProgramData\Autodesk\Revit\Addins\{2023|2024|2025}\RVTuk\` and writes the `.addin` manifest. Restart Revit after deploying.

## Family Browser Features

- Dark theme matching Revit's UI
- Search and filter by category
- **Sync** (🔄) — fast filesystem scan: adds new families, removes deleted ones, checks project version status
- **Settings** (⚙) — configure the library root folder and launch a deep scan
- **Deep Scan** — extracts full metadata from every family using the Revit engine; run after adding many new families
- Load or update families directly into the active Revit project
- Per-family instructions editor (rich text)
- Parameter table viewer
