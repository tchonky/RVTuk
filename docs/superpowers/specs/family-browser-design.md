# Family Browser — Design (consolidated)

**Consolidated:** 2026-07-01 (updated 2026-07-07)
**Status:** Approved / largely shipped
**Area:** Family Management (VISION pillar 1)

> Single source of truth for the Family Browser. Consolidates two earlier specs — the base
> browser (`2026-06-15`) and the "Family Explorer" enhancement batch (`2026-06-22`, parameter
> audit + gallery + network concurrency) — plus the 2026-07-07 redesign batch
> (`2026-07-07-family-browser-redesign-design.md`): checkbox category filter, OR-union source
> toggle buttons, merged Load/Update button, relocated action icons + new Open-in-Family-Editor,
> gallery removal, and settings + a new Help/About panel moved into the browser window itself.
> Those originals now live under [`docs/archive/`](../../archive/). "Family Explorer" was a
> codename for round 2 of this same product; the product name is **Family Browser**.
>
> See **Deviations since design (as built)** at the end for where the shipped code differs.

---

## Overview

A non-modal floating WPF window that lets users search the indexed family library, load
families into the open Revit project, check for outdated families in the project, view/author
rich instructions per family, and audit family parameters. The shared database can live on a
network path read by many users at once and written only by an admin.

---

## 1. Main Browser Window

### Layout: two-panel (list + detail)

```
┌─────────────────────────────────────────────────────────────┐
│ RVTuk — Family Browser          [Furniture › Seating]       │
├──────────────────────┬──────────────────────────────────────┤
│ 🔍 Search... ☑ Cat ▾ │ [thumb] Chair - Dining      [Load] ↻ │
│ ⬅️  ⭐  ➡️      ⟳ Sync│         Furniture › Seating       📝 ⬈│
├──────────────────────┼──────────────────────────────────────┤
│ ▌ Chair - Dining  ↑  │ Instructions │ Parameters (6)         │
│   Chair - Office  ✓  ├──────────────────────────────────────┤
│   Sofa - 3-Seat   ↑  │ Place in a 3D view...                │
│   Stool - Bar        │                                       │
├──────────────────────┤                                       │
│ ℹ️            ⚙️      │                                       │
└──────────────────────┴──────────────────────────────────────┘
```

**Window behaviour:** non-modal, resizable, stays open while the user works in Revit; user
closes it manually.

### Left panel
- **Search bar** — filters by family name (live, case-insensitive; multi-word: all tokens
  match in any order). Also matches tags.
- **Category filter** — a checkbox popover (`CategoryFilterOption.cs`), not the old single-select
  `ComboBox`. Header row is an "All categories" master checkbox with tri-state
  checked/unchecked/indeterminate behaviour; below it, one checkbox per category, all **on by
  default**. This is a genuine multi-select: unchecking a category hides its families from the
  list immediately, and any combination of categories can be shown at once.
- **Toggle buttons (⬅️ / ⭐ / ➡️)** — three always-visible toolbar toggles, all **off by default**
  (unfiltered — nothing hidden):
  - ⬅️ — families currently loaded in the project.
  - ⭐ — favourites.
  - ➡️ — library-only families (not loaded in the project).
  Pressing one or more **narrows the list to the union (OR) of what's pressed**. With none
  pressed the list is unfiltered. Pressing only ⭐ shows every favourite regardless of
  project/library status; pressing ⬅️ + ⭐ shows families that are either in the project or a
  favourite (or both). This is deliberately OR, not AND: ⬅️ and ➡️ are a complementary,
  exhaustive pair (every family is exactly one or the other), so an exclusion/AND model can't
  express "favourites only" — turning both off to try to isolate ⭐ would exclude everything,
  since there'd be no OR path back in. See *Deviations* for the shipped-then-corrected history.
- **Sync button (⟳)** — a plain button, not a popover menu. Clicking it directly re-runs the
  project/version check (§3); there is no menu of checkboxes hanging off it anymore.
- **Family list** — each row shows a small thumbnail, family name, and sub-category; selection
  highlights with a left accent bar. Version badges (after the check runs): `✓` green (in
  project, up to date), `↑ Update` orange pill (newer in library), no badge (not loaded).
- **Footer row** (new, under the family list) — two icon buttons:
  - ℹ️ — toggles the right panel to the Help/About page (§4a) instead of the family detail.
  - ⚙️ — toggles the right panel to the embedded Settings page (§4b) instead of the family
    detail.

There is no separate Revit-year ("Versions") filter anymore — that capability was removed
entirely (`VersionFilterOption.cs` deleted, no replacement).

### Right panel (detail)
Shown when a family is selected (unless the ℹ️ or ⚙️ footer toggle is active — see below).
- **Header** — square thumbnail frame; to its right, the family name/category-path block with a
  single **Load / Update** button placed directly to the right of that block, vertically centered
  against the thumbnail's height (§1a). In the top-right corner of the detail panel, a stacked
  icon-only action column (§1b): Rescan, Edit Info, Open.
- **Tabs** — **Instructions** (read-only rich content) and **Parameters (N)** (§5). There is no
  Gallery tab (removed — see §6 note below).

### 1a. Load / Update button (merged)

The old two-button pair — "Load into Project" and a conditionally-shown "↑ Update in Project" —
is now a **single button**. It reads:
- **"Load"** (normal styling) when the family isn't loaded, or is loaded and up to date.
- **"Update"** with an **orange background** (`Brush.UpdateOrange`, `#D9822B`, new in
  `DarkTheme.xaml`) when a newer version exists in the library than what's loaded in the project.

The old orange/red "↑ Newer version available in library" banner is gone entirely — the orange
Update button now carries that signal on its own, so there's no redundant banner + button pair.

### 1b. Action icon column (relocated + Open added)

Previously **Edit Info** and **Rescan** were text buttons in a row below the thumbnail. They now
live in a stacked, icon-only column in the top-right corner of the family detail panel:

1. **Rescan** — circular-arrow icon only (no text); re-extracts just this family and updates the
   preview in place. Same behaviour as before, new position.
2. **Edit Info** — notepad+pen icon; opens the Instructions Editor window (§4).
3. **Open** (new) — box-with-arrow-breaking-out icon; sends the family's `.rfa` straight to
   Revit's Family Editor. Implemented via a new `IExternalEventHandler`,
   `src\RVTuk.Revit\ExternalEvents\OpenFamilyEditorEventHandler.cs`, mirroring the existing
   `LoadFamilyEventHandler` pattern (`Prepare`/`Execute`/`WaitForCompletion`), calling
   `app.Application.OpenDocumentFile(path)`. Wired through new `Application.OpenFamilyEditorHandler`
   / `Application.OpenFamilyEditorEvent` statics and a new `Action<string> openInFamilyEditor`
   delegate in `BrowseLibraryCommand.cs`, exposed to the view as
   `FamilyBrowserViewModel.OpenFamilyEditorCommand`.

---

## 2. Thumbnail display priority

For every family, the thumbnail is resolved in this order:
1. **Custom image in DB** (`CustomThumbnail`) — if present, always shown.
2. **System preview extracted from the `.rfa`** — as fallback (see the extraction note in
   *Deviations*: modern Revit stores it in the `RevitPreview4.0` stream).

The indexer updates the system `Thumbnail` on every scan and never touches `CustomThumbnail`.

---

## 3. Version check

Triggered by the plain Sync button (§1, no longer a popover); runs on a background thread and
updates the list live. The underlying check is unchanged:

1. Get families currently loaded in the open project (`Document.LoadedFamilies`, read on
   Revit's main thread via `ExternalEvent`).
2. Match by file name (without extension) against the index DB.
3. Compare the DB `ModifiedDate` against the file's `ModifiedDate` on disk.
4. Unmatched families get no badge; matched ones get `VersionStatus = UpToDate | UpdateAvailable`.

**Update (single):** `Document.LoadFamily(path, overwriteExistingFamily: true)` inside a
transaction via `ExternalEvent`, triggered by the merged Load/Update button (§1a) when it's
showing "Update".

By default, post-sync, **nothing is hidden**: all three toggle buttons (§1) start off (unfiltered),
so no family is excluded from the list just because Sync ran. Previously the app defaulted to
hiding library-only families once Sync had run (`ShowInProjectOnly` defaulted `true`, an
inclusion filter); that default no longer applies — see *Deviations*.

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
  string with images base64-embedded (no separate image table for instructions). This inline
  image capability is also what replaced the removed per-family gallery — see §6 note.
- **Footer** — `Cancel` discards; `Save` persists instructions and thumbnail changes.

### 4a. Help/About panel (new)

Toggled by the ℹ️ footer button (§1) in place of the family detail. Renders markdown fetched
over HTTP from a raw GitHub URL, currently a **placeholder constant** in
`FamilyBrowserViewModel.cs`:

```
HelpMarkdownUrl = "https://raw.githubusercontent.com/knafo-klimor/rvtuk-docs/main/help.md"
```

> ⚠️ **This placeholder URL needs to be pointed at the real docs repo before this ships.**

Rendering goes through a new hand-rolled markdown→`FlowDocument` converter,
`RVTuk.UI.Helpers.MarkdownConverter` (deliberately no new NuGet dependency), consumed via a new
`RichTextBoxHelper.MarkdownSource` attached property (mirrors the existing `DocumentXaml`
attached property used for Instructions). Supports headings, paragraphs, bold/italic, inline
code, `[text](url)` links, bullet lists, and `---` horizontal rules.

### 4b. Settings panel (moved in from the ribbon)

Toggled by the ⚙️ footer button (§1) in place of the family detail. Content is unchanged from
the earlier Config-hub design (library root folder, ignored subfolders, deep scan with "Update
thumbnails"/"Update parameters" checkboxes + one Scan button) — only its *location* changed. See
*Deviations* for the history of where this panel has lived.

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

## 6. Network share + concurrency (1 writer / many readers)

The DB may live on `\\server\share\…`, browsed by many users, written only by admin.
- Allow UNC paths.
- Do **not** use WAL on a network file (host-local shared memory isn't safe across machines);
  use a rollback journal.
- `PRAGMA busy_timeout` so brief admin writes don't error concurrent readers.
- Browse connections open **read-only**; scan/edit writes go through a short-lived read-write
  connection per commit.

> **Note:** the per-family image gallery that previously lived in this section of the doc was
> removed on 2026-07-07 — pictures now live only as images embedded inline in the rich-text
> Instructions body (§4), a capability that already existed and made the separate gallery
> redundant. See the *Deviations* section for what was deleted.

---

## 7. Database schema

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
```

Schema is migrated on open (the `pragma_table_info` add-column pattern in the repositories).

> **`FamilyImage` table removed from this listing (2026-07-07).** The gallery feature was
> removed (§6 note), and both `BrowserRepository.cs` and `IndexRepository.cs` had their
> `CREATE TABLE IF NOT EXISTS FamilyImage` statement deleted, so brand-new databases never get
> that table. This was deliberately non-destructive: no migration drops the table or deletes
> existing rows/files on any already-deployed shared database — the code just stops
> creating/reading/writing gallery data going forward. An existing production database may still
> carry a populated `FamilyImage` table with historical data; it's simply untouched dead weight
> now. Repository methods `GetImages`/`AddImage`/`UpdateCaption`/`DeleteImage`/`ReorderImages`/
> `GetGalleryPath` were deleted along with it. Orphan-folder cleanup (deleting a *deleted
> family's* leftover gallery folder in `DeleteStaleEntries`) was kept as-is — that's unrelated
> pre-existing cleanup, not a gallery-editing feature.

---

## 8. Revit commands / entry points

| Entry point | Type | Action |
|---|---|---|
| `Browse Library` ribbon button | `IExternalCommand` | Open/focus the Family Browser |
| `FamilyBrowserWindow` | WPF window | Main browser (non-modal) |
| `InstructionsEditorWindow` | WPF window | Instructions editor (modal) |
| `LoadFamilyEventHandler` | `IExternalEventHandler` | Load/reload a family on the main thread |
| `OpenFamilyEditorEventHandler` | `IExternalEventHandler` | Open a family's `.rfa` in Revit's Family Editor (§1b) |
| `GetProjectFamiliesEventHandler` | `IExternalEventHandler` | Read `Document.LoadedFamilies` |
| `IndexingExternalEventHandler` | `IExternalEventHandler` | Per-family metadata extraction |

There is no `OpenConfigCommand` / ribbon Config entry point anymore — it was deleted along with
`ConfigWindow.xaml`/`.xaml.cs` when settings moved back into the Family Browser (§4b). Library
folder, ignored subfolders, and the deep-scan actions now render inline in the Family Browser's
right panel, toggled by the ⚙️ footer button (§1), reusing the same `ConfigViewModel` class
composed as `FamilyBrowserViewModel.Settings`.

---

## 9. Out of scope (future)

- Parameter **write-back** / reorganizing params from the tool.
- Configuring parameter values before loading a family.
- Auto-flagging rules for "disorganized" params (this iteration only shows + filters).
- Pointing the Help/About panel at the real docs repo URL (currently a placeholder — see §4a).

---

## Deviations since design (as built)

- **Thumbnail source:** the "system OLE thumbnail" is actually read from the modern
  **`RevitPreview4.0`** stream (embedded PNG) first, with the legacy `\x05SummaryInformation`
  DIB as fallback — current families leave the legacy property empty.
- **SQLite provider:** unified to **`Microsoft.Data.Sqlite`** for *all* configs (the old
  net48 `System.Data.SQLite` branch was dropped — it couldn't open DBs over some UNC shares).
- **Revit versions:** 2023 was dropped; only **2024 (net48)** and **2025 (net8)** are built.
- **Detail thumbnail** frame is a fixed square; **Rescan** refreshes the preview in place.
- **Settings location, round-tripped (2026-07-07):** settings started inline in the Family
  Browser (this doc's original design), were moved out to a dedicated ribbon **Config** window
  on 2026-06-30 (`docs/archive/2026-06-30-config-hub-deep-scan-design.md`) so the hub could grow
  tabs for other tools, then moved **back** into the Family Browser (§4b) on 2026-07-07 — a
  deliberate reversal, not an oversight. The Config-hub premise (growing into a shared settings
  surface for Comparator and future tools) never materialized: the hub only ever grew the one
  "Family Library" tab it started with. `ConfigWindow.xaml`/`.xaml.cs`, `OpenConfigCommand.cs`,
  and the ribbon Config button are all deleted; the underlying `ConfigViewModel` class was kept
  and reused rather than duplicated.
- **Gallery removed (2026-07-07):** the per-family image gallery (§6 note, §7 note) was removed
  in favour of images embedded inline in the rich-text Instructions body, which already
  supported drag-drop/paste and made the separate gallery redundant. `FamilyImage.cs`,
  `GalleryItemViewModel.cs`, and the `FamilyImage` table creation/repository methods are gone;
  no data migration touches existing production databases.
- **New Help/About panel (2026-07-07):** a markdown-rendered help page (§4a), fetched from a
  URL that is still a placeholder and must be pointed at the real docs repo before shipping.
- **Filter semantics replaced, then corrected (2026-07-07):** the old Sync-popover source/status
  filters (in-project / outdated checkboxes), the separate favourites-only toggle, and the
  Revit-year Versions multi-select were all replaced by the three toggle buttons in §1. The
  first shipped version used *exclusion* semantics (all on by default; switching one off hid
  what it represented, AND'd together) — this broke isolating favourites: since ⬅️ and ➡️ are a
  complementary, exhaustive pair, turning both off to try to show "favourites only" excluded
  every family, leaving nothing. Corrected same day to *inclusion/OR* semantics: all three start
  **off** (unfiltered); pressing one or more shows the union of what's pressed. Pressing only ⭐
  now correctly shows every favourite regardless of project/library status. Either way, nothing
  is hidden by default — the old app's post-sync default of hiding library-only families no
  longer applies.
