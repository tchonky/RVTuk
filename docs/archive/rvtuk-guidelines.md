# RVTuk Development Guidelines

> **⚠️ ARCHIVED — historical reference only.** This is the original Phase-1 planning/spec
> document. Some class names and the proposed UI here predate the implementation, and the
> build/deploy specifics below have since changed. Kept for history; **do not treat as current.**
> For the live picture see [`../../VISION.md`](../../VISION.md) (product) and
> [`../../CLAUDE.md`](../../CLAUDE.md) (authoritative technical reference).

## Project Overview
RVTuk is a Revit add-in toolkit for family management. Phase 1 focuses on building a Family Library Indexer that scans a folder of .rfa files, extracts metadata (category, parameters, thumbnails), and stores it in a shared SQLite database.

## Architecture Principles

### Three-Project Structure
- **RVTuk.Core**: Plain .NET class library. Zero Revit API references. Contains data models, SQLite schema, OLE document operations, metadata extraction logic, and configuration management. This is the reusable engine.
- **RVTuk.UI**: WPF class library with MVVM pattern. Zero Revit API references. Contains Views (SettingsWindow, IndexProgressWindow) and ViewModels. Modeless windows that can be hosted either in a dockable pane or floated independently.
- **RVTuk.Revit**: The actual Revit add-in. Implements `IExternalApplication`. Owns the ribbon, external commands, and external event handlers. Acts as the bridge between UI and Revit API. Never calls Revit API from background threads.

### Multi-Targeting
- Target `net48` for Revit 2023 and 2024 (uses Nice3point.Revit.Api for those binaries)
- Target `net8.0-windows` for Revit 2025+ (uses Nice3point.Revit.Api for 2025 binaries)
- Use build configurations `Release2023`, `Release2024`, and `Release2025` to control the target framework and which NuGet package versions are pulled
- `Deploy.ps1` handles copying the right binary set to `C:\ProgramData\Autodesk\Revit\Addins\<version>\RVTuk\`

### Threading Model
- **Background threads are safe for:** file I/O, SQLite reads/writes, OLE document parsing, thumbnail extraction, long-running scans
- **Revit API calls MUST happen on Revit's thread**, guarded by `ExternalEvent`. Never call Revit API (including `ExtractPartAtomFromFamilyFile`) directly from a background thread.
- Pattern: background thread collects work items → raises `ExternalEvent` → Revit's thread executes the API call → callback updates UI via dispatcher

### Configuration & Persistence
- Settings stored in JSON at `%AppData%\Autodesk\Revit\Addins\RVTuk\config.json`
- Shared by all Revit versions on that machine (e.g., 2024 and 2025 both read/write the same config)
- `ConfigManager` class handles all file I/O for config
- On first run, config does not exist; SettingsWindow must validate and create it

## Phase 1: Family Library Indexer

### Goals
1. User configures library folder path and index database location via Settings dialog
2. "Index Library" button scans the library folder recursively, extracts family metadata, and populates SQLite
3. Incremental indexing: only re-extract families whose file size or modified date has changed
4. Optional "Rebuild Index" to wipe the database and re-index from scratch (disaster recovery)
5. Progress dialog shows real-time feedback during indexing

### SQLite Schema
Create three tables in the index database:

```sql
CREATE TABLE Families (
    Id INTEGER PRIMARY KEY,
    RelativePath TEXT UNIQUE NOT NULL,
    FileName TEXT NOT NULL,
    ModifiedDate DATETIME NOT NULL,
    FileSize INTEGER NOT NULL,
    Category TEXT,
    IndexedDate DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE Parameters (
    Id INTEGER PRIMARY KEY,
    FamilyId INTEGER NOT NULL,
    ParameterName TEXT NOT NULL,
    DataType TEXT NOT NULL,
    IsInstance INTEGER NOT NULL,
    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
);

CREATE TABLE Thumbnail (
    Id INTEGER PRIMARY KEY,
    FamilyId INTEGER UNIQUE NOT NULL,
    PngData BLOB NOT NULL,
    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
);
```

### Core Classes (RVTuk.Core)

#### ConfigManager
- Static methods: `LoadConfig()`, `SaveConfig()`
- Properties: `LibraryFolderPath`, `IndexDatabasePath`
- Validation: ensure paths exist before saving
- File location: `%AppData%\Autodesk\Revit\Addins\RVTuk\config.json`

#### IndexRepository
- Constructor: takes database file path
- Methods:
  - `GetOrCreateFamily(relativePath, fileName, modifiedDate, fileSize) -> Family`
  - `UpdateFamily(familyId, category, parameters, thumbnailPng)`
  - `DeleteFamily(familyId)`
  - `GetFamilyByPath(relativePath) -> Family` (returns null if not indexed)
  - `GetAllFamilies() -> List<Family>`
  - `DeleteStaleEntries(List<string> validPaths)` — remove any families whose path is no longer on disk
  - `RebuildIndex()` — clears all tables and returns a fresh database

#### ThumbnailExtractor
- Static method: `ExtractThumbnailFromRfa(rfa_file_path) -> byte[] (PNG)` or `null` if no thumbnail
- Uses OleDocumentOpener (OleCompoundDocument library from NuGet) to read the .rfa as an OLE storage
- .rfa files embed a preview image; extract it as PNG bytes
- No Revit API needed; purely file I/O

#### FamilyMetadataExtractor
- Constructor: takes `Application` (Revit API object)
- Method: `ExtractMetadata(rfa_file_path) -> (category: string, parameters: List<Parameter>)`
- Calls Revit API's `Application.ExtractPartAtomFromFamilyFile()` to get an XML atom
- Parses the XML to extract category name and parameter list (name, data type, instance vs. type)
- **Important:** This method will be called from Revit's thread via ExternalEvent. Do not call it from a background thread.

#### FamilyIndexer
- Constructor: takes `IndexRepository`, `ThumbnailExtractor`, library folder path
- Method: `ScanLibrary(progressCallback) -> IndexSummary`
  - `progressCallback` is an `Action<string, int, int>` called for each file: `progressCallback(fileName, currentCount, totalCount)`
  - Returns `IndexSummary { TotalScanned, Updated, Skipped }`
- Logic:
  1. Recursively find all .rfa files in library folder
  2. For each file:
     - Compute relative path from library root
     - Check if it's in the index (by relative path)
     - If yes: compare file size and modified date
       - If both match: increment Skipped, continue
       - If either differs: mark for extraction
     - If no: mark for extraction
  3. For files marked for extraction:
     - Extract thumbnail (OLE, no Revit API)
     - Add to extraction queue (to be processed by Revit on its thread)
  4. Return summary

#### FamilyIndexerRvtBridge (new, handles Revit API calls)
- Constructor: takes `Application`, `FamilyMetadataExtractor`
- Method: `ExtractAndIndexFamily(rfa_path, IndexRepository, progressCallback)`
  - Calls `ExtractPartAtomFromFamilyFile()` on Revit's thread
  - Calls `IndexRepository.UpdateFamily()` with the results
  - Updates the callback with progress
- **Must be called only from an ExternalEvent handler**

### UI Classes (RVTuk.UI)

#### SettingsWindow
- XAML: two TextBox inputs (Library Folder Path, Index Database Path), two Browse buttons, Save/Cancel buttons
- ViewModel: `SettingsViewModel`
  - Properties: `LibraryFolderPath`, `IndexDatabasePath` (bindable)
  - Methods: `BrowseLibraryFolder()`, `BrowseIndexDatabase()`, `Save()`, `Cancel()`
  - Validation: check that paths exist and are writable before allowing Save
- On Save: calls `ConfigManager.SaveConfig()`
- On Cancel: closes without saving

#### IndexProgressWindow
- XAML: progress bar, label showing "Processing family N of M", elapsed time display, Cancel button, Rebuild Index button
- ViewModel: `IndexProgressViewModel`
  - Properties: `ProgressValue`, `CurrentFileName`, `CurrentCount`, `TotalCount`, `ElapsedTime`
  - Methods: `Cancel()`, `RebuildIndex()`
- Progress callback from `FamilyIndexer` updates these properties via Dispatcher
- Rebuild Index button: clears the database and re-indexes everything from scratch
- When indexing completes, show a TaskDialog with summary and close the window

### Revit Add-In (RVTuk.Revit)

#### External Application & Ribbon
- `IExternalApplication.OnStartup()`: creates ribbon tab "RVTuk" with two buttons
  - Button 1: "Settings" → opens `SettingsWindow`
  - Button 2: "Index Library" → validates settings, then shows `IndexProgressWindow` and begins indexing
- Button icons: use placeholder icons for now (simple PNG or SVG); upgrade later

#### Settings Command
- If `config.json` doesn't exist or settings are empty, open `SettingsWindow` and require the user to fill them
- If settings exist, open `SettingsWindow` pre-populated with current values (allow override)

#### Index Library Command
- Check if settings are configured; if not, show `SettingsWindow` first (block indexing)
- Once settings are valid, show `IndexProgressWindow`
- Spawn a background thread that:
  1. Calls `FamilyIndexer.ScanLibrary()` to identify which files need extraction
  2. For each file needing extraction, raise an `ExternalEvent` to call `FamilyMetadataExtractor.ExtractMetadata()`
  3. Collect results and update the index
  4. On completion, raise a final `ExternalEvent` to show the summary TaskDialog and close the progress window
- The user can cancel mid-indexing; gracefully stop the background thread

#### ExternalEvent Handlers
- `IndexingExternalEventHandler`: executes `FamilyMetadataExtractor.ExtractMetadata()` on Revit's thread
  - Raised by the background indexing thread
  - Updates progress via callback

## Testing Checklist for Phase 1

- [ ] ConfigManager creates config.json at the right location
- [ ] ConfigManager reads/writes library path and index path correctly
- [ ] SettingsWindow opens and allows browsing folders/files
- [ ] SettingsWindow validates paths (must exist and be writable)
- [ ] SettingsWindow saves to config.json and closes
- [ ] SQLite database creates with correct schema
- [ ] IndexRepository CRUD operations work (insert, read, update, delete)
- [ ] ThumbnailExtractor successfully extracts PNG from a test .rfa file
- [ ] FamilyIndexer scans the test library folder and identifies files correctly
- [ ] FamilyIndexer detects file changes by size + modified date
- [ ] IndexProgressWindow displays and updates during indexing
- [ ] Progress callback fires correctly and updates UI
- [ ] Rebuild Index button wipes and re-indexes the database
- [ ] Indexing can be cancelled mid-process
- [ ] Completion summary shows accurate counts (Total, Updated, Skipped)
- [ ] Ribbon buttons appear in Revit 2024
- [ ] Ribbon buttons appear in Revit 2025 (if available)
- [ ] Add-in loads without errors in the Revit journal file

## Common Pitfalls to Avoid

1. **Calling Revit API from a background thread** → causes crash or unpredictable behavior. Always use `ExternalEvent`.
2. **SQLite blocking on network shares** → use WAL mode and keep transactions short
3. **Config file not created on first run** → validate and create in `ConfigManager.LoadConfig()` if missing
4. **Thumbnail extraction failing silently** → wrap in try/catch, log failures, continue indexing (families without thumbnails are still valid)
5. **Progress callback not dispatched to UI thread** → use `Application.Current.Dispatcher.Invoke()` to update WPF properties from background threads
6. **Mixing relative and absolute paths** → store relative in the index, use absolute for file I/O; always convert between them explicitly

## Build & Deployment

- Run `dotnet build RVTuk.sln -c Release2023|Release2024|Release2025` to build a given target
- Run `Deploy.ps1` (as Administrator) to copy binaries and manifests to `C:\ProgramData\Autodesk\Revit\Addins\`
- Restart Revit
- Check the ribbon for the "RVTuk" tab and "Settings" / "Index Library" buttons

## Resources

- Revit API Docs: https://www.revitapidocs.com/
- Building Coder Blog: https://thebuildingcoder.typepad.com/ (search for threading and ExternalEvent)
- Nice3point Revit Packages: https://github.com/nice3point/RevitApi
- OLE Compound Document parsing: OpenMCDF NuGet package
