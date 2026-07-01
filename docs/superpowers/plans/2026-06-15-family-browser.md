# Family Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a non-modal WPF family browser for searching, loading, version-checking, and annotating Revit families from the indexed library.

**Architecture:** Three-layer addition following the existing pattern — Core (models + DB + OLE write), UI (WPF ViewModels + Windows), Revit (ExternalEvent handlers + ribbon command). The browser window is held as a singleton on `Application`; Revit API calls (load family, get project families) go through two new `IExternalEventHandler` implementations using the existing `ManualResetEventSlim` ping-pong pattern.

**Tech Stack:** C# / WPF / SQLite (System.Data.SQLite for net48, Microsoft.Data.Sqlite for net8) / OpenMCDF 3.1.2 / Revit API 2023–2025 / `System.Drawing` (DIB conversion)

---

## File Map

### Created
| File | Responsibility |
|---|---|
| `RVTuk.Core/Models/VersionStatus.cs` | Enum: None / UpToDate / UpdateAvailable |
| `RVTuk.Core/Models/FamilyBrowserItem.cs` | Lightweight DB row + resolved thumbnail + version status |
| `RVTuk.Core/Database/BrowserRepository.cs` | All browser-specific DB reads/writes (separate from IndexRepository) |
| `RVTuk.Core/Extraction/ThumbnailWriter.cs` | Convert PNG → DIB, write to `.rfa` OLE `\x05SummaryInformation` stream |
| `RVTuk.UI/Controls/RichTextBoxHelper.cs` | Attached property `DocumentXaml` binding XAML↔FlowDocument |
| `RVTuk.UI/ViewModels/FamilyBrowserItemViewModel.cs` | Per-item VM wrapping `FamilyBrowserItem`; holds `BitmapSource` thumbnail |
| `RVTuk.UI/ViewModels/FamilyBrowserViewModel.cs` | Main browser VM: search, filter, version check, load/update, open editor |
| `RVTuk.UI/ViewModels/InstructionsEditorViewModel.cs` | Editor VM: rich text, thumbnail ⁝ menu, save/cancel |
| `RVTuk.UI/Views/FamilyBrowserWindow.xaml` | Two-panel layout: family list + detail panel |
| `RVTuk.UI/Views/FamilyBrowserWindow.xaml.cs` | Code-behind: drag & drop onto thumbnail, paste |
| `RVTuk.UI/Views/InstructionsEditorWindow.xaml` | Editor layout: thumbnail header, toolbar, RichTextBox, footer |
| `RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs` | Code-behind: drag & drop, paste, FlowDocument serialization |
| `RVTuk.Revit/ExternalEvents/GetProjectFamiliesEventHandler.cs` | Reads `FilteredElementCollector(doc).OfClass(typeof(Family))` on Revit main thread |
| `RVTuk.Revit/ExternalEvents/LoadFamilyEventHandler.cs` | Calls `doc.LoadFamily(path, options)` on Revit main thread |
| `RVTuk.Revit/Commands/BrowseLibraryCommand.cs` | Opens or focuses `FamilyBrowserWindow` |

### Modified
| File | Change |
|---|---|
| `RVTuk.Core/Database/IndexRepository.cs` | Add `MigrateSchema()` call after `CreateSchemaIfNeeded()` |
| `RVTuk.Revit/Application.cs` | Register two new ExternalEvents; hold `FamilyBrowserWindow` singleton; add Browse Library ribbon button |

---

## Task 1: VersionStatus enum + FamilyBrowserItem model

**Files:**
- Create: `RVTuk.Core/Models/VersionStatus.cs`
- Create: `RVTuk.Core/Models/FamilyBrowserItem.cs`

- [ ] **Step 1: Create VersionStatus**

```csharp
// RVTuk.Core/Models/VersionStatus.cs
namespace RVTuk.Core.Models
{
    public enum VersionStatus { None, UpToDate, UpdateAvailable }
}
```

- [ ] **Step 2: Create FamilyBrowserItem**

```csharp
// RVTuk.Core/Models/FamilyBrowserItem.cs
using System;

namespace RVTuk.Core.Models
{
    public class FamilyBrowserItem
    {
        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? Category { get; set; }
        public DateTime ModifiedDate { get; set; }
        public byte[]? ThumbnailPng { get; set; }  // resolved: CustomThumbnail ?? OLE thumbnail
        public bool HasCustomThumbnail { get; set; }
        public bool OleSynced { get; set; } = true;
        public VersionStatus VersionStatus { get; set; } = VersionStatus.None;
    }
}
```

- [ ] **Step 3: Build Core to confirm no errors**

```powershell
dotnet build "RVTuk\RVTuk.Core\RVTuk.Core.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add RVTuk/RVTuk.Core/Models/VersionStatus.cs RVTuk/RVTuk.Core/Models/FamilyBrowserItem.cs
git commit -m "feat: add VersionStatus enum and FamilyBrowserItem model"
```

---

## Task 2: DB schema migration + BrowserRepository

**Files:**
- Modify: `RVTuk.Core/Database/IndexRepository.cs`
- Create: `RVTuk.Core/Database/BrowserRepository.cs`

- [ ] **Step 1: Add MigrateSchema to IndexRepository**

In `IndexRepository.cs`, add this method and call it from the constructor after `CreateSchemaIfNeeded()`:

```csharp
// In constructor, after CreateSchemaIfNeeded():
MigrateSchema();

// New method:
private void MigrateSchema()
{
    // Add InstructionsXaml column to Families if missing
    using var checkCmd = _connection.CreateCommand();
    checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='InstructionsXaml'";
    var count = (long)(checkCmd.ExecuteScalar() ?? 0L);
    if (count == 0)
        Execute("ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT");

    Execute(@"CREATE TABLE IF NOT EXISTS CustomThumbnail (
        Id       INTEGER PRIMARY KEY,
        FamilyId INTEGER UNIQUE NOT NULL,
        PngData  BLOB    NOT NULL,
        OleSynced INTEGER NOT NULL DEFAULT 1,
        FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
    )");
}
```

- [ ] **Step 2: Create BrowserRepository**

```csharp
// RVTuk.Core/Database/BrowserRepository.cs
using System;
using System.Collections.Generic;
using System.Data;
using RVTuk.Core.Models;

#if REVIT2024
using System.Data.SQLite;
#else
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;
#endif

namespace RVTuk.Core.Database
{
    public class BrowserRepository : IDisposable
    {
        private readonly SQLiteConnection _connection;

        public BrowserRepository(string databasePath)
        {
#if REVIT2024
            _connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
#else
            _connection = new SQLiteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite
                }.ToString());
#endif
            _connection.Open();
        }

        // Returns all families with thumbnail resolved (CustomThumbnail ?? OLE Thumbnail)
        public List<FamilyBrowserItem> GetAllFamilies()
        {
            var result = new List<FamilyBrowserItem>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT f.Id, f.FileName, f.RelativePath, f.Category, f.ModifiedDate,
                       t.PngData  AS OlePng,
                       ct.PngData AS CustomPng,
                       ct.OleSynced
                FROM Families f
                LEFT JOIN Thumbnail t ON t.FamilyId = f.Id
                LEFT JOIN CustomThumbnail ct ON ct.FamilyId = f.Id
                ORDER BY f.FileName";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hasCustom = !reader.IsDBNull(6);
                result.Add(new FamilyBrowserItem
                {
                    Id               = reader.GetInt64(0),
                    FileName         = reader.GetString(1),
                    RelativePath     = reader.GetString(2),
                    Category         = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ModifiedDate     = DateTime.Parse(reader.GetString(4)),
                    ThumbnailPng     = hasCustom ? (byte[])reader[6] : (reader.IsDBNull(5) ? null : (byte[])reader[5]),
                    HasCustomThumbnail = hasCustom,
                    OleSynced        = !hasCustom || reader.GetInt32(7) == 1,
                });
            }
            return result;
        }

        public List<string?> GetCategories()
        {
            var cats = new List<string?> { null }; // null = "All"
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT Category FROM Families WHERE Category IS NOT NULL ORDER BY Category";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                cats.Add(reader.GetString(0));
            return cats;
        }

        public string? GetInstructionsXaml(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT InstructionsXaml FROM Families WHERE Id = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (string)result;
        }

        public List<ParameterModel> GetParameters(long familyId)
        {
            var result = new List<ParameterModel>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, ParameterName, DataType, IsInstance FROM Parameters WHERE FamilyId = @id ORDER BY ParameterName";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new ParameterModel
                {
                    Id            = reader.GetInt64(0),
                    FamilyId      = familyId,
                    ParameterName = reader.GetString(1),
                    DataType      = reader.GetString(2),
                    IsInstance    = reader.GetInt32(3) == 1
                });
            return result;
        }

        public (byte[]? Png, bool OleSynced) GetCustomThumbnail(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT PngData, OleSynced FROM CustomThumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return (null, true);
            return ((byte[])reader[0], reader.GetInt32(1) == 1);
        }

        public byte[]? GetOleThumbnail(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT PngData FROM Thumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (byte[])result;
        }

        public void SaveInstructionsXaml(long familyId, string? xaml)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE Families SET InstructionsXaml = @xaml WHERE Id = @id";
            AddParam(cmd, "@xaml", (object?)xaml ?? DBNull.Value);
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
        }

        public void SaveCustomThumbnail(long familyId, byte[] pngData, bool oleSynced)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO CustomThumbnail (FamilyId, PngData, OleSynced)
                                VALUES (@fid, @png, @sync)
                                ON CONFLICT(FamilyId) DO UPDATE SET PngData=@png, OleSynced=@sync";
            AddParam(cmd, "@fid", familyId);
            AddParam(cmd, "@png", pngData);
            AddParam(cmd, "@sync", oleSynced ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void DeleteCustomThumbnail(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomThumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
        }

        public void SetOleSynced(long familyId, bool synced)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE CustomThumbnail SET OleSynced = @sync WHERE FamilyId = @id";
            AddParam(cmd, "@sync", synced ? 1 : 0);
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
        }

        private static void AddParam(IDbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public void Dispose() => _connection.Dispose();
    }
}
```

- [ ] **Step 3: Build Core**

```powershell
dotnet build "RVTuk\RVTuk.Core\RVTuk.Core.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add RVTuk/RVTuk.Core/Database/IndexRepository.cs RVTuk/RVTuk.Core/Database/BrowserRepository.cs
git commit -m "feat: DB schema migration (InstructionsXaml, CustomThumbnail) + BrowserRepository"
```

---

## Task 3: ThumbnailWriter — write PNG to .rfa OLE stream

**Files:**
- Create: `RVTuk.Core/Extraction/ThumbnailWriter.cs`

The `.rfa` OLE compound document contains a `\x05SummaryInformation` stream that holds the thumbnail as a CF_DIB property (PIDSI_THUMBNAIL = 0x0F). We rebuild this stream with the new image.

- [ ] **Step 1: Create ThumbnailWriter**

```csharp
// RVTuk.Core/Extraction/ThumbnailWriter.cs
using System;
using System.IO;
using OpenMcdf;

namespace RVTuk.Core.Extraction
{
    public static class ThumbnailWriter
    {
        private const string StreamName = "\x05SummaryInformation";

        /// <summary>
        /// Writes pngData as the OLE thumbnail into the .rfa file.
        /// Returns true on success. Makes a .bak backup and restores it on failure.
        /// </summary>
        public static bool WriteThumbnailToRfa(string rfaPath, byte[] pngData)
        {
            try
            {
                if (!File.Exists(rfaPath)) return false;

                var dib = ConvertPngToDib(pngData);
                if (dib == null) return false;

                var streamBytes = BuildSummaryInfoStream(dib);

                var backup = rfaPath + ".thumb_bak";
                File.Copy(rfaPath, backup, overwrite: true);
                try
                {
                    WriteStreamToCompoundFile(rfaPath, StreamName, streamBytes);
                    return true;
                }
                catch
                {
                    File.Copy(backup, rfaPath, overwrite: true);
                    return false;
                }
                finally
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }
            }
            catch { return false; }
        }

        private static void WriteStreamToCompoundFile(string path, string streamName, byte[] data)
        {
            // OpenMCDF 3.x write API — opens the compound file for read/write.
            // RootStorage.Open with FileAccess.ReadWrite is the v3 equivalent of
            // CompoundFile(path, CFSUpdateMode.Update, ...) from v2.
            // If the API differs in your version, check OpenMcdf NuGet release notes.
            using var storage = RootStorage.Open(path, FileAccess.ReadWrite);
            try { storage.Delete(streamName); } catch { /* stream may not exist */ }
            using var stream = storage.AddStream(streamName);
            stream.Write(data, 0, data.Length);
        }

        // Build a minimal \x05SummaryInformation property set stream
        // containing only PIDSI_THUMBNAIL (0x0F) with the supplied DIB bytes.
        private static byte[] BuildSummaryInfoStream(byte[] dib)
        {
            // Property value layout at section offset 16:
            //   [2] VT_CF = 0x47 0x00
            //   [2] padding
            //   [4] cbSize = 4 + dib.Length  (includes the 4-byte CF format field)
            //   [4] CF_DIB = 0x08 0x00 0x00 0x00
            //   [N] dib bytes
            int propValueSize = 12 + dib.Length;
            // Pad to 4-byte boundary
            int propValuePadded = (propValueSize + 3) & ~3;

            // Section layout:
            //   [4] section size = 16 (header) + propValuePadded
            //   [4] property count = 1
            //   [4] property ID = 0x0F
            //   [4] property offset from section start = 16
            //   [propValuePadded] property value
            int sectionSize = 16 + propValuePadded;

            // File header layout (48 bytes):
            //   [2] byte order 0xFE 0xFF
            //   [2] format version 0x00 0x00
            //   [2] OS major 0x06 0x00
            //   [2] OS minor 0x02 0x00
            //   [16] CLSID (zeros)
            //   [4] property set count = 1
            //   [16] FMTID_SummaryInformation
            //   [4] section offset = 48

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // File header
            w.Write((ushort)0xFFFE);   // byte order
            w.Write((ushort)0x0000);   // format version
            w.Write((ushort)0x0006);   // OS major
            w.Write((ushort)0x0002);   // OS minor
            w.Write(new byte[16]);     // CLSID zeros
            w.Write((uint)1);          // one property set

            // FMTID_SummaryInformation {F29F85E0-4FF9-1068-AB91-08002B27B3D9}
            w.Write(new byte[] {
                0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10,
                0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9
            });
            w.Write((uint)48);         // section starts at offset 48

            // Section header
            w.Write((uint)sectionSize);
            w.Write((uint)1);          // one property
            w.Write((uint)0x0F);       // PIDSI_THUMBNAIL
            w.Write((uint)16);         // property value at section offset 16

            // Property value
            w.Write((ushort)0x0047);   // VT_CF
            w.Write((ushort)0x0000);   // padding
            w.Write((uint)(4 + dib.Length)); // cbSize
            w.Write((uint)8);          // CF_DIB
            w.Write(dib);

            // Pad to 4-byte boundary
            int written = 12 + dib.Length;
            int pad = propValuePadded - written;
            if (pad > 0) w.Write(new byte[pad]);

            return ms.ToArray();
        }

        private static byte[]? ConvertPngToDib(byte[] pngData)
        {
            try
            {
                using var ms = new MemoryStream(pngData);
                using var bmp = new System.Drawing.Bitmap(ms);
                using var bmpStream = new MemoryStream();
                bmp.Save(bmpStream, System.Drawing.Imaging.ImageFormat.Bmp);
                var bmpBytes = bmpStream.ToArray();
                // DIB = BMP without 14-byte file header
                var dib = new byte[bmpBytes.Length - 14];
                Array.Copy(bmpBytes, 14, dib, 0, dib.Length);
                return dib;
            }
            catch { return null; }
        }
    }
}
```

- [ ] **Step 2: Build Core**

```powershell
dotnet build "RVTuk\RVTuk.Core\RVTuk.Core.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

> **Note:** If `RootStorage.Open(path, FileAccess.ReadWrite)` does not exist in OpenMCDF 3.1.2, check the package release notes. The v2 equivalent is `new CompoundFile(path, CFSUpdateMode.Update, CFSConfiguration.Default)` — use that as a fallback, calling `.RootStorage.Delete()`, `.RootStorage.AddStream()`, and `.Commit()`.

- [ ] **Step 3: Commit**

```bash
git add RVTuk/RVTuk.Core/Extraction/ThumbnailWriter.cs
git commit -m "feat: ThumbnailWriter — write custom PNG to .rfa OLE SummaryInformation stream"
```

---

## Task 4: GetProjectFamiliesEventHandler

**Files:**
- Create: `RVTuk.Revit/ExternalEvents/GetProjectFamiliesEventHandler.cs`

Reads all `Family` elements from the active document on Revit's main thread.

- [ ] **Step 1: Create handler**

```csharp
// RVTuk.Revit/ExternalEvents/GetProjectFamiliesEventHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.ExternalEvents
{
    public class GetProjectFamiliesEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IReadOnlyList<string> Result { get; private set; } = Array.Empty<string>();

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = Array.Empty<string>(); return; }

                Result = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Select(f => f.Name)
                    .ToList();
            }
            catch
            {
                Result = Array.Empty<string>();
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.GetProjectFamiliesEventHandler";
    }
}
```

- [ ] **Step 2: Build Revit project**

```powershell
dotnet build "RVTuk\RVTuk.Revit\RVTuk.Revit.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add RVTuk/RVTuk.Revit/ExternalEvents/GetProjectFamiliesEventHandler.cs
git commit -m "feat: GetProjectFamiliesEventHandler reads loaded family names from active document"
```

---

## Task 5: LoadFamilyEventHandler

**Files:**
- Create: `RVTuk.Revit/ExternalEvents/LoadFamilyEventHandler.cs`

Loads or reloads a family from a path on disk into the active document.

- [ ] **Step 1: Create handler**

```csharp
// RVTuk.Revit/ExternalEvents/LoadFamilyEventHandler.cs
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.ExternalEvents
{
    public class LoadFamilyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string? FamilyPath { get; set; }
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(string familyPath)
        {
            FamilyPath = familyPath;
            Success = false;
            ErrorMessage = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null || FamilyPath == null)
                {
                    ErrorMessage = "No active document.";
                    return;
                }

                using var tx = new Transaction(doc, "Load Family");
                tx.Start();
                doc.LoadFamily(FamilyPath, new OverwriteLoadOptions(), out _);
                tx.Commit();
                Success = true;
            }
            catch (System.Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.LoadFamilyEventHandler";

        private class OverwriteLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
```

- [ ] **Step 2: Build Revit project**

```powershell
dotnet build "RVTuk\RVTuk.Revit\RVTuk.Revit.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add RVTuk/RVTuk.Revit/ExternalEvents/LoadFamilyEventHandler.cs
git commit -m "feat: LoadFamilyEventHandler wraps Document.LoadFamily with overwrite options"
```

---

## Task 6: Application.cs updates + BrowseLibraryCommand

**Files:**
- Modify: `RVTuk.Revit/Application.cs`
- Create: `RVTuk.Revit/Commands/BrowseLibraryCommand.cs`

- [ ] **Step 1: Update Application.cs**

Add the new static members and wire them up in `OnStartup`. Also add the Browse Library ribbon button.

In `Application.cs`, add these static members after the existing ones:

```csharp
public static GetProjectFamiliesEventHandler GetFamiliesHandler { get; private set; } = null!;
public static ExternalEvent GetFamiliesEvent { get; private set; } = null!;
public static LoadFamilyEventHandler LoadFamilyHandler { get; private set; } = null!;
public static ExternalEvent LoadFamilyEvent { get; private set; } = null!;
public static RVTuk.UI.Views.FamilyBrowserWindow? BrowserWindow { get; set; }
```

In `OnStartup`, after creating `IndexingHandler`/`IndexingEvent`:

```csharp
GetFamiliesHandler = new GetProjectFamiliesEventHandler();
GetFamiliesEvent   = ExternalEvent.Create(GetFamiliesHandler);
LoadFamilyHandler  = new LoadFamilyEventHandler();
LoadFamilyEvent    = ExternalEvent.Create(LoadFamilyHandler);
```

In `CreateRibbon`, add the Browse Library button after the existing buttons:

```csharp
var browseBtn = new PushButtonData(
    "BrowseLibrary",
    "Browse\nLibrary",
    assemblyPath,
    typeof(BrowseLibraryCommand).FullName!)
{
    ToolTip = "Open the family browser to search, load, and manage library families"
};
browseBtn.LargeImage = CreatePlaceholderIcon(32, 0xFFFF8C00);
browseBtn.Image      = CreatePlaceholderIcon(16, 0xFFFF8C00);
panel.AddItem(browseBtn);
```

Also add the using for the new command at the top of Application.cs:

```csharp
using RVTuk.Revit.Commands;
using RVTuk.Revit.ExternalEvents;
```

- [ ] **Step 2: Create BrowseLibraryCommand**

```csharp
// RVTuk.Revit/Commands/BrowseLibraryCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class BrowseLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var config = ConfigManager.LoadConfig();
            if (!ConfigManager.IsConfigured(config))
            {
                var settings = new SettingsWindow();
                settings.ShowDialog();
                config = ConfigManager.LoadConfig();
                if (!ConfigManager.IsConfigured(config))
                    return Result.Cancelled;
            }

            // Bring existing window to front, or open a new one
            if (Application.BrowserWindow != null && Application.BrowserWindow.IsLoaded)
            {
                Application.BrowserWindow.Activate();
                return Result.Succeeded;
            }

            var window = new FamilyBrowserWindow(
                config,
                Application.GetFamiliesHandler,
                Application.GetFamiliesEvent,
                Application.LoadFamilyHandler,
                Application.LoadFamilyEvent);

            Application.BrowserWindow = window;
            window.Show();
            return Result.Succeeded;
        }
    }
}
```

- [ ] **Step 3: Build Revit project**

```powershell
dotnet build "RVTuk\RVTuk.Revit\RVTuk.Revit.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors (UI project will fail since FamilyBrowserWindow doesn't exist yet — that's fine, skip UI build for now).

- [ ] **Step 4: Commit**

```bash
git add RVTuk/RVTuk.Revit/Application.cs RVTuk/RVTuk.Revit/Commands/BrowseLibraryCommand.cs
git commit -m "feat: register GetFamilies/LoadFamily events and Browse Library ribbon button"
```

---

## Task 7: RichTextBoxHelper attached property

**Files:**
- Create: `RVTuk.UI/Controls/RichTextBoxHelper.cs`

WPF's `RichTextBox.Document` is not directly bindable. This attached property bridges a `string` (XAML FlowDocument) to the `RichTextBox`.

- [ ] **Step 1: Create RichTextBoxHelper**

```csharp
// RVTuk.UI/Controls/RichTextBoxHelper.cs
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

namespace RVTuk.UI.Controls
{
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty DocumentXamlProperty =
            DependencyProperty.RegisterAttached(
                "DocumentXaml",
                typeof(string),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnDocumentXamlChanged));

        public static string? GetDocumentXaml(DependencyObject obj)
            => (string?)obj.GetValue(DocumentXamlProperty);

        public static void SetDocumentXaml(DependencyObject obj, string? value)
            => obj.SetValue(DocumentXamlProperty, value);

        private static bool _updating;

        private static void OnDocumentXamlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (_updating || d is not RichTextBox rtb) return;
            _updating = true;
            try
            {
                var xaml = e.NewValue as string;
                if (string.IsNullOrWhiteSpace(xaml))
                {
                    rtb.Document = new FlowDocument();
                }
                else
                {
                    try
                    {
                        var doc = (FlowDocument)XamlReader.Parse(xaml);
                        rtb.Document = doc;
                    }
                    catch
                    {
                        rtb.Document = new FlowDocument();
                    }
                }
            }
            finally
            {
                _updating = false;
            }
        }

        // Call this to read the current document back as a XAML string
        public static string? SerializeDocument(RichTextBox rtb)
        {
            if (rtb.Document == null) return null;
            try
            {
                return XamlWriter.Save(rtb.Document);
            }
            catch { return null; }
        }
    }
}
```

- [ ] **Step 2: Build UI project**

```powershell
dotnet build "RVTuk\RVTuk.UI\RVTuk.UI.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add RVTuk/RVTuk.UI/Controls/RichTextBoxHelper.cs
git commit -m "feat: RichTextBoxHelper attached property for binding XAML FlowDocument to RichTextBox"
```

---

## Task 8: FamilyBrowserItemViewModel + FamilyBrowserViewModel

**Files:**
- Create: `RVTuk.UI/ViewModels/FamilyBrowserItemViewModel.cs`
- Create: `RVTuk.UI/ViewModels/FamilyBrowserViewModel.cs`

- [ ] **Step 1: Create FamilyBrowserItemViewModel**

```csharp
// RVTuk.UI/ViewModels/FamilyBrowserItemViewModel.cs
using System.IO;
using System.Windows.Media.Imaging;
using RVTuk.Core.Models;

namespace RVTuk.UI.ViewModels
{
    public class FamilyBrowserItemViewModel : ViewModelBase
    {
        private VersionStatus _versionStatus;

        public FamilyBrowserItem Model { get; }

        public long Id => Model.Id;
        public string FileName => Model.FileName;
        public string DisplayName => Path.GetFileNameWithoutExtension(Model.FileName);
        public string? Category => Model.Category;
        public string RelativePath => Model.RelativePath;

        public VersionStatus VersionStatus
        {
            get => _versionStatus;
            set
            {
                SetProperty(ref _versionStatus, value);
                OnPropertyChanged(nameof(ShowUpToDate));
                OnPropertyChanged(nameof(ShowUpdateAvailable));
            }
        }

        public bool ShowUpToDate => _versionStatus == VersionStatus.UpToDate;
        public bool ShowUpdateAvailable => _versionStatus == VersionStatus.UpdateAvailable;

        public BitmapSource? Thumbnail { get; }

        public FamilyBrowserItemViewModel(FamilyBrowserItem model)
        {
            Model = model;
            _versionStatus = model.VersionStatus;
            Thumbnail = model.ThumbnailPng != null ? LoadBitmap(model.ThumbnailPng) : null;
        }

        private static BitmapSource? LoadBitmap(byte[] pngData)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(pngData);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelHeight = 40;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
```

- [ ] **Step 2: Create FamilyBrowserViewModel**

```csharp
// RVTuk.UI/ViewModels/FamilyBrowserViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.Core.Database;
using RVTuk.Core.Models;

namespace RVTuk.UI.ViewModels
{
    public class FamilyBrowserViewModel : ViewModelBase, IDisposable
    {
        private readonly AppConfig _config;
        private readonly BrowserRepository _repo;
        private readonly IExternalEventHandler _getFamiliesHandler;
        private readonly ExternalEvent _getFamiliesEvent;
        private readonly IExternalEventHandler _loadFamilyHandler;
        private readonly ExternalEvent _loadFamilyEvent;

        private List<FamilyBrowserItemViewModel> _allItems = new();
        private string _searchText = string.Empty;
        private string? _selectedCategory;
        private FamilyBrowserItemViewModel? _selectedItem;
        private bool _isCheckingVersions;
        private int _outdatedCount;
        private string? _instructionsXaml;
        private List<ParameterModel> _parameters = new();

        public ObservableCollection<FamilyBrowserItemViewModel> FilteredItems { get; } = new();
        public ObservableCollection<string?> Categories { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); ApplyFilter(); }
        }

        public string? SelectedCategory
        {
            get => _selectedCategory;
            set { SetProperty(ref _selectedCategory, value); ApplyFilter(); }
        }

        public FamilyBrowserItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                SetProperty(ref _selectedItem, value);
                LoadDetailAsync(value);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowUpdateInProject));
            }
        }

        public bool HasSelection => _selectedItem != null;
        public bool ShowUpdateInProject => _selectedItem?.VersionStatus == VersionStatus.UpdateAvailable;

        public bool IsCheckingVersions
        {
            get => _isCheckingVersions;
            set => SetProperty(ref _isCheckingVersions, value);
        }

        public int OutdatedCount
        {
            get => _outdatedCount;
            set { SetProperty(ref _outdatedCount, value); OnPropertyChanged(nameof(ShowUpdateAll)); }
        }

        public bool ShowUpdateAll => _outdatedCount > 0;

        public string? InstructionsXaml
        {
            get => _instructionsXaml;
            set => SetProperty(ref _instructionsXaml, value);
        }

        public List<ParameterModel> Parameters
        {
            get => _parameters;
            set => SetProperty(ref _parameters, value);
        }

        public ICommand CheckVersionsCommand { get; }
        public ICommand UpdateAllCommand { get; }
        public ICommand LoadFamilyCommand { get; }
        public ICommand UpdateInProjectCommand { get; }

        // Raised by the VM; the View handles opening the editor window
        public event Action<FamilyBrowserItemViewModel>? EditInfoRequested;
        public ICommand EditInfoCommand { get; }

        public FamilyBrowserViewModel(
            AppConfig config,
            BrowserRepository repo,
            object getFamiliesHandler,
            ExternalEvent getFamiliesEvent,
            object loadFamilyHandler,
            ExternalEvent loadFamilyEvent)
        {
            _config = config;
            _repo = repo;
            // Cast via interface; actual types are in Revit project
            _getFamiliesHandler = (IExternalEventHandler)getFamiliesHandler;
            _getFamiliesEvent = getFamiliesEvent;
            _loadFamilyHandler = (IExternalEventHandler)loadFamilyHandler;
            _loadFamilyEvent = loadFamilyEvent;

            CheckVersionsCommand  = new RelayCommand(CheckVersions, () => !IsCheckingVersions);
            UpdateAllCommand      = new RelayCommand(UpdateAll,     () => OutdatedCount > 0);
            LoadFamilyCommand     = new RelayCommand(LoadSelected,  () => SelectedItem != null);
            UpdateInProjectCommand= new RelayCommand(UpdateSelected,() => ShowUpdateInProject);
            EditInfoCommand       = new RelayCommand(RequestEditInfo, () => SelectedItem != null);

            LoadFamilies();
            LoadCategories();
        }

        private void LoadFamilies()
        {
            _allItems = _repo.GetAllFamilies()
                .Select(f => new FamilyBrowserItemViewModel(f))
                .ToList();
            ApplyFilter();
        }

        private void LoadCategories()
        {
            Categories.Clear();
            Categories.Add(null); // "All"
            foreach (var cat in _repo.GetCategories().Where(c => c != null))
                Categories.Add(cat);
            SelectedCategory = null;
        }

        private void ApplyFilter()
        {
            var search = _searchText.Trim();
            var filtered = _allItems.AsEnumerable();
            if (!string.IsNullOrEmpty(search))
                filtered = filtered.Where(i => i.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));
            if (_selectedCategory != null)
                filtered = filtered.Where(i => i.Category == _selectedCategory);

            FilteredItems.Clear();
            foreach (var item in filtered.OrderBy(i => i.DisplayName))
                FilteredItems.Add(item);
        }

        private void LoadDetailAsync(FamilyBrowserItemViewModel? item)
        {
            if (item == null) { InstructionsXaml = null; Parameters = new List<ParameterModel>(); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var xaml = _repo.GetInstructionsXaml(item.Id);
                var prms = _repo.GetParameters(item.Id);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    InstructionsXaml = xaml;
                    Parameters = prms;
                });
            });
        }

        private void CheckVersions()
        {
            IsCheckingVersions = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // GetProjectFamiliesEventHandler exposes Reset() + WaitForCompletion() + Result
                    // Access via reflection-style cast through a shared interface isn't available
                    // since handler is in Revit project. Use dynamic dispatch.
                    dynamic handler = _getFamiliesHandler;
                    handler.Reset();
                    _getFamiliesEvent.Raise();
                    handler.WaitForCompletion();
                    IReadOnlyList<string> projectFamilies = handler.Result;

                    var projectSet = new HashSet<string>(projectFamilies, StringComparer.OrdinalIgnoreCase);
                    int outdated = 0;

                    foreach (var item in _allItems)
                    {
                        var nameNoExt = Path.GetFileNameWithoutExtension(item.FileName);
                        if (!projectSet.Contains(nameNoExt))
                        {
                            item.VersionStatus = VersionStatus.None;
                            continue;
                        }
                        // Compare disk LastWriteTime with indexed ModifiedDate
                        var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
                        bool fileExists = File.Exists(fullPath);
                        bool isNewer = fileExists &&
                            new FileInfo(fullPath).LastWriteTimeUtc > item.Model.ModifiedDate.AddSeconds(1);

                        item.VersionStatus = isNewer ? VersionStatus.UpdateAvailable : VersionStatus.UpToDate;
                        if (item.VersionStatus == VersionStatus.UpdateAvailable) outdated++;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        OutdatedCount = outdated;
                        OnPropertyChanged(nameof(ShowUpdateInProject));
                    });
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => IsCheckingVersions = false);
                }
            });
        }

        private void LoadSelected() => LoadOrUpdate(SelectedItem);
        private void UpdateSelected() => LoadOrUpdate(SelectedItem);

        private void UpdateAll()
        {
            foreach (var item in _allItems.Where(i => i.VersionStatus == VersionStatus.UpdateAvailable).ToList())
                LoadOrUpdate(item);
        }

        private void LoadOrUpdate(FamilyBrowserItemViewModel? item)
        {
            if (item == null) return;
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                dynamic handler = _loadFamilyHandler;
                handler.Prepare(fullPath);
                _loadFamilyEvent.Raise();
                handler.WaitForCompletion();
                bool success = handler.Success;
                string? error = handler.ErrorMessage;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (success)
                        item.VersionStatus = VersionStatus.UpToDate;
                    else if (!string.IsNullOrEmpty(error))
                        MessageBox.Show($"Failed to load family: {error}", "RVTuk");
                });
            });
        }

        private void RequestEditInfo()
        {
            if (SelectedItem != null)
                EditInfoRequested?.Invoke(SelectedItem);
        }

        public void Dispose() => _repo.Dispose();
    }
}
```

> **Note on dynamic dispatch:** `FamilyBrowserViewModel` lives in the UI project, which has no reference to the Revit project. The handler objects are typed as `IExternalEventHandler` (Revit API, available via the Revit reference in the UI project). The concrete handler properties (`Reset`, `Result`, `Prepare`, etc.) are accessed via `dynamic`. Alternatively, extract a shared interface in Core — but `dynamic` avoids adding a Core dependency on ManualResetEventSlim patterns.

- [ ] **Step 3: Build UI**

```powershell
dotnet build "RVTuk\RVTuk.UI\RVTuk.UI.csproj" -c Release2024
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add RVTuk/RVTuk.UI/ViewModels/FamilyBrowserItemViewModel.cs RVTuk/RVTuk.UI/ViewModels/FamilyBrowserViewModel.cs
git commit -m "feat: FamilyBrowserItemViewModel and FamilyBrowserViewModel with search, filter, version check"
```

---

## Task 9: FamilyBrowserWindow

**Files:**
- Create: `RVTuk.UI/Views/FamilyBrowserWindow.xaml`
- Create: `RVTuk.UI/Views/FamilyBrowserWindow.xaml.cs`

- [ ] **Step 1: Create XAML**

```xml
<!-- RVTuk.UI/Views/FamilyBrowserWindow.xaml -->
<Window x:Class="RVTuk.UI.Views.FamilyBrowserWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:RVTuk.UI.ViewModels"
        xmlns:ctrl="clr-namespace:RVTuk.UI.Controls"
        Title="RVTuk — Family Browser"
        Width="860" Height="600" MinWidth="640" MinHeight="400"
        WindowStartupLocation="CenterScreen">

    <Window.Resources>
        <!-- Version status converters inline -->
        <BooleanToVisibilityConverter x:Key="BoolVis"/>
    </Window.Resources>

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="300" MinWidth="200"/>
            <ColumnDefinition Width="5"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- LEFT PANEL -->
        <Grid Grid.Column="0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Search + Category row -->
            <Grid Grid.Row="0" Margin="4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBox Grid.Column="0" Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                         Margin="0,0,4,0">
                    <TextBox.Style>
                        <Style TargetType="TextBox">
                            <Style.Triggers>
                                <Trigger Property="Text" Value="">
                                    <Setter Property="Background">
                                        <Setter.Value>
                                            <VisualBrush Stretch="None" AlignmentX="Left">
                                                <VisualBrush.Visual>
                                                    <TextBlock Text="🔍 Search..." Foreground="Gray" Margin="4,0,0,0"/>
                                                </VisualBrush.Visual>
                                            </VisualBrush>
                                        </Setter.Value>
                                    </Setter>
                                </Trigger>
                            </Style.Triggers>
                        </Style>
                    </TextBox.Style>
                </TextBox>
                <ComboBox Grid.Column="1" Width="100"
                          ItemsSource="{Binding Categories}"
                          SelectedItem="{Binding SelectedCategory}">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding, TargetNullValue='All'}"/>
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
            </Grid>

            <!-- Version toolbar -->
            <Grid Grid.Row="1" Margin="4,0,4,4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Button Grid.Column="0" Command="{Binding CheckVersionsCommand}"
                        Content="⟳ Check Project Versions" HorizontalAlignment="Left"/>
                <Button Grid.Column="1" Command="{Binding UpdateAllCommand}"
                        Visibility="{Binding ShowUpdateAll, Converter={StaticResource BoolVis}}"
                        Content="{Binding OutdatedCount, StringFormat='↑ Update All ({0})'}"/>
            </Grid>

            <!-- Family list -->
            <ListBox Grid.Row="2"
                     ItemsSource="{Binding FilteredItems}"
                     SelectedItem="{Binding SelectedItem}"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                <ListBox.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:FamilyBrowserItemViewModel}">
                        <Grid Margin="2">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="36"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <Image Grid.Column="0" Source="{Binding Thumbnail}" Width="32" Height="24" Stretch="Uniform"/>
                            <StackPanel Grid.Column="1" Margin="4,0,0,0" VerticalAlignment="Center">
                                <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                <TextBlock Text="{Binding Category}" Foreground="Gray" FontSize="10"/>
                            </StackPanel>
                            <!-- Version badges -->
                            <TextBlock Grid.Column="2" Text="✓" Foreground="Green" VerticalAlignment="Center" Margin="4,0,0,0"
                                       Visibility="{Binding ShowUpToDate, Converter={StaticResource BoolVis}}"/>
                            <Border Grid.Column="2" Background="OrangeRed" CornerRadius="3" Padding="4,1" Margin="4,0,0,0"
                                    Visibility="{Binding ShowUpdateAvailable, Converter={StaticResource BoolVis}}">
                                <TextBlock Text="↑ Update" Foreground="White" FontSize="10"/>
                            </Border>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Grid>

        <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch"/>

        <!-- RIGHT DETAIL PANEL -->
        <Grid Grid.Column="2" Visibility="{Binding HasSelection, Converter={StaticResource BoolVis}}">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Header: thumbnail + name + buttons -->
            <Grid Grid.Row="0" Margin="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Image Grid.Column="0" Source="{Binding SelectedItem.Thumbnail}"
                       Width="120" Height="80" Stretch="Uniform"/>
                <StackPanel Grid.Column="1" Margin="8,0,0,0">
                    <TextBlock Text="{Binding SelectedItem.DisplayName}" FontSize="14" FontWeight="Bold"/>
                    <TextBlock Text="{Binding SelectedItem.Category}" Foreground="Gray" FontSize="11"/>
                    <!-- Outdated banner -->
                    <Border Background="#33CC6622" BorderBrush="OrangeRed" BorderThickness="1"
                            Margin="0,4,0,4" Padding="6,2" CornerRadius="3"
                            Visibility="{Binding ShowUpdateInProject, Converter={StaticResource BoolVis}}">
                        <TextBlock Text="↑ Newer version available in library" Foreground="OrangeRed" FontSize="10"/>
                    </Border>
                    <!-- Action buttons -->
                    <WrapPanel>
                        <Button Content="Load into Project" Command="{Binding LoadFamilyCommand}" Margin="0,0,4,0"/>
                        <Button Content="↑ Update in Project" Command="{Binding UpdateInProjectCommand}" Margin="0,0,4,0"
                                Visibility="{Binding ShowUpdateInProject, Converter={StaticResource BoolVis}}"/>
                        <Button Content="Edit Info" Command="{Binding EditInfoCommand}"/>
                    </WrapPanel>
                </StackPanel>
            </Grid>

            <!-- Tabs -->
            <TabControl Grid.Row="1" Grid.RowSpan="2" Margin="8,0,8,8">
                <TabItem Header="Instructions">
                    <RichTextBox IsReadOnly="True" BorderThickness="0"
                                 ctrl:RichTextBoxHelper.DocumentXaml="{Binding InstructionsXaml}"/>
                </TabItem>
                <TabItem Header="{Binding Parameters.Count, StringFormat='Parameters ({0})'}">
                    <DataGrid ItemsSource="{Binding Parameters}" AutoGenerateColumns="False"
                              IsReadOnly="True" GridLinesVisibility="Horizontal">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Name" Binding="{Binding ParameterName}" Width="*"/>
                            <DataGridTextColumn Header="Type" Binding="{Binding DataType}" Width="100"/>
                            <DataGridCheckBoxColumn Header="Instance" Binding="{Binding IsInstance}" Width="60"/>
                        </DataGrid.Columns>
                    </DataGrid>
                </TabItem>
            </TabControl>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create code-behind**

```csharp
// RVTuk.UI/Views/FamilyBrowserWindow.xaml.cs
using System;
using System.Windows;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.Core.Database;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class FamilyBrowserWindow : Window
    {
        public FamilyBrowserViewModel ViewModel { get; }

        public FamilyBrowserWindow(
            AppConfig config,
            object getFamiliesHandler,
            ExternalEvent getFamiliesEvent,
            object loadFamilyHandler,
            ExternalEvent loadFamilyEvent)
        {
            InitializeComponent();
            var repo = new BrowserRepository(config.IndexDatabasePath);
            ViewModel = new FamilyBrowserViewModel(
                config, repo,
                getFamiliesHandler, getFamiliesEvent,
                loadFamilyHandler, loadFamilyEvent);
            ViewModel.EditInfoRequested += OnEditInfoRequested;
            DataContext = ViewModel;
        }

        private void OnEditInfoRequested(FamilyBrowserItemViewModel item)
        {
            var editor = new InstructionsEditorWindow(item, ViewModel);
            editor.Owner = this;
            editor.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
```

- [ ] **Step 3: Build UI**

```powershell
dotnet build "RVTuk\RVTuk.UI\RVTuk.UI.csproj" -c Release2024
```

Expected: Build succeeded. (InstructionsEditorWindow is referenced but not yet created — add a stub class temporarily if needed.)

- [ ] **Step 4: Commit**

```bash
git add "RVTuk/RVTuk.UI/Views/FamilyBrowserWindow.xaml" "RVTuk/RVTuk.UI/Views/FamilyBrowserWindow.xaml.cs"
git commit -m "feat: FamilyBrowserWindow two-panel layout with search, version badges, and detail panel"
```

---

## Task 10: InstructionsEditorViewModel

**Files:**
- Create: `RVTuk.UI/ViewModels/InstructionsEditorViewModel.cs`

- [ ] **Step 1: Create InstructionsEditorViewModel**

```csharp
// RVTuk.UI/ViewModels/InstructionsEditorViewModel.cs
using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RVTuk.Core.Database;
using RVTuk.Core.Extraction;

namespace RVTuk.UI.ViewModels
{
    public class InstructionsEditorViewModel : ViewModelBase
    {
        private readonly BrowserRepository _repo;
        private readonly long _familyId;
        private readonly string _rfaFullPath;

        private string? _instructionsXaml;
        private byte[]? _customThumbPng;
        private bool _oleSynced;
        private BitmapSource? _thumbnailSource;
        private string _thumbStatus = string.Empty;

        public string FamilyDisplayName { get; }

        public string? InstructionsXaml
        {
            get => _instructionsXaml;
            set => SetProperty(ref _instructionsXaml, value);
        }

        public BitmapSource? ThumbnailSource
        {
            get => _thumbnailSource;
            private set => SetProperty(ref _thumbnailSource, value);
        }

        public string ThumbStatus
        {
            get => _thumbStatus;
            private set => SetProperty(ref _thumbStatus, value);
        }

        public bool OleSynced
        {
            get => _oleSynced;
            private set { SetProperty(ref _oleSynced, value); OnPropertyChanged(nameof(CanUpdateOle)); }
        }

        public bool CanUpdateOle => _customThumbPng != null && !_oleSynced;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ReplaceThumbnailCommand { get; }
        public ICommand ResetThumbnailCommand { get; }
        public ICommand UpdateOleCommand { get; }

        public event Action? CloseRequested;

        public InstructionsEditorViewModel(
            long familyId,
            string familyFileName,
            string rfaFullPath,
            string? currentXaml,
            BrowserRepository repo)
        {
            _repo = repo;
            _familyId = familyId;
            _rfaFullPath = rfaFullPath;
            FamilyDisplayName = Path.GetFileNameWithoutExtension(familyFileName);
            _instructionsXaml = currentXaml;

            SaveCommand             = new RelayCommand(Save);
            CancelCommand           = new RelayCommand(() => CloseRequested?.Invoke());
            ReplaceThumbnailCommand = new RelayCommand(ReplaceThumbnail);
            ResetThumbnailCommand   = new RelayCommand(ResetThumbnail);
            UpdateOleCommand        = new RelayCommand(UpdateOle, () => CanUpdateOle);

            LoadThumbnailState();
        }

        private void LoadThumbnailState()
        {
            var (customPng, oleSynced) = _repo.GetCustomThumbnail(_familyId);
            if (customPng != null)
            {
                _customThumbPng = customPng;
                _oleSynced = oleSynced;
                ThumbnailSource = ToBitmapSource(customPng, 80, 64);
                ThumbStatus = oleSynced ? "● Custom thumbnail" : "● Custom thumbnail · .rfa out of sync";
            }
            else
            {
                var olePng = _repo.GetOleThumbnail(_familyId);
                ThumbnailSource = olePng != null ? ToBitmapSource(olePng, 80, 64) : null;
                ThumbStatus = "● System thumbnail";
                _oleSynced = true;
            }
        }

        public void SetThumbnailFromBytes(byte[] pngData)
        {
            _customThumbPng = pngData;
            _oleSynced = false;
            ThumbnailSource = ToBitmapSource(pngData, 80, 64);
            ThumbStatus = "● Custom thumbnail · .rfa out of sync";
            OnPropertyChanged(nameof(CanUpdateOle));
        }

        private void ReplaceThumbnail()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Title  = "Replace Thumbnail"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var png = ConvertToPng(File.ReadAllBytes(dlg.FileName));
                SetThumbnailFromBytes(png);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not load image: {ex.Message}");
            }
        }

        private void ResetThumbnail()
        {
            _customThumbPng = null;
            _oleSynced = true;
            var olePng = _repo.GetOleThumbnail(_familyId);
            ThumbnailSource = olePng != null ? ToBitmapSource(olePng, 80, 64) : null;
            ThumbStatus = "● System thumbnail";
            OnPropertyChanged(nameof(CanUpdateOle));
        }

        private void UpdateOle()
        {
            if (_customThumbPng == null) return;
            bool ok = ThumbnailWriter.WriteThumbnailToRfa(_rfaFullPath, _customThumbPng);
            if (ok)
            {
                _oleSynced = true;
                ThumbStatus = "● Custom thumbnail";
                _repo.SetOleSynced(_familyId, true);
                OnPropertyChanged(nameof(CanUpdateOle));
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "Could not write to the .rfa file. The file may be read-only or locked.",
                    "RVTuk");
            }
        }

        private void Save(string? xamlFromEditor)
        {
            // xamlFromEditor is passed from the code-behind after serializing the RichTextBox
            _repo.SaveInstructionsXaml(_familyId, xamlFromEditor);

            if (_customThumbPng != null)
            {
                bool oleOk = ThumbnailWriter.WriteThumbnailToRfa(_rfaFullPath, _customThumbPng);
                _repo.SaveCustomThumbnail(_familyId, _customThumbPng, oleOk);
            }
            else
            {
                // Reset was clicked — delete custom thumbnail from DB
                _repo.DeleteCustomThumbnail(_familyId);
            }

            CloseRequested?.Invoke();
        }

        // Called from the Save command via XAML binding; overload with no param triggers code-behind
        private void Save() { /* code-behind calls Save(string?) directly */ }

        private static byte[] ConvertToPng(byte[] rawBytes)
        {
            using var ms = new MemoryStream(rawBytes);
            using var bmp = System.Drawing.Image.FromStream(ms);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
            return pngMs.ToArray();
        }

        private static BitmapSource? ToBitmapSource(byte[] pngData, int decodeW, int decodeH)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource     = new MemoryStream(pngData);
                bmp.CacheOption      = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeW;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
```

- [ ] **Step 2: Build UI**

```powershell
dotnet build "RVTuk\RVTuk.UI\RVTuk.UI.csproj" -c Release2024
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add RVTuk/RVTuk.UI/ViewModels/InstructionsEditorViewModel.cs
git commit -m "feat: InstructionsEditorViewModel with thumbnail ⁝ menu, OLE sync detection, and save"
```

---

## Task 11: InstructionsEditorWindow

**Files:**
- Create: `RVTuk.UI/Views/InstructionsEditorWindow.xaml`
- Create: `RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs`

- [ ] **Step 1: Create XAML**

```xml
<!-- RVTuk.UI/Views/InstructionsEditorWindow.xaml -->
<Window x:Class="RVTuk.UI.Views.InstructionsEditorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ctrl="clr-namespace:RVTuk.UI.Controls"
        Title="Edit Family Info"
        Width="640" Height="560" MinWidth="480" MinHeight="400"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False">

    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis"/>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- header: thumb + name -->
            <RowDefinition Height="Auto"/>  <!-- rich text toolbar -->
            <RowDefinition Height="*"/>     <!-- editor body -->
            <RowDefinition Height="Auto"/>  <!-- footer -->
        </Grid.RowDefinitions>

        <!-- HEADER -->
        <Grid Grid.Row="0" Margin="10" Background="#16213e">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Thumbnail with ⁝ menu -->
            <Grid Grid.Column="0" Width="84" Height="68">
                <Border BorderThickness="2" CornerRadius="4">
                    <Border.Style>
                        <Style TargetType="Border">
                            <Setter Property="BorderBrush" Value="#333"/>
                            <Style.Triggers>
                                <!-- Green = custom + in sync (HasCustom=true, OleSynced=true) -->
                                <!-- Orange = custom + out of sync (CanUpdateOle=true) -->
                                <DataTrigger Binding="{Binding CanUpdateOle}" Value="True">
                                    <Setter Property="BorderBrush" Value="OrangeRed"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Border.Style>
                    <Image Source="{Binding ThumbnailSource}" Stretch="Uniform"
                           AllowDrop="True" x:Name="ThumbnailImage"/>
                </Border>
                <!-- Three-dot menu button -->
                <Button Content="⁝" HorizontalAlignment="Right" VerticalAlignment="Top"
                        Width="18" Height="18" FontSize="12" Padding="0"
                        Margin="0,2,2,0"
                        Click="ThumbMenuButton_Click"/>
                <ContextMenu x:Name="ThumbContextMenu">
                    <MenuItem Header="🖼 Replace…"         Command="{Binding ReplaceThumbnailCommand}"/>
                    <MenuItem Header="↺ Reset to system original" Command="{Binding ResetThumbnailCommand}"/>
                    <Separator/>
                    <MenuItem Header="↑ Update .rfa with DB image"
                              Command="{Binding UpdateOleCommand}"
                              IsEnabled="{Binding CanUpdateOle}"/>
                </ContextMenu>
            </Grid>

            <StackPanel Grid.Column="1" Margin="10,4,0,4" VerticalAlignment="Center">
                <TextBlock Text="{Binding FamilyDisplayName}" FontSize="13" FontWeight="Bold"/>
                <TextBlock Text="{Binding ThumbStatus}" FontSize="10" Foreground="Gray" Margin="0,2,0,0"/>
            </StackPanel>
        </Grid>

        <!-- RICH TEXT TOOLBAR -->
        <ToolBar Grid.Row="1">
            <Button Content="B"  FontWeight="Bold"   Click="Bold_Click"/>
            <Button Content="I"  FontStyle="Italic"  Click="Italic_Click"/>
            <Button Content="U"  Click="Underline_Click">
                <Button.ContentTemplate>
                    <DataTemplate>
                        <TextBlock Text="U" TextDecorations="Underline"/>
                    </DataTemplate>
                </Button.ContentTemplate>
            </Button>
            <Separator/>
            <Button Content="H1" Click="H1_Click"/>
            <Button Content="H2" Click="H2_Click"/>
            <Button Content="≡ List" Click="List_Click"/>
            <Separator/>
            <Button Content="🖼 Add Image" Click="AddImage_Click"/>
        </ToolBar>

        <!-- EDITOR BODY -->
        <RichTextBox Grid.Row="2" x:Name="Editor" Margin="8" AcceptsReturn="True"
                     VerticalScrollBarVisibility="Auto"
                     AllowDrop="True"
                     ctrl:RichTextBoxHelper.DocumentXaml="{Binding InstructionsXaml}"/>

        <!-- FOOTER -->
        <Grid Grid.Row="3" Margin="8">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="Cancel" Command="{Binding CancelCommand}" Width="70" Margin="0,0,8,0"/>
                <Button Content="Save"   Click="Save_Click"                Width="70"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create code-behind**

```csharp
// RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RVTuk.Core.Config;
using RVTuk.UI.Controls;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class InstructionsEditorWindow : Window
    {
        public InstructionsEditorViewModel ViewModel { get; }

        public InstructionsEditorWindow(FamilyBrowserItemViewModel item, FamilyBrowserViewModel browser)
        {
            InitializeComponent();

            // Resolve full path
            var config = RVTuk.Core.Config.ConfigManager.LoadConfig();
            var fullPath = Path.Combine(config.LibraryFolderPath, item.RelativePath);
            var xaml = browser.InstructionsXaml; // already loaded by browser VM

            ViewModel = new InstructionsEditorViewModel(
                item.Id, item.FileName, fullPath, xaml, browser._repo);

            ViewModel.CloseRequested += () =>
            {
                // Refresh the browser's detail panel after save
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Re-select item to reload instructions
                    var sel = browser.SelectedItem;
                    browser.SelectedItem = null;
                    browser.SelectedItem = sel;
                });
                Close();
            };

            DataContext = ViewModel;

            // Wire up drag-drop to thumbnail
            ThumbnailImage.Drop += ThumbnailImage_Drop;
            ThumbnailImage.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            // Wire up drag-drop to editor body
            Editor.Drop += Editor_Drop;
        }

        private void ThumbMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ThumbContextMenu.PlacementTarget = (UIElement)sender;
            ThumbContextMenu.DataContext = ViewModel;
            ThumbContextMenu.IsOpen = true;
        }

        private void ThumbnailImage_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadThumbnailFromFile(files[0]);
            }
            else if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                var bmp = (System.Drawing.Bitmap)e.Data.GetData(DataFormats.Bitmap);
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ViewModel.SetThumbnailFromBytes(ms.ToArray());
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Paste image from clipboard if thumbnail has focus, else let RichTextBox handle it
                if (ThumbnailImage.IsMouseOver && Clipboard.ContainsImage())
                {
                    var bmpSrc = Clipboard.GetImage();
                    if (bmpSrc != null)
                    {
                        ViewModel.SetThumbnailFromBytes(BitmapSourceToPng(bmpSrc));
                        e.Handled = true;
                    }
                }
            }
        }

        private void Editor_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
                        InsertImageIntoEditor(File.ReadAllBytes(file));
                }
                e.Handled = true;
            }
        }

        private void LoadThumbnailFromFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var png = ConvertToPng(bytes);
                ViewModel.SetThumbnailFromBytes(png);
            }
            catch (Exception ex) { MessageBox.Show($"Could not load image: {ex.Message}"); }
        }

        private void InsertImageIntoEditor(byte[] pngData)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(pngData);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                var image = new Image { Source = bmp, MaxWidth = 400 };
                var container = new InlineUIContainer(image, Editor.CaretPosition);
                _ = container; // insertion is done by InlineUIContainer constructor
            }
            catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Serialize current editor content before calling Save
            var xaml = RichTextBoxHelper.SerializeDocument(Editor);
            ViewModel.InstructionsXaml = xaml;
            // Trigger save through the command (call internal method via a workaround)
            // Since Save(string?) is private, we use a public entry point on the VM:
            ViewModel.ExecuteSave(xaml);
        }

        // Formatting toolbar handlers
        private void Bold_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        private void Italic_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
        private void Underline_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
        private void H1_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 22.0);
        private void H2_Click(object sender, RoutedEventArgs e) =>
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 16.0);
        private void List_Click(object sender, RoutedEventArgs e)
        {
            var para = Editor.CaretPosition.Paragraph;
            if (para != null)
            {
                var list = new List(new ListItem(para));
                Editor.Document.Blocks.Add(list);
            }
        }
        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
                { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true)
                InsertImageIntoEditor(File.ReadAllBytes(dlg.FileName));
        }

        private static byte[] ConvertToPng(byte[] rawBytes)
        {
            using var ms = new MemoryStream(rawBytes);
            using var bmp = System.Drawing.Image.FromStream(ms);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
            return pngMs.ToArray();
        }

        private static byte[] BitmapSourceToPng(BitmapSource bmpSrc)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmpSrc));
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
```

> **Note:** The `Save_Click` handler calls `ViewModel.ExecuteSave(xaml)`. Rename the private `Save(string?)` method in `InstructionsEditorViewModel` to `public void ExecuteSave(string? xaml)` so the code-behind can call it.

- [ ] **Step 3: Update InstructionsEditorViewModel — rename Save to ExecuteSave**

In `InstructionsEditorViewModel.cs`, rename the `private void Save(string? xamlFromEditor)` method to `public void ExecuteSave(string? xamlFromEditor)`, and update the `private void Save()` stub:

```csharp
// Remove the parameterless private Save() stub entirely.
// Rename:
public void ExecuteSave(string? xamlFromEditor)
{
    _repo.SaveInstructionsXaml(_familyId, xamlFromEditor);
    if (_customThumbPng != null)
    {
        bool oleOk = ThumbnailWriter.WriteThumbnailToRfa(_rfaFullPath, _customThumbPng);
        _repo.SaveCustomThumbnail(_familyId, _customThumbPng, oleOk);
    }
    else
    {
        _repo.DeleteCustomThumbnail(_familyId);
    }
    CloseRequested?.Invoke();
}
// Update SaveCommand to a no-op (save is triggered from code-behind via Save_Click):
SaveCommand = new RelayCommand(() => { }); // actual save goes through ExecuteSave
```

Also update `BrowserRepository` field access: since `FamilyBrowserWindow` references `browser._repo` in `InstructionsEditorWindow`, add `internal BrowserRepository _repo;` as an accessible field or expose it via a property on `FamilyBrowserViewModel`.

Change in `FamilyBrowserViewModel`:
```csharp
internal BrowserRepository Repo => _repo;
```

And in `InstructionsEditorWindow`:
```csharp
var browser = (FamilyBrowserViewModel)/* ... */;
// Use browser.Repo instead of browser._repo
```

- [ ] **Step 4: Build all three projects**

```powershell
dotnet build "RVTuk\RVTuk.Revit\RVTuk.Revit.csproj" -c Release2024
dotnet build "RVTuk\RVTuk.Revit\RVTuk.Revit.csproj" -c Release2023
dotnet build "RVTuk\RVTuk.Revit\RVTuk.Revit.csproj" -c Release2025
```

Expected: All three succeed, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add "RVTuk/RVTuk.UI/Views/InstructionsEditorWindow.xaml" "RVTuk/RVTuk.UI/Views/InstructionsEditorWindow.xaml.cs" RVTuk/RVTuk.UI/ViewModels/InstructionsEditorViewModel.cs
git commit -m "feat: InstructionsEditorWindow with rich text, thumbnail ⁝ menu, drag-drop, and OLE sync"
```

---

## Task 12: Deploy and smoke test

- [ ] **Step 1: Run Deploy.ps1 as Administrator**

```powershell
# In an elevated PowerShell session from the repo root:
.\Deploy.ps1
```

Expected: DLLs deployed to `C:\Users\danie\AppData\Roaming\Autodesk\Revit\Addins\RVTuk\` for each version.

- [ ] **Step 2: Start Revit 2024, open any project**

- [ ] **Step 3: Smoke test — Browse Library**

1. Click **Browse Library** ribbon button → `FamilyBrowserWindow` opens as non-modal floating window.
2. Verify family list loads with thumbnails.
3. Search for a name → list filters live.
4. Select a family → detail panel shows name, category, Instructions tab, Parameters tab.

- [ ] **Step 4: Smoke test — Load into Project**

1. Select a family in the browser.
2. Click **Load into Project**.
3. In Revit: `Project Browser → Families` — verify the family appears.

- [ ] **Step 5: Smoke test — Check Versions**

1. Click **⟳ Check Project Versions**.
2. Families loaded in the open project get ✓ or ↑ badges.
3. Families not in the project have no badge.

- [ ] **Step 6: Smoke test — Edit Info**

1. Select a family, click **Edit Info** → `InstructionsEditorWindow` opens modal.
2. Type some text in the editor, apply Bold.
3. Click **🖼 Add Image**, pick a PNG.
4. Click **Save** → window closes.
5. Back in browser: select same family → Instructions tab shows saved content.

- [ ] **Step 7: Smoke test — Thumbnail replace**

1. Open **Edit Info** for a family.
2. Click **⁝** → **Replace…** → pick an image.
3. Thumbnail preview updates with orange border (out of sync).
4. Click **Save** → window closes.
5. Reopen **Edit Info** → thumbnail shows the custom image, border is green (synced).

- [ ] **Step 8: Final commit**

```bash
git add -A
git commit -m "feat: family browser — complete implementation (browser, editor, version check, OLE thumbnail write)"
```

---

---

## Self-Review Corrections

Apply these fixes during implementation — they correct gaps found in the post-write review.

### Fix 1: Architecture — UI project must not reference Revit API types

`FamilyBrowserViewModel` was written taking `ExternalEvent` (a Revit API type). The UI project has no Revit dependency and must stay that way.

**Fix:** Replace `ExternalEvent`/handler parameters with delegate types in `FamilyBrowserViewModel` and `FamilyBrowserWindow`. `BrowseLibraryCommand` (Revit project) creates the lambdas.

Change `FamilyBrowserViewModel` constructor signature to:

```csharp
public FamilyBrowserViewModel(
    AppConfig config,
    BrowserRepository repo,
    Func<IReadOnlyList<string>> getProjectFamilies,   // raises event + blocks + returns names
    Func<string, (bool Success, string? Error)> loadFamily) // raises event + blocks + returns result
```

Remove the `_getFamiliesHandler`, `_getFamiliesEvent`, `_loadFamilyHandler`, `_loadFamilyEvent` fields and the `dynamic` dispatch. Replace `CheckVersions` inner calls with:

```csharp
IReadOnlyList<string> projectFamilies = _getProjectFamilies(); // blocks on bg thread
```

And `LoadOrUpdate`:

```csharp
var (success, error) = _loadFamily(fullPath);
```

Change `FamilyBrowserWindow` constructor:

```csharp
public FamilyBrowserWindow(
    AppConfig config,
    Func<IReadOnlyList<string>> getProjectFamilies,
    Func<string, (bool Success, string? Error)> loadFamily)
{
    InitializeComponent();
    var repo = new BrowserRepository(config.IndexDatabasePath);
    ViewModel = new FamilyBrowserViewModel(config, repo, getProjectFamilies, loadFamily);
    ...
}
```

Change `BrowseLibraryCommand` to create the lambdas (this is in the Revit project so it CAN reference ExternalEvent):

```csharp
Func<IReadOnlyList<string>> getProjectFamilies = () =>
{
    Application.GetFamiliesHandler.Reset();
    Application.GetFamiliesEvent.Raise();
    Application.GetFamiliesHandler.WaitForCompletion();
    return Application.GetFamiliesHandler.Result;
};

Func<string, (bool, string?)> loadFamily = path =>
{
    Application.LoadFamilyHandler.Prepare(path);
    Application.LoadFamilyEvent.Raise();
    Application.LoadFamilyHandler.WaitForCompletion();
    return (Application.LoadFamilyHandler.Success, Application.LoadFamilyHandler.ErrorMessage);
};

var window = new FamilyBrowserWindow(config, getProjectFamilies, loadFamily);
```

### Fix 2: Inline image ✕ remove button

The spec requires inline instruction images to have a ✕ remove button. Wrap each inserted image in an `InlineUIContainer` using a `Grid` that overlays the button:

Replace `InsertImageIntoEditor` in `InstructionsEditorWindow.xaml.cs`:

```csharp
private void InsertImageIntoEditor(byte[] pngData)
{
    try
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(pngData);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        var image  = new Image { Source = bmp, MaxWidth = 400, Stretch = Stretch.Uniform };
        var remove = new Button
        {
            Content = "✕", Width = 20, Height = 20, Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Foreground = Brushes.Red, Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        };
        var grid = new Grid();
        grid.Children.Add(image);
        grid.Children.Add(remove);

        var container = new InlineUIContainer(grid, Editor.CaretPosition);
        remove.Click += (_, _) =>
        {
            // Find and remove the InlineUIContainer from its parent paragraph
            var para = container.Parent as Paragraph;
            para?.Inlines.Remove(container);
        };
    }
    catch { }
}
```

### Fix 3: Ctrl+V paste for inline images in the editor body

Add to `InstructionsEditorWindow.xaml.cs` — handle paste in the editor (the RichTextBox's `PreviewKeyDown`):

```csharp
// In the constructor, after InitializeComponent():
Editor.PreviewKeyDown += Editor_PreviewKeyDown;

// New handler:
private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
    {
        if (Clipboard.ContainsImage())
        {
            var bmpSrc = Clipboard.GetImage();
            if (bmpSrc != null)
            {
                InsertImageIntoEditor(BitmapSourceToPng(bmpSrc));
                e.Handled = true; // prevent default paste
            }
        }
        // If not an image, let RichTextBox handle normal text paste
    }
}
```

### Fix 4: BrowserRepository access in InstructionsEditorWindow

`InstructionsEditorWindow` needs the `BrowserRepository`. Pass it explicitly rather than accessing a private field on the VM:

Change `FamilyBrowserViewModel`:
```csharp
public BrowserRepository Repo => _repo; // add internal accessor
```

In `FamilyBrowserWindow.OnEditInfoRequested`:
```csharp
private void OnEditInfoRequested(FamilyBrowserItemViewModel item)
{
    var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
    var editor = new InstructionsEditorWindow(
        item, ViewModel.InstructionsXaml, fullPath, ViewModel.Repo);
    editor.Owner = this;
    editor.ShowDialog();
    // Refresh detail after close
    var sel = ViewModel.SelectedItem;
    ViewModel.SelectedItem = null;
    ViewModel.SelectedItem = sel;
}
```

Store `_config` on `FamilyBrowserWindow` as a field:
```csharp
private readonly AppConfig _config;
// assign in constructor: _config = config;
```

---

## Known Issues / Follow-ups

- **OpenMCDF write API**: `RootStorage.Open(path, FileAccess.ReadWrite)` may differ in v3.1.2. If it doesn't exist, fall back to the v2 `CompoundFile` API: `new CompoundFile(path, CFSUpdateMode.Update, CFSConfiguration.Default)` → `.RootStorage.Delete(...)` / `.RootStorage.AddStream(...)` / `.Commit()`.
- **`dynamic` dispatch**: If the build rejects `dynamic` in the net48 targets (unlikely, `dynamic` is available on net48), extract a shared interface in Core: `IExternalEventLike` with `Reset()`, `WaitForCompletion()`, and a `Result`/`Success` property.
- **FlowDocument with embedded images grows large**: For large documents, consider storing inline images as separate `InstructionImages` rows (as noted in the spec's future section) rather than base64 in the XAML column.
- **OLE sync detection on indexer re-run**: Currently `OleSynced` is set to `0` only when the write fails. Future: on each index run, detect when Revit regenerated the OLE thumbnail and set `OleSynced = 0` automatically.
