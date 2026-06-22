# Family Browser — Design Spec
**Date:** 2026-06-15  
**Status:** Approved

---

## Overview

A non-modal floating WPF window that lets users search the indexed family library, load families into the open Revit project, check for outdated families in the project, view/author rich instructions per family, and inspect family parameters.

---

## 1. Main Browser Window

### Layout: Two-panel (List + Detail)

```
┌─────────────────────────────────────────────────────────────┐
│ ReviTchucky — Family Browser          [Furniture › Seating] │
├──────────────────────┬──────────────────────────────────────┤
│ 🔍 Search...  All ▾  │  [thumbnail]  Chair - Dining         │
├──────────────────────│  Furniture › Seating                 │
│ ⟳ Check Versions     │  ↑ Newer version available           │
│              ↑ Upd All│  [Load into Project] [↑ Update] [Edit Info] │
├──────────────────────┼──────────────────────────────────────┤
│ ▌ Chair - Dining  ↑  │  Instructions │ Parameters (6)       │
│   Chair - Office  ✓  ├──────────────────────────────────────┤
│   Sofa - 3-Seat   ↑  │  Place in a 3D view...              │
│   Stool - Bar        │  [plan img]  [3D img]               │
│   Armchair - Lounge  │                                      │
└──────────────────────┴──────────────────────────────────────┘
```

**Window behaviour:** Non-modal, resizable, stays open while the user works in Revit. User closes it manually.

### Left Panel

- **Search bar**: filters by family name (live, case-insensitive)
- **Category dropdown**: "All" or a specific Revit category
- **Toolbar row**:
  - `⟳ Check Project Versions` button — triggers version scan (see §3)
  - `↑ Update All (N)` button — appears only when N > 0 outdated families are found; updates all in one shot
- **Family list**: each row shows a small thumbnail, family name, and sub-category. Selection highlights with a left accent bar.
  - **Version badges** (shown after Check Versions is run):
    - `✓` green — family is in the project and up to date
    - `↑ Update` orange pill — newer version exists in library
    - No badge — family is not loaded in the current project

### Right Panel (Detail)

Shown when a family is selected.

- **Header**: thumbnail (120×80px), family name, category path, version status banner (orange warning if outdated), action buttons:
  - `Load into Project` — loads the `.rfa` into the open Revit model via `Document.LoadFamily`
  - `↑ Update in Project` — visible only when family is loaded and outdated; reloads with new version
  - `Edit Info` — opens the Instructions Editor window (§4)
- **Tabs**:
  - **Instructions** — read-only rich content (text + inline images) from DB
  - **Parameters (N)** — read-only list of family parameters (name, data type, instance/type flag)

---

## 2. Thumbnail Display Priority

For every family in the browser, the thumbnail is resolved in this order:

1. **Custom image in DB** — if present, always shown
2. **System OLE thumbnail** — extracted from the `.rfa` compound document (`BasicFileInfo`-adjacent stream), shown as fallback

---

## 3. Version Check

Triggered by `⟳ Check Project Versions`. Runs on a background thread; the list updates live as results come in.

**Algorithm:**
1. Call `Document.LoadedFamilies` to get all families currently loaded in the open Revit project.
2. For each loaded family, match by file name (without extension) against `Families.FileName` in the index DB.
3. Compare `ModifiedDate` from the DB against the file's `ModifiedDate` on disk (stored during indexing).
4. Families with no DB match, or no project match, are skipped (no badge).
5. Result: each matched family gets a `VersionStatus` enum: `UpToDate` or `UpdateAvailable`.

**Update (single family):** Calls `Document.LoadFamily(path, options)` with `overwriteExistingFamily = true` inside a Revit transaction, via `ExternalEvent`.

**Update All:** Iterates all `UpdateAvailable` families and runs the same update call sequentially.

---

## 4. Instructions Editor Window

Opens as a separate, modal WPF window when `Edit Info` is clicked. Modal prevents accidental edits while the browser is in use.

### Thumbnail Section (top of window)

- Displays the current thumbnail (DB or OLE fallback, same priority as §2)
- **Border color** signals state:
  - Subtle default — system OLE thumbnail (no custom DB image)
  - Green — custom DB image, in sync with `.rfa` OLE stream
  - Orange — custom DB image, `.rfa` OLE stream differs (e.g. family was resaved in Revit)
- **Status label** below the family name: `● Custom thumbnail`, `● Custom thumbnail · .rfa out of sync`, or `● System thumbnail`
- **⁝ menu** (three-dot, top-right corner of thumbnail):
  - `🖼 Replace…` — opens file picker (PNG/JPG/BMP); image is resized and stored in DB
  - `↺ Reset to system original` — deletes the DB custom image; falls back to OLE
  - `↑ Update .rfa with DB image` — rewrites the OLE thumbnail stream in the `.rfa` file; **grayed out** when DB and `.rfa` are in sync; **orange/active** when out of sync
- **Drag & drop onto thumbnail**: dragging an image file from Explorer or an inline image from the editor body onto the thumbnail replaces it (updates DB entry; marks `.rfa` as out of sync)
- **Paste (Ctrl+V) onto thumbnail**: pastes clipboard image as the new thumbnail

### Rich Text Editor Body

- Formatting toolbar: Bold, Italic, Underline, H1, H2, Bulleted list, `🖼 Add Image`
- Inline images: rendered in-flow with a `✕` remove button in the top-right corner
- Drop zone at the bottom of the body: accepts drag & drop or Paste for inline image insertion
- `Add Image` opens a file picker

### Footer

- `Cancel` — discards all changes and closes
- `Save` — persists to DB:
  - Saves rich text as XAML FlowDocument string (images base64-embedded inline; no separate image table needed)
  - If thumbnail was replaced: writes new image to `CustomThumbnail` **and** rewrites `.rfa` OLE thumbnail stream; sets `OleSynced = 1`
  - If thumbnail was reset: deletes `CustomThumbnail` row (`.rfa` OLE stream is not touched)

---

## 5. Database Schema Changes

New columns and tables needed on top of the existing schema:

```sql
-- Store rich instructions as XAML FlowDocument per family.
-- Images are base64-embedded in the XAML, so no separate image table is needed.
ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT;

-- Custom thumbnails live in their own table so the indexer (which populates
-- the existing Thumbnail table from OLE) never overwrites user-provided images.
CREATE TABLE IF NOT EXISTS CustomThumbnail (
    Id          INTEGER PRIMARY KEY,
    FamilyId    INTEGER UNIQUE NOT NULL,
    PngData     BLOB    NOT NULL,
    OleSynced   INTEGER NOT NULL DEFAULT 1,
    -- OleSynced: 1 = DB image matches .rfa OLE stream; 0 = out of sync
    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
);
```

**Thumbnail priority (display):**
1. `CustomThumbnail.PngData` if a row exists for this family
2. `Thumbnail.PngData` (OLE-extracted by the indexer) as fallback

The indexer continues to update `Thumbnail` on every scan and never touches `CustomThumbnail`.

---

## 6. New Revit Commands / UI Entry Points

| Entry Point | Type | Action |
|---|---|---|
| `Browse Library` ribbon button | `IExternalCommand` | Opens/focuses the Family Browser window |
| `FamilyBrowserWindow` | `WPF Window` | Main browser (non-modal) |
| `InstructionsEditorWindow` | `WPF Window` | Instructions editor (modal) |
| `LoadFamilyEventHandler` | `IExternalEventHandler` | Loads or reloads a family on Revit's main thread |
| `GetProjectFamiliesEventHandler` | `IExternalEventHandler` | Reads `Document.LoadedFamilies` on Revit's main thread |

---

## 7. Out of Scope (Future)

- Filtering/searching by parameter values
- Configuring parameter values before loading a family
- Batch thumbnail replacement across multiple families

---

## 8. Open Questions / Risks

- **OLE write safety**: OpenMCDF can write to `.rfa` compound documents, but writing to files on a shared network drive while Revit may have them cached requires care. The editor will warn the user if the file is read-only or locked.
- **Family matching for version check**: matching by file name (without extension) is simple but could produce false matches if two families share a name in different folders. A future improvement could use relative path matching instead.
- **Rich text storage**: instructions are stored as XAML `FlowDocument` in the DB. WPF's `RichTextBox` serializes/deserializes `FlowDocument` natively with `XamlWriter`/`XamlReader`; images are base64-encoded and embedded directly in the XAML so no separate image table is needed.
- **OLE sync detection**: when the indexer re-reads a family that has a `CustomThumbnail` row, it compares the freshly extracted OLE thumbnail bytes against those stored at last sync time. If they differ (Revit regenerated the thumbnail on resave), `OleSynced` is set to `0`.
