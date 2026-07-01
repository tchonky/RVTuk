# Family Explorer — Design Spec
**Date:** 2026-06-22
**Status:** Approved

Evolution of the existing **Family Browser** (see `2026-06-15-family-browser-design.md`).
Three additive features plus one infrastructure change. Not a new parallel tool.

---

## Context

The Family Browser already provides search/filter, load/update into the project, version
checking, a single per-family thumbnail (OLE or custom), a rich-text instructions editor,
and a basic 3-column parameter table (Name / DataType / Instance). This spec makes browsing
richer and the database safe on a shared network path.

Decisions locked during brainstorming:
- **Evolve** the existing Family Browser; do not build a separate tool.
- **Parameters: view/audit only.** Show all metadata (group + System/Shared/Family); no
  write-back to families. Live text filter over the parameter rows.
- **Concurrency: admin writes, everyone reads.** One person runs deep scans / edits;
  everyone else browses. Optimize for 1 writer + many readers over a network share.
- **Images: a gallery** — multiple admin-added images per family, each with a caption.
  Image *files* on disk, metadata (caption/order) in the DB. Per-row list thumbnail stays a
  DB BLOB.
- **UI styling deferred** to a later iteration; deliver data + commands hung on existing
  layout regions (Parameters tab, detail panel, editor window).

Intended outcome: each family shows a captioned image gallery; a complete, filterable
parameter list distinguishes system vs. shared parameters and their "group under" assignment
(so disorganized families are easy to spot and fix manually in Revit); the database lives on
a shared network path with many simultaneous read-only users.

---

## 1. Parameter audit (view-only)

Capture and display each family parameter's *group*, *kind* (System / Shared / Family),
instance/type flag, data type, GUID, and formula; filter the list by typing.

### Extraction change (core of the work)
`FamilyMetadataExtractor.ParseAtomXml` (`src/RVTuk.Revit/Extraction/`) reads
`Application.ExtractPartAtomFromFamilyFile` — a fast XML peek yielding only name / datatype /
isInstance, with **no** group or shared/system info.

Replace with `FamilyManager`-based extraction that opens the family document on Revit's main
thread (the deep-scan handler `IndexingExternalEventHandler.Execute` already runs per-family
on that thread). For each `FamilyParameter` from `doc.FamilyManager.Parameters`:
- `IsInstance` (already captured)
- `IsShared` → **Kind = Shared**
- non-shared with a built-in/internal definition (maps to a `BuiltInParameter`) →
  **Kind = System**; otherwise → **Kind = Family**
- group label from `Definition.GetGroupTypeId()` (Revit 2022+), `BuiltInParameterGroup`
  fallback on the older API surface — store the human-readable label
- `GUID` (shared only; nullable)
- `Formula` (nullable)
- data type from `Definition.GetDataType()` / storage type → string

Thumbnail extraction stays OLE-based; only the parameter/category path moves to doc-open.

**Trade-off (accepted):** deep scan gets slower (opens every `.rfa` instead of the PartAtom
peek). Admin-only and occasional, runs in the background on Revit's thread. Worth a progress
note in the scan UI.

### Data model + DB
Extend `ParameterModel` (`src/RVTuk.Core/Models/ParameterModel.cs`) with `ParamGroup`,
`Kind`, `Guid`, `Formula` (nullable strings; `Kind` may be an enum). Migrate the `Parameters`
table on open (same pattern as the existing `InstructionsXaml` migration in
`IndexRepository.cs`):

```sql
ALTER TABLE Parameters ADD COLUMN ParamGroup TEXT;
ALTER TABLE Parameters ADD COLUMN Kind       TEXT;   -- System | Shared | Family
ALTER TABLE Parameters ADD COLUMN Guid       TEXT;
ALTER TABLE Parameters ADD COLUMN Formula    TEXT;
```

Update writes in `IndexRepository.UpdateFamilyMetadata` and reads in
`BrowserRepository.GetParameters` to round-trip the new columns.

### UI (functional, styling deferred)
`FamilyBrowserWindow.xaml` Parameters tab: add **Group** and **Kind** columns (keep Name /
Type / Instance); optionally group by Group so "Other"/ungrouped params stand out. Add a
**filter textbox** above the grid bound to a new `FamilyBrowserViewModel` property that
live-filters rows by substring (name/group/kind), mirroring the existing family-list search.

---

## 2. Image gallery per family

Multiple admin-added images per family, each with a caption; shown in the detail panel,
managed in the editor.

### Storage (hybrid)
- **Files on disk:** `{LibraryFolder}\.Setup\Gallery\{familyId}\NN.png` (under the hidden
  `.Setup` folder that already holds the DB). Convert inputs (PNG/JPG/BMP) to PNG, reusing
  the existing `ConvertToPng` helper used for custom thumbnails.
- **Metadata in DB** (new table, on-open migration):

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

- Per-row list thumbnail stays a DB BLOB (`Thumbnail`/`CustomThumbnail`) — read for every
  list row, so it stays in the DB to avoid thousands of tiny network file reads.
- Missing/orphaned files render a placeholder and are skipped silently (admin-curated share).

### Code
- New `BrowserRepository` methods: `GetImages`, `AddImage`, `UpdateCaption`, `DeleteImage`,
  `ReorderImages`. Writes copy/delete the file under `.Setup\Gallery\{familyId}\` and upsert
  the row.
- New `FamilyImage` model in Core; a small `GalleryItemViewModel` (path → `BitmapSource`,
  lazy, only for the selected family).
- Detail panel: gallery region bound to the selected family's images (strip or grid; form
  deferred).
- `InstructionsEditorWindow`: add/remove/caption/reorder controls + commands (file picker,
  drag-drop, paste), reusing the editor's existing image-input handling.

---

## 3. Network share + concurrency (1 writer / many readers)

Let the DB live on a shared network path, read by many users at once, written only by admin.

- **Remove the UNC block** in `IndexRepository.cs` (the `databasePath.StartsWith(@"\\")`
  guard) so `\\server\share\...` is allowed.
- **Stop using WAL on the shared file.** WAL relies on host-local shared memory and is not
  safe across machines on a network filesystem (almost certainly why UNC was blocked).
  Switch journal mode to rollback (`PRAGMA journal_mode=TRUNCATE;` or `DELETE;`).
- **Add `PRAGMA busy_timeout=5000;`** on every connection so brief admin writes don't error
  out concurrent readers.
- **Open browse connections read-only** (`SqliteOpenMode.ReadOnly` / `Read Only=True`). Only
  the scan/edit path opens read-write. Make `BrowserRepository` open read-only; route the
  caption/gallery/instruction *writes* through a short-lived read-write connection opened per
  edit commit, then closed. Preserves "many readers, one writer."

Apply the same PRAGMA/journal settings in both `IndexRepository` and `BrowserRepository`
(net48 `System.Data.SQLite` and net8 `Microsoft.Data.Sqlite` branches).

---

## Verification

1. **Build** all three configs: `dotnet build RVTuk.sln -c Release2024` (and
   `Release2023`, `Release2025`).
2. **Deploy** (`Deploy.ps1`, elevated) and restart Revit.
3. **Parameter audit:** library folder with shared/system/family params in various groups
   (incl. "Other"); deep scan; confirm Parameters tab shows correct Group + Kind, and the
   filter textbox narrows rows live.
4. **Gallery:** add several captioned images to a family; confirm files land in
   `…\.Setup\Gallery\{familyId}\`, captions/order persist, detail panel shows them, delete
   removes file + row, and a manually deleted file shows a placeholder without error.
5. **Concurrency:** DB on `\\server\share\…`; browse read-only from two machines while admin
   runs a deep scan; confirm readers keep working (no "database is locked") and see updated
   data after the scan.

## Out of scope (future)
- Editing/reorganizing parameters from the tool (write-back to `.rfa`).
- Auto-generating gallery images from the `.rfa` (rendered views).
- Auto-flagging rules for "disorganized" params (this iteration only *shows* + filters).
- UI styling/layout polish for the new regions.
