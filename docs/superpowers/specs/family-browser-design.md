# Family Browser — Design (consolidated)

**Consolidated:** 2026-07-01
**Status:** Approved / largely shipped
**Area:** Family Management (VISION pillar 1)

> Single source of truth for the Family Browser. Consolidates two earlier specs — the base
> browser (`2026-06-15`) and the "Family Explorer" enhancement batch (`2026-06-22`, parameter
> audit + gallery + network concurrency). Those originals now live under
> [`docs/archive/`](../../archive/). "Family Explorer" was a codename for round 2 of this same
> product; the product name is **Family Browser**.
>
> See **Deviations since design (as built)** at the end for where the shipped code differs.

---

## Overview

A non-modal floating WPF window that lets users search the indexed family library, load
families into the open Revit project, check for outdated families in the project, view/author
rich instructions per family, browse a per-family image gallery, and audit family parameters.
The shared database can live on a network path read by many users at once and written only by
an admin.

---

## 1. Main Browser Window

### Layout: two-panel (list + detail)

```
┌─────────────────────────────────────────────────────────────┐
│ RVTuk — Family Browser          [Furniture › Seating]       │
├──────────────────────┬──────────────────────────────────────┤
│ 🔍 Search...  All ▾  │  [thumbnail]  Chair - Dining         │
├──────────────────────│  Furniture › Seating                 │
│ ⟳ Check Versions     │  ↑ Newer version available           │
│              ↑ Upd All│  [Load into Project] [↑ Update] [Edit Info] │
├──────────────────────┼──────────────────────────────────────┤
│ ▌ Chair - Dining  ↑  │  Instructions │ Parameters (6) │ Gallery │
│   Chair - Office  ✓  ├──────────────────────────────────────┤
│   Sofa - 3-Seat   ↑  │  Place in a 3D view...              │
│   Stool - Bar        │  [plan img]  [3D img]               │
└──────────────────────┴──────────────────────────────────────┘
```

**Window behaviour:** non-modal, resizable, stays open while the user works in Revit; user
closes it manually.

### Left panel
- **Search bar** — filters by family name (live, case-insensitive; multi-word: all tokens
  match in any order). Also matches tags.
- **Category dropdown** — "All" or a specific Revit category.
- **Version / Sync controls** — a check that marks which families are in the active project
  and which are outdated (§3), plus an `↑ Update All (N)` action shown only when N > 0.
- **Family list** — each row shows a small thumbnail, family name, and sub-category; selection
  highlights with a left accent bar. Version badges (after the check runs): `✓` green (in
  project, up to date), `↑ Update` orange pill (newer in library), no badge (not loaded).
- Favourites (★), a "favourites only" filter, and a multi-select Revit-version filter.

### Right panel (detail)
Shown when a family is selected.
- **Header** — square thumbnail frame, family name, category path, version-status banner, and
  actions: `Load into Project`, `↑ Update in Project` (only when loaded & outdated),
  `Edit Info` (§4), and `↻ Rescan` (re-extract just this family; updates the preview in place).
- **Tabs** — **Instructions** (read-only rich content), **Parameters (N)** (§5), **Gallery
  (N)** (§6).

---

## 2. Thumbnail display priority

For every family, the thumbnail is resolved in this order:
1. **Custom image in DB** (`CustomThumbnail`) — if present, always shown.
2. **System preview extracted from the `.rfa`** — as fallback (see the extraction note in
   *Deviations*: modern Revit stores it in the `RevitPreview4.0` stream).

The indexer updates the system `Thumbnail` on every scan and never touches `CustomThumbnail`.

---

## 3. Version check

Triggered from the Sync control; runs on a background thread and updates the list live.

1. Get families currently loaded in the open project (`Document.LoadedFamilies`, read on
   Revit's main thread via `ExternalEvent`).
2. Match by file name (without extension) against the index DB.
3. Compare the DB `ModifiedDate` against the file's `ModifiedDate` on disk.
4. Unmatched families get no badge; matched ones get `VersionStatus = UpToDate | UpdateAvailable`.

**Update (single / all):** `Document.LoadFamily(path, overwriteExistingFamily: true)` inside a
transaction via `ExternalEvent`; Update All iterates `UpdateAvailable` families sequentially.

---

## 4. Instructions Editor window

Separate **modal** WPF window opened by `Edit Info`.

- **Thumbnail section** — shows the current thumbnail (custom-over-system); border colour +
  status label signal state (system / custom in-sync / custom out-of-sync with the `.rfa`).
  `⁝` menu: `Replace…` (file picker, resized into DB), `Reset to system original` (deletes the
  custom row), `Update .rfa with DB image` (rewrites the OLE stream; enabled only when out of
  sync). Drag-drop and paste onto the thumbnail also replace it.
- **Rich-text body** — Bold/Italic/Underline/H1/H2/bulleted list/Add Image; inline images with
  a remove button; a drop zone accepts drag-drop / paste. Stored as a XAML `FlowDocument`
  string with images base64-embedded (no separate image table for instructions).
- **Gallery controls** (§6) — add/remove/caption/reorder.
- **Footer** — `Cancel` discards; `Save` persists instructions, thumbnail changes, and gallery.

---

## 5. Parameter audit (view-only)

Show each family parameter's **group**, **kind** (System / Shared / Family), instance/type
flag, data type, GUID, and formula; filter the rows live by typing. **View/audit only — no
write-back** to families (a family with disorganized params is easy to spot and fix by hand).

**Extraction:** instead of the fast `ExtractPartAtomFromFamilyFile` peek (name/datatype/
isInstance only), open the family document on Revit's main thread (the deep-scan handler
already runs per-family there) and read `doc.FamilyManager.Parameters`:
- `IsInstance`; `IsShared` → Kind = Shared; non-shared mapping to a `BuiltInParameter` → Kind =
  System, else Kind = Family; group label from `Definition.GetGroupTypeId()`; `GUID` (shared,
  nullable); `Formula` (nullable); data type from `Definition.GetDataType()`.
- **Trade-off (accepted):** deep scan is slower (opens every `.rfa`). Admin-only + occasional;
  runs in the background with a progress note.

Parameters tab adds **Group** and **Kind** columns and a filter textbox above the grid.

---

## 6. Image gallery per family

Multiple admin-added images per family, each with a caption; shown in the detail Gallery tab,
managed in the editor.

**Storage (hybrid):** image *files* on disk at `{LibraryFolder}\.Setup\Gallery\{familyId}\NN.png`
(inputs converted to PNG); *metadata* in the DB. The per-row list thumbnail stays a DB BLOB
(read for every row → avoid thousands of tiny network file reads). Missing/orphaned files
render a placeholder and are skipped silently.

Gallery images decode off the UI thread (lazy, only for the selected family).

---

## 7. Network share + concurrency (1 writer / many readers)

The DB may live on `\\server\share\…`, browsed by many users, written only by admin.
- Allow UNC paths.
- Do **not** use WAL on a network file (host-local shared memory isn't safe across machines);
  use a rollback journal.
- `PRAGMA busy_timeout` so brief admin writes don't error concurrent readers.
- Browse connections open **read-only**; scan/edit writes go through a short-lived read-write
  connection per commit.

---

## 8. Database schema

On top of the base index (`Families`, `Parameters`, `Thumbnail`):

```sql
-- Rich instructions as a XAML FlowDocument (images base64-embedded inline).
ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT;
-- Plus, added in later rounds: Tags TEXT, IsFavorite INTEGER, RevitYear INTEGER.

-- Parameter audit columns.
ALTER TABLE Parameters ADD COLUMN ParamGroup TEXT;
ALTER TABLE Parameters ADD COLUMN Kind       TEXT;   -- System | Shared | Family
ALTER TABLE Parameters ADD COLUMN Guid       TEXT;
ALTER TABLE Parameters ADD COLUMN Formula    TEXT;

-- User thumbnails kept separate so the indexer never overwrites them.
CREATE TABLE IF NOT EXISTS CustomThumbnail (
    Id        INTEGER PRIMARY KEY,
    FamilyId  INTEGER UNIQUE NOT NULL,
    PngData   BLOB    NOT NULL,
    OleSynced INTEGER NOT NULL DEFAULT 1,   -- 1 = matches .rfa preview; 0 = out of sync
    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
);

-- Gallery metadata (files live under .Setup\Gallery\{familyId}\).
CREATE TABLE IF NOT EXISTS FamilyImage (
    Id        INTEGER PRIMARY KEY,
    FamilyId  INTEGER NOT NULL,
    FileName  TEXT    NOT NULL,
    Caption   TEXT,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
);
```

Schema is migrated on open (the `pragma_table_info` add-column pattern in the repositories).

---

## 9. Revit commands / entry points

| Entry point | Type | Action |
|---|---|---|
| `Browse Library` ribbon button | `IExternalCommand` | Open/focus the Family Browser |
| `FamilyBrowserWindow` | WPF window | Main browser (non-modal) |
| `InstructionsEditorWindow` | WPF window | Instructions + gallery editor (modal) |
| `LoadFamilyEventHandler` | `IExternalEventHandler` | Load/reload a family on the main thread |
| `GetProjectFamiliesEventHandler` | `IExternalEventHandler` | Read `Document.LoadedFamilies` |
| `IndexingExternalEventHandler` | `IExternalEventHandler` | Per-family metadata extraction |

(Library folder, ignored subfolders, and the deep-scan actions now live in the separate
**Config** ribbon hub — see `2026-06-30-config-hub-deep-scan-design.md`.)

---

## 10. Out of scope (future)

- Parameter **write-back** / reorganizing params from the tool.
- Configuring parameter values before loading a family.
- Batch thumbnail replacement across families; auto-generated gallery renders.
- Auto-flagging rules for "disorganized" params (this iteration only shows + filters).

---

## Deviations since design (as built)

- **Thumbnail source:** the "system OLE thumbnail" is actually read from the modern
  **`RevitPreview4.0`** stream (embedded PNG) first, with the legacy `\x05SummaryInformation`
  DIB as fallback — current families leave the legacy property empty.
- **SQLite provider:** unified to **`Microsoft.Data.Sqlite`** for *all* configs (the old
  net48 `System.Data.SQLite` branch was dropped — it couldn't open DBs over some UNC shares).
- **Revit versions:** 2023 was dropped; only **2024 (net48)** and **2025 (net8)** are built.
- **Settings + deep scan:** moved out of the browser into the **Config** ribbon hub, with two
  modes ("Scan New & Changed" / "Re-scan All Families") and resumable-on-cancel behaviour.
- **Detail thumbnail** frame is a fixed square; **Rescan** refreshes the preview in place.
