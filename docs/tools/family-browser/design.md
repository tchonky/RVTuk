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
  list immediately, and any combination of categories can be shown at once. The options come
  from the rows in the list (not a DB query), so a model-only family's category always has
  one, and families without a category get a "(No category)" option at the end.
- **Toggle buttons (⬅️ / ⭐ / ➡️)** — three always-visible toolbar toggles, all **off by default**
  (unfiltered — nothing hidden):
  - ⬅️ — families loaded in the model (model-only rows **and** families that are also in the
    library).
  - ⭐ — favourites.
  - ➡️ — families present in the library (library-only **and** families that are also loaded
    in the model).
  Each pressed toggle **narrows the list (AND)**; with none pressed the list is unfiltered.
  ⬅️ and ➡️ are *inclusive* membership filters that overlap on the in-both statuses, so
  pressing both shows exactly the families that exist in both the model and the library —
  the four source views are: nothing pressed (everything), ⬅️ (all in the model), ➡️ (all in
  the library), ⬅️+➡️ (in both). Pressing only ⭐ still shows every favourite regardless of
  model/library status; ⭐ combined with an arrow intersects (e.g. ⭐+⬅️ = favourites loaded
  in the model). Semantics live in `RVTuk.Core.FamilyBrowser.Util.SourceToggleFilter` with a
  truth-table test. See *Deviations* for the exclusion→OR→AND history.
- **Sync button (⟳)** — a plain button, not a popover menu. Clicking it directly re-runs the
  project/version check (§3); there is no menu of checkboxes hanging off it anymore.
- **Family list** — each row shows a small thumbnail, family name, and sub-category; selection
  highlights with a left accent bar. Long names wrap to at most **two lines**, then trim with
  an ellipsis (the full name is in the row tooltip). Version badges (after the check runs):
  `✓` green (in project, up to date), `↑ Update` orange pill (newer in library), `⚠️` (loaded
  in the project but not found in the library — a "model only" row), no badge (not loaded).
  When a family carries the `_Version` parameter, its value renders as muted text just before
  the badge (list rows and grid cards alike) — in **red** when `_Version` is an *instance*
  parameter in the family (library `.rfa` per the deep scan, or the loaded copy per the sync's
  instance fallback, §3): off the office standard, which wants a type parameter, so the red
  marks families whose parameter needs changing. The tooltip says so.
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

For **model-only rows** (in the project, not in the library) the Load/Update button is replaced
by **"Save to Library"**: `Document.EditFamily` + `SaveAs` (via `EditProjectFamilyEventHandler`)
writes the family's `.rfa` into the folder where most same-category families already live —
else a folder named after the category (created on demand), else the library root
(`SaveToLibraryPlanner` in Core picks the path). The DB isn't touched; the next Scan indexes
the new file (refresh stays read-only, §6). Family names containing characters Windows forbids
in file names are rejected with a message (`FamilyFileName.IsSafeFileName`).

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

For **model-only rows**, Rescan and Edit Info are hidden (no `.rfa` / DB row to act on), while
**Open** stays available: it edits the family from the model — `Document.EditFamily`, `SaveAs`
to a temp `.rfa` named exactly after the family (so "Load into Project" round-trips onto the
same family), then `OpenAndActivateDocument` (an in-memory `EditFamily` document has no UI
window and can't be activated directly). Same `EditProjectFamilyEventHandler` as Save to
Library (§1a), just without a library target path.

Model-only rows get their **thumbnail** from the loaded family symbol
(`ElementType.GetPreviewImage`, 96 px) rather than an OLE stream — Sync fetches previews
lazily for just the model-only names via `GetFamilyPreviewsEventHandler`, best-effort.

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
updates the list live:

1. Get families currently loaded in the open project, each with its `_Version` value, on
   Revit's main thread via `ExternalEvent` (`GetProjectFamiliesEventHandler`). The value is
   read off the family's symbols (type parameter — the office standard); when that finds
   nothing, a fallback reads it off the first placed `FamilyInstance`, which is where an
   *instance* `_Version` lives (one collector pass covers all unresolved families; a
   formula-locked value makes any instance authoritative). A family whose `_Version` is
   instance-level **and** has no placed instances stays version-less — the value exists
   nowhere in the project short of `EditFamily`, which is far too slow per-family. The
   fallback also reports `VersionIsInstance`, feeding the red version display (§1).
2. Match by file name (without extension) against the index DB.
3. Compare the project value against the index's `_Version` captured by the deep scan
   (`FamilyVersionCheck`: numeric compare when both parse, only library-ahead counts;
   missing value on either side → no verdict).
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

Separate **non-modal** WPF window opened by `Edit Info`, owned by the browser, one at a time.
Opening another family's editor closes the current one first.

- **Thumbnail section** — shows the current thumbnail (custom-over-system); border colour +
  status label signal state (system / custom in-sync / custom out-of-sync with the `.rfa`).
  `⁝` menu: `Replace…` (file picker, resized into DB), `Reset to system original` (deletes the
  custom row), `Write this thumbnail into the .rfa file` (rewrites the OLE stream and stores the
  thumbnail; enabled only when out of sync). Drag-drop and paste onto the thumbnail also replace
  it. Save writes the .rfa only while it is out of sync, so a text-only edit never touches the
  shared library file.
- **Rich-text body** — Bold/Italic/Underline/H1/H2/bulleted list/Add Image; inline images with
  a remove button; a drop zone accepts drag-drop / paste. Stored as a XAML `FlowDocument`
  string with images base64-embedded (no separate image table for instructions). This inline
  image capability is also what replaced the removed per-family gallery — see §6 note.
- **Footer** — `Save` persists instructions and thumbnail changes. Closing without saving
  (`Cancel`, Esc, the window's ✕, or the browser opening another editor or closing) asks
  before discarding unsaved changes, and only when there are some.

### 4a. Help/About panel (new)

Toggled by the ℹ️ footer button (§1) in place of the family detail. Renders markdown fetched
over HTTP from this repo's [`help.md`](help.md) on `master` (`HelpMarkdownUrl` in
`FamilyBrowserViewModel.cs`), so editing that file and merging it updates every user's help
page with no deploy. The fetch times out after 10 s and is retried the next time the panel
opens if it failed. Links open in the web browser (http, https and mailto only).

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
- Writers fail fast with a clear error: SQLite silently degrades a read-write open to
  read-only when the file denies writes (e.g. a cloud-sync client like Desktop Connector
  flags the DB ReadOnly), so `IndexRepository`'s ctor probes `sqlite3_db_readonly` and throws
  a plain-language `IOException` instead of letting a later write die mid-scan.
- Browse connections open **read-only**; scan/edit writes go through a short-lived read-write
  connection per commit. Connection **pooling is disabled** on every connection —
  `Microsoft.Data.Sqlite` pools by default, and a pooled handle outlives `Dispose`, keeping
  the shared file open (and blockable) for the whole Revit session.
- The browser write-opens the DB only to **create it on first run** or to **migrate an
  outdated schema**; an everyday open of a current DB is read-only from the first byte.
- The **Sync/refresh button never writes**: it re-reads the index and re-runs the
  project/version compare. Reconciling the DB with the `.rfa` files on disk (add new, prune
  deleted, flag changed) is solely the Scan's job — its filenames-only mode (both checkboxes
  off) does exactly that with no extraction.

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
> `GetGalleryPath` were deleted along with it. The orphan-folder cleanup in `DeleteStaleEntries`
> (deleting a *deleted family's* leftover gallery folder) outlived it until 2026-10-04, when it
> went too and the stale-row prune became a single transaction.

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
- **New Help/About panel (2026-07-07):** a markdown-rendered help page (§4a). Its URL was a
  placeholder until 2026-10-04, when it was pointed at `help.md` in this repo.
- **Read-only refresh (2026-07-16):** Sync previously also fast-synced the DB with the `.rfa`
  files on disk (one write transaction per file + stale-row pruning) — a write burst that
  locked the shared DB for every other user (backlog "Read Only DB"). Refresh now only
  re-reads the index and re-runs the project/version compare (§3); disk reconciliation moved
  entirely to the Scan. In the same fix, the browser stopped write-opening the DB on every
  open (only first-run create / schema migration), and connection pooling was disabled on all
  connections (§6). Trade-off: library files added/deleted on disk appear/disappear after the
  next Scan, not on refresh.
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
- **Filter semantics corrected again — OR → inclusive AND (2026-07-19):** once model-only rows
  (`VersionStatus.ModelOnly`) joined the list, ⬅️ and ➡️ stopped being a complementary pair, and
  the OR/union model had two holes: ➡️ ("library-only", `== None`) hid the families that exist
  in both places, and pressing ⬅️+➡️ unioned to "everything" — the in-both view was
  inexpressible. Redefined both arrows as *inclusive* membership filters ("in the model" /
  "in the library") combined by AND, giving all four source views (§1) including in-both.
  ⭐ became an AND constraint too (⭐+⬅️ = favourites in the model, an intersection, no longer a
  union). The predicate moved to Core (`SourceToggleFilter`) under a truth-table test;
  `ShowLibraryOnlyFamilies` was renamed `ShowLibraryFamilies`.
- **Review fixes (2026-10-04):** a full code review's verified findings were fixed in one pass.
  The behaviour changes a user sees:
  - The browser is no longer `Topmost`: it is owned by Revit's main window, so Revit's own
    dialogs show above it. The editor and the scan progress window are owned by the browser.
  - Load, Open, Save to Library and Sync refuse to act on a family document (after "Open in
    Family Editor" the browser stays clickable) and say to switch to the project window.
    Open in Family Editor now reports its failures instead of silently doing nothing.
  - The selection survives filtering, starring and Sync. A failed Sync keeps the last good
    list. A Scan re-reads the list when it finishes. Rescan updates the row's category,
    version and verdict in place, against the project copy the last Sync saw.
  - Category options come from the loaded rows, with a "(No category)" option, so unscanned
    and model-only families can always be shown.
  - "Working…" in the rail footer shows while a Revit round-trip or a DB re-read runs, and
    the buttons it would race are disabled. An empty list explains itself.
  - A family Revit cannot open during a scan (too new, locked, damaged) keeps its old
    metadata and stays unextracted, so a later scan retries it, instead of being stamped
    extracted with empty data.
