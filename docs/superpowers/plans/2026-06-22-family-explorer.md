# Family Explorer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evolve the existing Family Browser into a richer "Family Explorer": a per-family image gallery with captions, a complete parameter audit (group + System/Shared/Family), and a database that lives safely on a shared network path for many read-only users.

**Architecture:** Three additive phases on the existing 3-project structure (Core → UI → Revit). Phase 1 reworks the SQLite connection model for 1-writer/many-readers over a network share (read-only browse connection + transient write connections, rollback journal instead of WAL). Phase 2 swaps the lightweight PartAtom metadata extraction for a `FamilyManager` document-open path that yields parameter group + kind. Phase 3 adds a hybrid image gallery (files on disk, metadata in DB). Phases 2 and 3 build on Phase 1's connection model.

**Tech Stack:** C# / WPF (MVVM) / SQLite (System.Data.SQLite on net48 for Revit 2023–2024; Microsoft.Data.Sqlite on net8 for Revit 2025) / Revit API 2023–2025 / System.Drawing.

## Global Constraints

- **Git (added during execution).** The repo was `git init`'d on branch `family-explorer`. Each task ends with its build-verification checkpoint **and a commit** (`git add -A && git commit`). The `.Setup/` data folder and `.superpowers/` scratch are git-ignored.
- **Multi-target both provider branches.** All `Database/` changes must compile under both `#if REVIT2024` (System.Data.SQLite, net48) and the `#else` (Microsoft.Data.Sqlite, net8) branches. Verify by building **both** `Release2024` and `Release2025`.
- **Keep `ReviTchucky.Core` and `ReviTchucky.UI` free of Revit API types.** Only `ReviTchucky.Revit` references the Revit API.
- **Revit API calls run only on Revit's main thread** (inside an `IExternalEventHandler.Execute`). Never call the Revit API from a background/ThreadPool thread.
- **Dates** persist as UTC ISO-8601 (`DateTime.ToString("o")`); read back via `DbConvert.ParseUtc`.
- **Build command** (run from the `ReviTchucky` folder, i.e. `D:\User\OneDrive - Knafo Klimor Architects LTD\Coding\ReviTchucky`):
  `dotnet build ReviTchucky.sln -c Release2024` (and `-c Release2025`, `-c Release2023`).
- **SQL must stay portable** across both providers (one statement per `ExecuteScalar`; named params via the existing `AddParam` helper).

## File Structure

Created:
- `src/ReviTchucky.Core/Models/FamilyImage.cs` — gallery image metadata row (Phase 3).
- `src/LibraryBrowser/ReviTchucky.UI/ViewModels/GalleryItemViewModel.cs` — one gallery image, lazy file→BitmapSource (Phase 3).

Modified:
- `src/ReviTchucky.Core/Database/IndexRepository.cs` — drop UNC block, rollback journal + busy_timeout, param columns in schema/migration/write (Phases 1–2).
- `src/ReviTchucky.Core/Database/BrowserRepository.cs` — read-only read connection + transient write connection, param columns, gallery CRUD (Phases 1–3).
- `src/ReviTchucky.Core/Models/ParameterModel.cs` — add `ParamGroup`, `Kind`, `Guid`, `Formula` (Phase 2).
- `src/ReviTchucky.Revit/Extraction/FamilyMetadataExtractor.cs` — FamilyManager document-open extraction (Phase 2).
- `src/LibraryBrowser/ReviTchucky.UI/ViewModels/FamilyBrowserViewModel.cs` — parameter filter, gallery binding (Phases 2–3).
- `src/LibraryBrowser/ReviTchucky.UI/Views/FamilyBrowserWindow.xaml` — param columns + filter box, gallery region (Phases 2–3).
- `src/LibraryBrowser/ReviTchucky.UI/ViewModels/InstructionsEditorViewModel.cs` — gallery management commands (Phase 3).
- `src/LibraryBrowser/ReviTchucky.UI/Views/InstructionsEditorWindow.xaml` (+ `.xaml.cs`) — gallery management UI (Phase 3).

---

## PHASE 1 — Network share + concurrency (1 writer / many readers)

Goal: DB can live at `\\server\share\…`, read-only for browsing, written only via short-lived connections, using a rollback journal that is safe across machines.

### Task 1: IndexRepository — allow UNC, rollback journal, busy_timeout

**Files:**
- Modify: `src/ReviTchucky.Core/Database/IndexRepository.cs`

**Interfaces:**
- Produces: unchanged public API; `IndexRepository(string databasePath)` now accepts `\\…` paths and opens with `journal_mode=DELETE` + `busy_timeout=5000`.

- [ ] **Step 1: Remove the UNC guard**

Delete this block (currently lines ~28–32):

```csharp
            if (databasePath.StartsWith(@"\\"))
                throw new ArgumentException(
                    $"IndexDatabasePath cannot be a UNC network path: \"{databasePath}\". " +
                    "SQLite requires a local path or a mapped drive letter (e.g. Z:\\Revit\\.Setup\\family-index.db). " +
                    "Open Settings and choose a local path.");
```

- [ ] **Step 2: Switch journal mode off WAL and add busy_timeout**

Replace the PRAGMA block in the constructor (currently lines ~51–53):

```csharp
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
```

with:

```csharp
            // WAL is NOT safe across machines on a network filesystem (it relies on host-local
            // shared memory). Use a rollback journal so the DB can live on \\server\share.
            // busy_timeout lets brief writes wait for a lock instead of failing immediately.
            Execute("PRAGMA busy_timeout=5000;");
            Execute("PRAGMA journal_mode=DELETE;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
```

- [ ] **Step 3: Build Core (both provider branches)**

Run from the `ReviTchucky` folder:
```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Checkpoint** — confirm both builds are clean before moving on.

---

### Task 2: BrowserRepository — read-only reads, transient writes

Today `BrowserRepository` holds one read-write connection used for both reads and writes (Sync upserts/deletes, editor saves). Split it: a persistent **read-only** connection for `Get*`, and a transient **read-write** connection per write. The constructor still ensures schema (and migrates the journal off WAL) via a one-time write connection, which also handles first-run DB creation.

**Files:**
- Modify: `src/ReviTchucky.Core/Database/BrowserRepository.cs`

**Interfaces:**
- Consumes: `databasePath` (may be `\\…`).
- Produces: all existing public methods keep their signatures. Reads use the RO connection; writes (`UpsertFamily`, `DeleteStaleEntries`, `SaveInstructionsXaml`, `SaveCustomThumbnail`, `DeleteCustomThumbnail`, `SetOleSynced`) route through a new private `WithWrite(Action<SQLiteConnection>)` helper.

- [ ] **Step 1: Replace the constructor + add connection helpers**

Replace the constructor (currently lines ~41–58) and the `EnsureSchema`/`Execute` region with this. Keep the `static BrowserRepository()` interop loader and the `#if REVIT2024` using-aliases above it unchanged.

```csharp
        private readonly string _databasePath;
        private readonly SQLiteConnection _connection; // persistent READ-ONLY connection

        public BrowserRepository(string databasePath)
        {
            _databasePath = databasePath;

            // One-time: ensure the file + schema exist and the journal is migrated off WAL.
            // Best-effort — if the share is read-only for this user, assume the admin already
            // created/migrated the DB and fall through to the read-only connection.
            try
            {
                using var init = OpenWrite();
                EnsureSchema(init);
            }
            catch { /* read-only share or locked; admin DB assumed ready */ }

            _connection = OpenRead();
            ExecuteOn(_connection, "PRAGMA busy_timeout=5000;");
            ExecuteOn(_connection, "PRAGMA foreign_keys=ON;");
        }

        // Persistent read-only connection for all Get* methods.
        private SQLiteConnection OpenRead()
        {
#if REVIT2024
            var c = new SQLiteConnection($"Data Source={_databasePath};Version=3;Read Only=True;");
#else
            var c = new SQLiteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly
            }.ToString());
#endif
            c.Open();
            return c;
        }

        // Short-lived read-write connection for the occasional admin/edit write.
        private SQLiteConnection OpenWrite()
        {
#if REVIT2024
            var c = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
#else
            var c = new SQLiteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate
            }.ToString());
#endif
            c.Open();
            ExecuteOn(c, "PRAGMA busy_timeout=5000;");
            ExecuteOn(c, "PRAGMA journal_mode=DELETE;");
            ExecuteOn(c, "PRAGMA synchronous=NORMAL;");
            ExecuteOn(c, "PRAGMA foreign_keys=ON;");
            return c;
        }

        // Runs a write action against a fresh read-write connection, then closes it.
        private void WithWrite(Action<SQLiteConnection> action)
        {
            using var c = OpenWrite();
            action(c);
        }

        private static void ExecuteOn(SQLiteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
```

- [ ] **Step 2: Make `EnsureSchema` take a connection**

Replace the existing `EnsureSchema()` (lines ~60–99) and the old private `Execute(string)` (lines ~101–106) with an `EnsureSchema(SQLiteConnection)` that runs every `CREATE TABLE IF NOT EXISTS` and the `InstructionsXaml` migration against the passed connection (use `ExecuteOn(c, …)` for each statement). Keep the exact same table definitions (`Families`, `Parameters`, `Thumbnail`, `CustomThumbnail`) and the `InstructionsXaml` column check already present.

```csharp
        private void EnsureSchema(SQLiteConnection c)
        {
            ExecuteOn(c, @"
                CREATE TABLE IF NOT EXISTS Families (
                    Id INTEGER PRIMARY KEY,
                    RelativePath TEXT UNIQUE NOT NULL,
                    FileName TEXT NOT NULL,
                    ModifiedDate DATETIME NOT NULL,
                    FileSize INTEGER NOT NULL,
                    Category TEXT,
                    IndexedDate DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS Parameters (
                    Id INTEGER PRIMARY KEY,
                    FamilyId INTEGER NOT NULL,
                    ParameterName TEXT NOT NULL,
                    DataType TEXT NOT NULL,
                    IsInstance INTEGER NOT NULL,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS Thumbnail (
                    Id INTEGER PRIMARY KEY,
                    FamilyId INTEGER UNIQUE NOT NULL,
                    PngData BLOB NOT NULL,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS CustomThumbnail (
                    Id       INTEGER PRIMARY KEY,
                    FamilyId INTEGER UNIQUE NOT NULL,
                    PngData  BLOB    NOT NULL,
                    OleSynced INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );");

            using var checkCmd = c.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='InstructionsXaml'";
            if ((long)(checkCmd.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT");
        }
```

- [ ] **Step 3: Point every read method at `_connection` (unchanged) and route every write through `WithWrite`**

The `Get*` methods already use `_connection` — leave them. Convert each **write** method to use a fresh write connection. Example — `UpsertFamily` becomes:

```csharp
        public void UpsertFamily(string relativePath, string fileName, DateTime modifiedDateUtc, long fileSize)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                    VALUES (@rel, @name, @modified, @size)
                    ON CONFLICT(RelativePath) DO UPDATE SET
                        FileName = excluded.FileName,
                        ModifiedDate = excluded.ModifiedDate,
                        FileSize = excluded.FileSize";
                AddParam(cmd, "@rel", relativePath);
                AddParam(cmd, "@name", fileName);
                AddParam(cmd, "@modified", modifiedDateUtc.ToString("o"));
                AddParam(cmd, "@size", fileSize);
                cmd.ExecuteNonQuery();
            });
        }
```

Apply the identical pattern (wrap the body in `WithWrite(c => { … using var cmd = c.CreateCommand(); … })`) to: `DeleteStaleEntries`, `SaveInstructionsXaml`, `SaveCustomThumbnail`, `DeleteCustomThumbnail`, `SetOleSynced`. Keep the SQL text and parameters exactly as they are now; only the connection source changes from `_connection` to the `c` passed by `WithWrite`.

- [ ] **Step 4: Build Core (both provider branches)**

```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Smoke-test the connection split (manual, in Revit after deploy)**

Deploy (`Deploy.ps1`, elevated), restart Revit, open the browser. Confirm: list populates (RO reads work), **Sync** still updates the list (transient write works), and **Edit Info → Save** persists instructions (transient write works). This validates Task 2 before later phases depend on it.

- [ ] **Step 6: Checkpoint.**

---

## PHASE 2 — Parameter audit (view-only)

Goal: capture each parameter's group + kind (System/Shared/Family) + GUID + formula via a `FamilyManager` document open during deep scan, and show them with a live filter.

### Task 3: Extend ParameterModel

**Files:**
- Modify: `src/ReviTchucky.Core/Models/ParameterModel.cs`

**Interfaces:**
- Produces: `ParameterModel` with new nullable string properties `ParamGroup`, `Kind`, `Guid`, `Formula`.

- [ ] **Step 1: Add the fields**

```csharp
namespace ReviTchucky.Core.Models
{
    public class ParameterModel
    {
        public long Id { get; set; }
        public long FamilyId { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsInstance { get; set; }

        // "group parameter under" label, e.g. "Dimensions", "Identity Data", "Other".
        public string? ParamGroup { get; set; }
        // "System" | "Shared" | "Family"
        public string? Kind { get; set; }
        // Shared-parameter GUID (null for non-shared).
        public string? Guid { get; set; }
        public string? Formula { get; set; }
    }
}
```

- [ ] **Step 2: Build Core**

```
dotnet build ReviTchucky.sln -c Release2024
```
Expected: 0 errors.

- [ ] **Step 3: Checkpoint.**

---

### Task 4: DB columns + read/write round-trip for new param fields

**Files:**
- Modify: `src/ReviTchucky.Core/Database/IndexRepository.cs`
- Modify: `src/ReviTchucky.Core/Database/BrowserRepository.cs`

**Interfaces:**
- Consumes: `ParameterModel.ParamGroup/Kind/Guid/Formula`.
- Produces: `Parameters` table has 4 new TEXT columns; `IndexRepository.UpdateFamilyMetadata` writes them; `BrowserRepository.GetParameters` reads them.

- [ ] **Step 1: Migrate the `Parameters` table in IndexRepository**

In `IndexRepository.MigrateSchema()` (after the `InstructionsXaml` block, before/after the `CustomThumbnail` create), add idempotent column adds:

```csharp
            foreach (var col in new[] { "ParamGroup", "Kind", "Guid", "Formula" })
            {
                using var c = _connection.CreateCommand();
                c.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Parameters') WHERE name='{col}'";
                if ((long)(c.ExecuteScalar() ?? 0L) == 0)
                    Execute($"ALTER TABLE Parameters ADD COLUMN {col} TEXT");
            }
```

- [ ] **Step 2: Migrate the same columns in BrowserRepository.EnsureSchema**

In `EnsureSchema(SQLiteConnection c)` (from Phase 1 Task 2), after the `InstructionsXaml` check, add:

```csharp
            foreach (var col in new[] { "ParamGroup", "Kind", "Guid", "Formula" })
            {
                using var pc = c.CreateCommand();
                pc.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Parameters') WHERE name='{col}'";
                if ((long)(pc.ExecuteScalar() ?? 0L) == 0)
                    ExecuteOn(c, $"ALTER TABLE Parameters ADD COLUMN {col} TEXT");
            }
```

- [ ] **Step 3: Write the new columns in `IndexRepository.UpdateFamilyMetadata`**

Replace the per-parameter INSERT (currently lines ~164–174) with:

```csharp
                foreach (var p in parameters)
                {
                    using var pCmd = CreateCommand(
                        "INSERT INTO Parameters (FamilyId, ParameterName, DataType, IsInstance, ParamGroup, Kind, Guid, Formula) " +
                        "VALUES (@fid, @name, @type, @inst, @grp, @kind, @guid, @formula)",
                        transaction);
                    AddParam(pCmd, "@fid", familyId);
                    AddParam(pCmd, "@name", p.ParameterName);
                    AddParam(pCmd, "@type", p.DataType);
                    AddParam(pCmd, "@inst", p.IsInstance ? 1 : 0);
                    AddParam(pCmd, "@grp", (object?)p.ParamGroup ?? DBNull.Value);
                    AddParam(pCmd, "@kind", (object?)p.Kind ?? DBNull.Value);
                    AddParam(pCmd, "@guid", (object?)p.Guid ?? DBNull.Value);
                    AddParam(pCmd, "@formula", (object?)p.Formula ?? DBNull.Value);
                    pCmd.ExecuteNonQuery();
                }
```

- [ ] **Step 4: Read the new columns in `BrowserRepository.GetParameters`**

Replace `GetParameters` (currently lines ~161–178) with:

```csharp
        public List<ParameterModel> GetParameters(long familyId)
        {
            var result = new List<ParameterModel>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, ParameterName, DataType, IsInstance, ParamGroup, Kind, Guid, Formula " +
                              "FROM Parameters WHERE FamilyId = @id ORDER BY ParamGroup, ParameterName";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new ParameterModel
                {
                    Id            = reader.GetInt64(0),
                    FamilyId      = familyId,
                    ParameterName = reader.GetString(1),
                    DataType      = reader.GetString(2),
                    IsInstance    = reader.GetInt32(3) == 1,
                    ParamGroup    = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Kind          = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Guid          = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Formula       = reader.IsDBNull(7) ? null : reader.GetString(7),
                });
            return result;
        }
```

- [ ] **Step 5: Build Core (both branches)**

```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: 0 errors.

- [ ] **Step 6: Checkpoint.**

---

### Task 5: FamilyManager document-open extraction

Replace the PartAtom XML peek with opening the family document and reading `FamilyManager`. This is the source of group + kind data.

**Files:**
- Modify: `src/ReviTchucky.Revit/Extraction/FamilyMetadataExtractor.cs`

**Interfaces:**
- Consumes: a `.rfa` path; runs on Revit's main thread (called from `IndexingExternalEventHandler.Execute`, which already guards "too new" families).
- Produces: `(string? Category, IReadOnlyList<ParameterModel>)` with `ParamGroup`, `Kind`, `Guid`, `Formula` populated.

- [ ] **Step 1: Rewrite `ExtractMetadata` to open the document**

Replace the whole class body's `ExtractMetadata` + `ParseAtomXml` with a FamilyManager reader. Keep the constructor and `_app` field.

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ReviTchucky.Core.Models;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace ReviTchucky.Revit.Extraction
{
    /// <summary>
    /// Must be called only from Revit's main thread (inside an ExternalEvent handler).
    /// Opens the family document to read FamilyManager parameters (group + kind), which the
    /// lightweight PartAtom XML cannot provide. Heavier than ExtractPartAtomFromFamilyFile,
    /// but only runs during the admin deep scan.
    /// </summary>
    public class FamilyMetadataExtractor
    {
        private readonly RevitApplication _app;

        public FamilyMetadataExtractor(RevitApplication app)
        {
            _app = app;
        }

        public (string? Category, IReadOnlyList<ParameterModel> Parameters) ExtractMetadata(string rfaPath)
        {
            Document? doc = null;
            try
            {
                doc = _app.OpenDocumentFile(rfaPath);
                if (doc == null || !doc.IsFamilyDocument)
                    return (null, Array.Empty<ParameterModel>());

                string? category = null;
                try { category = doc.OwnerFamily?.FamilyCategory?.Name; } catch { }

                var parameters = new List<ParameterModel>();
                foreach (FamilyParameter fp in doc.FamilyManager.Parameters)
                {
                    try { parameters.Add(ReadParameter(fp)); }
                    catch { /* skip a single unreadable parameter */ }
                }
                return (category, parameters);
            }
            catch
            {
                return (null, Array.Empty<ParameterModel>());
            }
            finally
            {
                try { doc?.Close(false); } catch { }
            }
        }

        private static ParameterModel ReadParameter(FamilyParameter fp)
        {
            var def = fp.Definition;

            string kind;
            string? guid = null;
            if (fp.IsShared)
            {
                kind = "Shared";
                try { guid = fp.GUID.ToString(); } catch { }
            }
            else if (def is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID)
            {
                kind = "System";
            }
            else
            {
                kind = "Family";
            }

            string? group = null;
            try { group = LabelUtils.GetLabelForGroup(def.GetGroupTypeId()); }
            catch
            {
#pragma warning disable CS0618
                try { group = LabelUtils.GetLabelFor(def.ParameterGroup); } catch { }
#pragma warning restore CS0618
            }

            string dataType;
            try { dataType = LabelUtils.GetLabelForSpec(def.GetDataType()); }
            catch { dataType = fp.StorageType.ToString(); }

            string? formula = null;
            try { formula = fp.Formula; } catch { }

            return new ParameterModel
            {
                ParameterName = def.Name,
                DataType      = string.IsNullOrEmpty(dataType) ? "Unknown" : dataType,
                IsInstance    = fp.IsInstance,
                ParamGroup    = group,
                Kind          = kind,
                Guid          = guid,
                Formula       = formula
            };
        }
    }
}
```

> **API note:** `Definition.GetGroupTypeId()` and `LabelUtils.GetLabelForGroup(ForgeTypeId)` exist in Revit 2022+ (covers 2023/2024/2025). The `#pragma`-wrapped `ParameterGroup`/`GetLabelFor` fallback covers the deprecated path. If `GetLabelForSpec` is unavailable for a given build, the `catch` falls back to `StorageType`. Confirm these resolve when building `ReviTchucky.Revit` per config; if a symbol is missing for one Revit version, guard it with the existing per-config constants.

- [ ] **Step 2: Build the Revit project (all three configs — Revit API surface differs per version)**

```
dotnet build ReviTchucky.sln -c Release2023
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: 0 errors. If a `LabelUtils`/`Definition` symbol fails on one config, apply a per-config guard and rebuild.

- [ ] **Step 3: Checkpoint.** No handler changes needed — `IndexingExternalEventHandler.Execute` already calls `Extractor.ExtractMetadata(CurrentItem.FullPath)` inside its `tooNew` guard and try/catch.

---

### Task 6: Parameters UI — Group/Kind columns + live filter

**Files:**
- Modify: `src/LibraryBrowser/ReviTchucky.UI/ViewModels/FamilyBrowserViewModel.cs`
- Modify: `src/LibraryBrowser/ReviTchucky.UI/Views/FamilyBrowserWindow.xaml`

**Interfaces:**
- Consumes: `BrowserRepository.GetParameters` (now with group/kind).
- Produces: VM exposes `ParameterFilter` (string) and `FilteredParameters` (`ObservableCollection<ParameterModel>`); the Parameters grid binds to `FilteredParameters` and shows Group + Kind.

- [ ] **Step 1: Add filtering to the VM**

In `FamilyBrowserViewModel`, add backing fields near the other private fields:

```csharp
        private string _parameterFilter = string.Empty;
        public ObservableCollection<ParameterModel> FilteredParameters { get; } = new();
```

Add the property (place near `Parameters`):

```csharp
        public string ParameterFilter
        {
            get => _parameterFilter;
            set { SetProperty(ref _parameterFilter, value ?? string.Empty); ApplyParameterFilter(); }
        }

        private void ApplyParameterFilter()
        {
            var q = _parameterFilter.Trim();
            IEnumerable<ParameterModel> src = _parameters;
            if (!string.IsNullOrEmpty(q))
                src = src.Where(p =>
                    (p.ParameterName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.ParamGroup?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Kind?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));

            FilteredParameters.Clear();
            foreach (var p in src) FilteredParameters.Add(p);
        }
```

In the `Parameters` setter, refresh the filtered view. Change:

```csharp
        public List<ParameterModel> Parameters
        {
            get => _parameters;
            set => SetProperty(ref _parameters, value);
        }
```

to:

```csharp
        public List<ParameterModel> Parameters
        {
            get => _parameters;
            set { SetProperty(ref _parameters, value); ApplyParameterFilter(); }
        }
```

(`_parameters` is assigned on the dispatcher thread in `LoadDetailAsync`, so `FilteredParameters` updates safely there.)

- [ ] **Step 2: Update the Parameters tab XAML**

Replace the Parameters `TabItem` (currently lines ~198–207 in `FamilyBrowserWindow.xaml`) with a filter box + expanded grid bound to `FilteredParameters`:

```xml
                <TabItem Header="{Binding FilteredParameters.Count, StringFormat='Parameters ({0})'}">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <TextBox Grid.Row="0" Margin="0,0,0,4"
                                 Text="{Binding ParameterFilter, UpdateSourceTrigger=PropertyChanged}"
                                 ToolTip="Filter parameters by name, group, or kind"/>
                        <DataGrid Grid.Row="1" ItemsSource="{Binding FilteredParameters}"
                                  AutoGenerateColumns="False" IsReadOnly="True"
                                  GridLinesVisibility="Horizontal">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Name"  Binding="{Binding ParameterName}" Width="*"/>
                                <DataGridTextColumn Header="Group" Binding="{Binding ParamGroup}"    Width="130"/>
                                <DataGridTextColumn Header="Kind"  Binding="{Binding Kind}"          Width="70"/>
                                <DataGridTextColumn Header="Type"  Binding="{Binding DataType}"      Width="100"/>
                                <DataGridCheckBoxColumn Header="Inst." Binding="{Binding IsInstance}" Width="46"/>
                            </DataGrid.Columns>
                        </DataGrid>
                    </Grid>
                </TabItem>
```

- [ ] **Step 3: Build UI (both branches)**

```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: 0 errors.

- [ ] **Step 4: Checkpoint.**

---

## PHASE 3 — Image gallery per family

Goal: multiple admin-added images per family, each with a caption; files on disk under `.Setup\Gallery\{familyId}\`, metadata in a `FamilyImage` table; shown in the detail panel, managed in the editor.

### Task 7: FamilyImage model + gallery storage helper + DB CRUD

**Files:**
- Create: `src/ReviTchucky.Core/Models/FamilyImage.cs`
- Modify: `src/ReviTchucky.Core/Database/BrowserRepository.cs`

**Interfaces:**
- Produces:
  - `FamilyImage { long Id; long FamilyId; string FileName; string? Caption; int SortOrder; }`
  - `BrowserRepository.GetImages(long familyId) -> List<FamilyImage>`
  - `BrowserRepository.AddImage(long familyId, byte[] pngData, string? caption) -> FamilyImage` (writes file + row)
  - `BrowserRepository.UpdateCaption(long imageId, string? caption)`
  - `BrowserRepository.DeleteImage(long imageId)` (deletes row + file)
  - `BrowserRepository.ReorderImages(long familyId, IReadOnlyList<long> orderedImageIds)`
  - `BrowserRepository.GetGalleryPath(long familyId, string fileName) -> string` (absolute path for display)
- Consumes: the `_databasePath` field (Phase 1) to derive the `.Setup\Gallery` root: `Path.GetDirectoryName(_databasePath)` is the `.Setup` folder.

- [ ] **Step 1: Create the model**

```csharp
namespace ReviTchucky.Core.Models
{
    public class FamilyImage
    {
        public long Id { get; set; }
        public long FamilyId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
    }
}
```

- [ ] **Step 2: Create the `FamilyImage` table in `EnsureSchema`**

In `BrowserRepository.EnsureSchema(SQLiteConnection c)`, add to the `CREATE TABLE IF NOT EXISTS` batch:

```sql
                CREATE TABLE IF NOT EXISTS FamilyImage (
                    Id        INTEGER PRIMARY KEY,
                    FamilyId  INTEGER NOT NULL,
                    FileName  TEXT    NOT NULL,
                    Caption   TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );
```

Add the same `CREATE TABLE IF NOT EXISTS FamilyImage (...)` to `IndexRepository.MigrateSchema()` so an admin's deep-scan DB also has it.

- [ ] **Step 3: Add the gallery storage + CRUD methods to BrowserRepository**

```csharp
        private string GalleryRoot(long familyId)
        {
            var setupDir = System.IO.Path.GetDirectoryName(_databasePath)!; // the .Setup folder
            return System.IO.Path.Combine(setupDir, "Gallery", familyId.ToString());
        }

        public string GetGalleryPath(long familyId, string fileName)
            => System.IO.Path.Combine(GalleryRoot(familyId), fileName);

        public List<FamilyImage> GetImages(long familyId)
        {
            var result = new List<FamilyImage>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, FileName, Caption, SortOrder FROM FamilyImage " +
                              "WHERE FamilyId=@id ORDER BY SortOrder, Id";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new FamilyImage
                {
                    Id        = reader.GetInt64(0),
                    FamilyId  = familyId,
                    FileName  = reader.GetString(1),
                    Caption   = reader.IsDBNull(2) ? null : reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                });
            return result;
        }

        public FamilyImage AddImage(long familyId, byte[] pngData, string? caption)
        {
            var dir = GalleryRoot(familyId);
            System.IO.Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid():N}.png";
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, fileName), pngData);

            long newId = 0;
            int sort = 0;
            WithWrite(c =>
            {
                using (var max = c.CreateCommand())
                {
                    max.CommandText = "SELECT COALESCE(MAX(SortOrder)+1, 0) FROM FamilyImage WHERE FamilyId=@id";
                    AddParam(max, "@id", familyId);
                    sort = Convert.ToInt32(max.ExecuteScalar() ?? 0);
                }
                using (var ins = c.CreateCommand())
                {
                    ins.CommandText = "INSERT INTO FamilyImage (FamilyId, FileName, Caption, SortOrder) " +
                                      "VALUES (@fid, @fn, @cap, @sort)";
                    AddParam(ins, "@fid", familyId);
                    AddParam(ins, "@fn", fileName);
                    AddParam(ins, "@cap", (object?)caption ?? DBNull.Value);
                    AddParam(ins, "@sort", sort);
                    ins.ExecuteNonQuery();
                }
                using (var sel = c.CreateCommand())
                {
                    sel.CommandText = "SELECT Id FROM FamilyImage WHERE FamilyId=@fid AND FileName=@fn";
                    AddParam(sel, "@fid", familyId);
                    AddParam(sel, "@fn", fileName);
                    newId = (long)(sel.ExecuteScalar() ?? 0L);
                }
            });
            return new FamilyImage { Id = newId, FamilyId = familyId, FileName = fileName, Caption = caption, SortOrder = sort };
        }

        public void UpdateCaption(long imageId, string? caption)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE FamilyImage SET Caption=@cap WHERE Id=@id";
                AddParam(cmd, "@cap", (object?)caption ?? DBNull.Value);
                AddParam(cmd, "@id", imageId);
                cmd.ExecuteNonQuery();
            });
        }

        public void DeleteImage(long imageId)
        {
            // Look up file (RO connection) before deleting the row.
            long familyId = 0; string? fileName = null;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT FamilyId, FileName FROM FamilyImage WHERE Id=@id";
                AddParam(cmd, "@id", imageId);
                using var r = cmd.ExecuteReader();
                if (r.Read()) { familyId = r.GetInt64(0); fileName = r.GetString(1); }
            }
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM FamilyImage WHERE Id=@id";
                AddParam(cmd, "@id", imageId);
                cmd.ExecuteNonQuery();
            });
            if (fileName != null)
            {
                try { System.IO.File.Delete(System.IO.Path.Combine(GalleryRoot(familyId), fileName)); } catch { }
            }
        }

        public void ReorderImages(long familyId, IReadOnlyList<long> orderedImageIds)
        {
            WithWrite(c =>
            {
                for (int i = 0; i < orderedImageIds.Count; i++)
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "UPDATE FamilyImage SET SortOrder=@s WHERE Id=@id AND FamilyId=@fid";
                    AddParam(cmd, "@s", i);
                    AddParam(cmd, "@id", orderedImageIds[i]);
                    AddParam(cmd, "@fid", familyId);
                    cmd.ExecuteNonQuery();
                }
            });
        }
```

- [ ] **Step 4: Build Core (both branches)**

```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: 0 errors.

- [ ] **Step 5: Checkpoint.**

---

### Task 8: GalleryItemViewModel + detail-panel gallery

**Files:**
- Create: `src/LibraryBrowser/ReviTchucky.UI/ViewModels/GalleryItemViewModel.cs`
- Modify: `src/LibraryBrowser/ReviTchucky.UI/ViewModels/FamilyBrowserViewModel.cs`
- Modify: `src/LibraryBrowser/ReviTchucky.UI/Views/FamilyBrowserWindow.xaml`

**Interfaces:**
- Produces:
  - `GalleryItemViewModel { long Id; string? Caption; BitmapSource? Image; }` (loads the file lazily, placeholder/null if missing).
  - `FamilyBrowserViewModel.GalleryItems` (`ObservableCollection<GalleryItemViewModel>`), refreshed in `LoadDetailAsync`.

- [ ] **Step 1: Create GalleryItemViewModel**

```csharp
using System.IO;
using System.Windows.Media.Imaging;

namespace ReviTchucky.UI.ViewModels
{
    public class GalleryItemViewModel
    {
        public long Id { get; }
        public string? Caption { get; }
        public BitmapSource? Image { get; }

        public GalleryItemViewModel(long id, string? caption, string absolutePath)
        {
            Id = id;
            Caption = caption;
            Image = LoadFile(absolutePath);
        }

        private static BitmapSource? LoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null; // missing file → placeholder (null) handled by UI
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new System.Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 320;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
```

- [ ] **Step 2: Load gallery items in the VM**

In `FamilyBrowserViewModel`, add:

```csharp
        public ObservableCollection<GalleryItemViewModel> GalleryItems { get; } = new();
```

In `LoadDetailAsync`, extend the background block to also fetch images and rebuild `GalleryItems` on the dispatcher. Replace the body of the `ThreadPool.QueueUserWorkItem` lambda with:

```csharp
                try
                {
                    var xaml = _repo.GetInstructionsXaml(item.Id);
                    var prms = _repo.GetParameters(item.Id);
                    var images = _repo.GetImages(item.Id)
                        .Select(im => new GalleryItemViewModel(im.Id, im.Caption, _repo.GetGalleryPath(item.Id, im.FileName)))
                        .ToList();
                    _dispatcher.Invoke(() =>
                    {
                        InstructionsXaml = xaml;
                        Parameters = prms;
                        GalleryItems.Clear();
                        foreach (var g in images) GalleryItems.Add(g);
                    });
                }
                catch { /* swallow — detail load failure is non-fatal */ }
```

Also clear `GalleryItems` in the early-return branch when `item == null`:

```csharp
            if (item == null) { InstructionsXaml = null; Parameters = new List<ParameterModel>(); GalleryItems.Clear(); return; }
```

- [ ] **Step 3: Add a gallery region to the detail panel**

In `FamilyBrowserWindow.xaml`, add a third `TabItem` to the detail `TabControl` (after Parameters):

```xml
                <TabItem Header="{Binding GalleryItems.Count, StringFormat='Gallery ({0})'}">
                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <ItemsControl ItemsSource="{Binding GalleryItems}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate DataType="{x:Type vm:GalleryItemViewModel}">
                                    <StackPanel Margin="6" Width="160">
                                        <Border Background="{StaticResource Brush.Control}"
                                                BorderBrush="{StaticResource Brush.Border}" BorderThickness="1"
                                                Height="120">
                                            <Image Source="{Binding Image}" Stretch="Uniform"/>
                                        </Border>
                                        <TextBlock Text="{Binding Caption}" TextWrapping="Wrap" FontSize="11"
                                                   Foreground="{StaticResource Brush.TextMuted}" Margin="0,3,0,0"/>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </TabItem>
```

- [ ] **Step 4: Build UI (both branches)**

```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: 0 errors.

- [ ] **Step 5: Checkpoint.**

---

### Task 9: Gallery management in the editor

**Files:**
- Modify: `src/LibraryBrowser/ReviTchucky.UI/ViewModels/InstructionsEditorViewModel.cs`
- Modify: `src/LibraryBrowser/ReviTchucky.UI/Views/InstructionsEditorWindow.xaml` (+ `.xaml.cs`)

**Interfaces:**
- Consumes: `BrowserRepository` (already passed to the editor VM), `AddImage/UpdateCaption/DeleteImage/GetImages/GetGalleryPath` (Task 7), the existing `ConvertToPng` helper in this VM.
- Produces: editor exposes `GalleryItems` (`ObservableCollection<GalleryItemViewModel>`), `AddImageCommand`, `DeleteImageCommand` (param: image id), and per-item caption editing that persists on change.

- [ ] **Step 1: Add gallery state + commands to the editor VM**

In `InstructionsEditorViewModel`, add:

```csharp
        public System.Collections.ObjectModel.ObservableCollection<GalleryItemViewModel> GalleryItems { get; } = new();
        public ICommand AddImageCommand { get; }
        public ICommand DeleteImageCommand { get; }
```

In the constructor (after the existing command wiring), add:

```csharp
            AddImageCommand    = new RelayCommand(AddImage);
            DeleteImageCommand = new RelayCommand<long>(DeleteImage);
            ReloadGallery();
```

The project has only the non-generic `RelayCommand` (`src/LibraryBrowser/ReviTchucky.UI/ViewModels/RelayCommand.cs`). Create a generic sibling `RelayCommand<T>` in the same folder (`src/LibraryBrowser/ReviTchucky.UI/ViewModels/RelayCommandT.cs`):

```csharp
using System;
using System.Windows.Input;

namespace ReviTchucky.UI.ViewModels
{
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
            => _canExecute?.Invoke(parameter is T t ? t : default!) ?? true;

        public void Execute(object? parameter) => _execute(parameter is T t ? t : default!);
    }
}
```

Add the methods:

```csharp
        private void ReloadGallery()
        {
            GalleryItems.Clear();
            foreach (var im in _repo.GetImages(_familyId))
                GalleryItems.Add(new GalleryItemViewModel(im.Id, im.Caption, _repo.GetGalleryPath(_familyId, im.FileName)));
        }

        private void AddImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Title  = "Add gallery image",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                foreach (var file in dlg.FileNames)
                {
                    var png = ConvertToPng(File.ReadAllBytes(file));
                    _repo.AddImage(_familyId, png, Path.GetFileNameWithoutExtension(file));
                }
                ReloadGallery();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not add image: {ex.Message}");
            }
        }

        private void DeleteImage(long imageId)
        {
            _repo.DeleteImage(imageId);
            ReloadGallery();
        }

        public void SaveCaption(long imageId, string? caption) => _repo.UpdateCaption(imageId, caption);
```

> Caption editing: the gallery writes are committed immediately (admin edits are infrequent), so they do not depend on the editor's `ExecuteSave`. Default caption = source file name; the admin can edit it inline (Step 2).

- [ ] **Step 2: Add a gallery row to the editor window**

`InstructionsEditorWindow.xaml` is a fixed 4-row `Grid`: row 0 header (Auto), row 1 toolbar (Auto), row 2 `RichTextBox` editor body (`*`), row 3 footer (Auto). Insert a capped-height gallery **between** the editor body and the footer.

(a) Change the `Grid.RowDefinitions` (currently lines ~16–21) to add a fifth row:

```xml
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>  <!-- gallery -->
            <RowDefinition Height="Auto"/>  <!-- footer -->
        </Grid.RowDefinitions>
```

(b) Change the footer `Grid` from `Grid.Row="3"` to `Grid.Row="4"` (currently line ~88).

(c) Insert the gallery block at `Grid.Row="3"` (after the `RichTextBox`, before the footer). The add button is labeled **"Add to Gallery"** to avoid confusion with the toolbar's existing inline-image "Add Image" (`AddImage_Click`, which inserts images into the rich text — a different feature):

```xml
        <DockPanel Grid.Row="3" Margin="8,0,8,0" LastChildFill="False">
            <TextBlock Text="GALLERY" FontWeight="SemiBold" FontSize="11"
                       VerticalAlignment="Center" DockPanel.Dock="Left"/>
            <Button Content="Add to Gallery…" Command="{Binding AddImageCommand}"
                    DockPanel.Dock="Right"/>
        </DockPanel>
        <ScrollViewer Grid.Row="3" Margin="8,22,8,4" MaxHeight="150"
                      VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding GalleryItems}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Width="150" Margin="6">
                            <Image Source="{Binding Image}" Height="90" Stretch="Uniform"/>
                            <TextBox Text="{Binding Caption}" Margin="0,3,0,0"
                                     Tag="{Binding Id}" LostFocus="Caption_LostFocus"/>
                            <Button Content="Delete" Margin="0,3,0,0"
                                    Command="{Binding DataContext.DeleteImageCommand,
                                              RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                    CommandParameter="{Binding Id}"/>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
```

> The `DockPanel` (header + button) and the `ScrollViewer` (thumbnails) both sit in `Grid.Row="3"`; the `ScrollViewer`'s `Margin` top of 22 leaves room for the header band above it. Keep it simple — exact styling is deferred.

> Note: `GalleryItemViewModel.Caption` is get-only in Task 8. For inline editing here, make it settable — change `public string? Caption { get; }` to `public string? Caption { get; set; }` in `GalleryItemViewModel` and keep assigning it in the constructor. The browser detail panel binds one-way, so this does not affect it.

- [ ] **Step 3: Persist caption edits in code-behind**

In `InstructionsEditorWindow.xaml.cs`, add the `LostFocus` handler:

```csharp
        private void Caption_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb &&
                tb.Tag is long id &&
                DataContext is ViewModels.InstructionsEditorViewModel vm)
            {
                vm.SaveCaption(id, tb.Text);
            }
        }
```

> If XAML binds `Tag="{Binding Id}"` as `long`, the `tb.Tag is long id` pattern works. If the binding boxes it differently, use `System.Convert.ToInt64(tb.Tag)`.

- [ ] **Step 4: Build UI (both branches)**

```
dotnet build ReviTchucky.sln -c Release2024
dotnet build ReviTchucky.sln -c Release2025
```
Expected: 0 errors.

- [ ] **Step 5: Checkpoint.**

---

## Final Verification (manual, in Revit)

1. **Build all three configs** from the `ReviTchucky` folder:
   ```
   dotnet build ReviTchucky.sln -c Release2023
   dotnet build ReviTchucky.sln -c Release2024
   dotnet build ReviTchucky.sln -c Release2025
   ```
   Expected: 0 errors each.
2. **Deploy** (`Deploy.ps1`, elevated) and restart Revit.
3. **Parameter audit:** Settings → **Start Deep Scan** over a folder containing families with shared, system, and family parameters in varied groups (incl. "Other"). Open a family → **Parameters** tab: confirm Group + Kind populate correctly; type in the filter box and confirm rows narrow live by name/group/kind.
4. **Gallery:** Select a family → **Edit Info** → **Add Image…** (pick 2–3 files), edit a caption, close. Confirm files exist under `…\.Setup\Gallery\{familyId}\`, the **Gallery** tab shows them with captions, **Delete** removes file + row, and manually deleting a file on disk then reopening shows a blank placeholder (no crash).
5. **Concurrency:** Point `LibraryFolderPath` at a `\\server\share\…` location. From **two** machines, open the browser and browse simultaneously while one machine runs a Deep Scan. Confirm: both readers keep working with no "database is locked" error, and after the scan completes a **Sync**/reopen shows the updated data. Verify no `-wal`/`-shm` files appear next to the `.db` (rollback journal in use).

## Out of scope (future)
- Editing/reorganizing parameters from the tool (write-back to `.rfa`).
- Auto-generating gallery images from the `.rfa` (rendered views).
- Auto-flagging rules for "disorganized" params (this iteration only shows + filters).
- Drag-to-reorder gallery UI (the `ReorderImages` API exists; wiring a drag handler is deferred).
- UI styling/layout polish for the new regions.
```
