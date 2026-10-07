# Family Browser — Backlog

Bugs, improvements, and ideas for the Family Browser tool. Toolkit-wide items live in
[`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] **Old-format instructions crash the editor.** Found 2026-10-04: instructions stored with
  a bare `Image` in an `InlineUIContainer` (the dev-era format, before images were wrapped in a
  `Border`) make `WireExistingImages` throw from the editor's `Loaded` handler. Current saves
  always wrap images, so only instructions written during development are affected. The office
  DB had none on 2026-10-04 (3 families with instructions, 0 bare images), so this is low
  priority; make `WireExistingImages` tolerate them if one ever turns up.

## ✨ Improvements

- [ ] **Decide: keep or delete "write this thumbnail into the .rfa".** Found in the
  2026-10-04 review: `ThumbnailWriter` writes the custom thumbnail into the legacy
  `\x05SummaryInformation` stream, but the tool's own reader prefers `RevitPreview4.0`, stock
  2024 families have no SummaryInformation stream, and the next scan re-extracts Revit's
  preview anyway — so "custom thumbnail synced to the .rfa" claims more than it does. The
  browser already shows the DB's custom thumbnail. Either delete `ThumbnailWriter` and the
  `UpdateOle`/`OleSynced` plumbing (thumbnails become DB-only), or write the stream Revit and
  Explorer actually read. (The same review stopped every Save from rewriting the .rfa.)
- [ ] **"Database is locked" UX.** While another user's scan holds the DB, opening the browser
  (or saving an edit) past the 5 s `busy_timeout` surfaces a raw SqliteException dialog. Show a
  friendly "library is being scanned — try again shortly" message instead, and audit the edit
  paths (tags, instructions save) for catch/retry — favourites already degrade gracefully.
  *(Partially done 2026-07-16: scan/rescan now fail fast with a clear "library database is
  read-only" message — see the read-only-DB entry in Done. Browser-open and edit paths remain.)*
- [ ] **Library on Autodesk Desktop Connector (ACC Docs) is fragile.** Discovered 2026-07-16:
  the office library lives under `C:\Users\<user>\DC\ACCDocs\...`, so every user gets a local
  replica and DC syncs whole files. DC flagged `RVTuk.db` with the Windows **ReadOnly attribute**
  (it does this while the cloud copy is held/locked elsewhere), which silently blocks every scan
  and rescan — and because the DB is also one schema migration behind (`Families.Version`
  missing), nobody can migrate it, so the browser runs without version data (no ✓/Update
  badges, no version display). Two-user scans on DC would also conflict wholesale (last writer
  wins) — the shared-DB design assumes a real SMB share (`\\server\share`). **To unblock now:**
  clear the read-only attribute on `...\.DB\RVTuk.db` (right-click → Properties, or
  `attrib -r`) while nobody else has the browser open, then run a Scan once (migrates the
  schema and captures `_Version`). **Decide longer-term:** move the DB (or the whole library)
  off Desktop Connector.
- [ ] **Old-schema DB on a no-write share.** The degraded-read fallback only covers the
  `Version`/`ParametersExtracted` columns; a DB predating `Tags`/`IsFavorite`/`RevitYear` that
  can't be migrated (user lacks write permission) still crashes `GetAllFamilies`. Rare (one
  admin scan anywhere fixes the schema), but the browser could degrade those columns too.
- [ ] **Scan staleness compare mismatch.** `FamilyIndexer` treats a file as unchanged within a
  1-second `ModifiedDate` tolerance, but the SQL CASE in `UpsertFamilyFileInfo` and
  `UpdateThumbnailOnly` uses exact string equality — a sub-second timestamp drift
  (SMB/filesystem granularity) can needlessly clear `Version`/`ParametersExtracted` for an
  unchanged family. Self-heals on the next parameter scan; align the two comparisons.
  *(Narrowed 2026-10-04: an unchanged family no longer gets a write at all, so only the
  thumbnail-only path — an unchanged file missing its thumbnail — can still hit it.)*
- [ ] **Deep-scan re-entrancy.** The embedded Settings panel exposes one **Scan** button; a
  user can still click it twice in a row while a scan is in progress. Both runs would share
  `IndexingHandler` / `IndexingEvent` and race. Disable the Scan button (or guard
  `RunDeepScan`) while a scan is in progress.

## 🚀 Ideas

- [ ] **Tags follow-ups** (base `c742a9b`, clickable chips `c625502`): a tag auto-complete /
  pick-from-existing list so spelling stays consistent.
- [ ] **Recently used** — track the last N families loaded into a project for quick access.
- [x] Toolbar polish: the ⬅️/⭐/➡️ toggle buttons and Sync button use default (light) WPF
  chrome; style them to match the dark theme. Also style the category checkbox popover.

## ⏳ Deferred (decided "later" during the Family Explorer build)

- [ ] Parameter **write-back** — let the tool actually fix/reorganize parameters in the
  families (currently view/audit only).
- [x] UI styling/layout polish for the parameter regions.

## ✅ Done

- [x] **"Open family button stop working"** (2026-10-04). Two causes. (1) Open in Family
  Editor was the only action whose Revit-side error was thrown away: a file moved or renamed
  since the last Scan, a family already open, a locked or newer-version file all looked like a
  dead button. It now reports the reason. (2) Every action ignored `ExternalEvent.Raise()`'s
  result and waited forever; one refused raise wedged the load lock, so Load, Open and Save to
  Library all went dead for the session. A refused raise now fails with a message. Also: after
  Open in Family Editor the family stays the active document, and Load used to nest the
  library family into it — it now asks you to switch to the project window.
- [x] **"logic" bug — the detail pane kept vanishing** (2026-10-04). Every list rebuild
  (each search keystroke, toggle, category tick, tag click, and starring a family) cleared the
  selection, so the detail pane disappeared. The selection is now kept unless the family is
  filtered out. Same review round (55 verified findings, all fixed): Settings edits no longer
  revert other tools' saved settings, ignore-list edits refresh the list, a failed Sync keeps
  the list, Update All's count stays right, the Load button reads "Update", the category popup
  scrolls, the Raw view no longer rewrites `Door_Single_90.rfa`, the editor asks before
  discarding unsaved changes, and the scan no longer deletes rows of over-long paths or aborts
  when a file vanishes mid-scan. See design.md *Deviations* (2026-10-04).
- [x] **Rescan said "Could not rescan this family" with no reason** (2026-07-16). Root cause:
  `IndexRepository` opens the DB read-write, but SQLite silently degrades to a read-only open
  when the file denies writes (Desktop Connector had flagged `RVTuk.db` ReadOnly), and the
  ctor's auto-migration then threw a raw `SqliteException` that the rescan swallowed into the
  generic message. Fixed: the ctor now probes `sqlite3_db_readonly` and throws a clear
  `IOException` naming the DB and the likely cloud-sync cause; the rescan delegate returns
  `(Success, Error)` and the dialog shows the real reason; the deep scan already surfaced
  `ex.Message` and inherits the clear text. (The DB itself still needs its read-only attribute
  cleared — see the Desktop Connector item under Improvements.)
- [x] **Model-only rows now show thumbnails** (2026-07-16). They have no `.rfa` to extract an
  OLE preview from, so Sync renders the loaded type's preview instead
  (`ElementType.GetPreviewImage`, 96px, via the new `GetFamilyPreviewsEventHandler`) — fetched
  lazily for just the model-only names, best-effort.
- [x] **Model-only row polish** (4 backlog items, 2026-07-16): flag is now ⚠️ (was a bordered
  chip); long names trim to 2 lines with ellipsis + full-name tooltip; the `_Version` value
  shows next to the row flags (list + card grid); model-only rows got real actions —
  **Open in Family Editor** edits the family from the model (`EditFamily` → temp .rfa →
  activate), and a **Save to Library** button (replacing Load) writes the .rfa into the
  folder where most same-category families live (else `<category>\`, created on demand;
  else the root — `SaveToLibraryPlanner`). The saved file is indexed by the next Scan
  (refresh stays read-only by design). Rescan/Edit Info buttons are hidden for model-only
  rows instead of showing permanently disabled.
- [x] Family Explorer: network-share concurrency, parameter audit (Group/Kind + filter),
  image gallery.
- [x] Fix: WPF `Application.Current` null crash on Browse Library (`90b96d6`).
- [x] Fix: `LibraryFolderPath` read-only TwoWay binding crash (`7771823`).
- [x] Fix: scan aborting on Windows MAX_PATH; now skips over-long paths (`4b9998f`).
- [x] Window **always on top**; long family names **wrap** to 2–3 lines (`3813ecf`).
- [x] **Multi-word search** — all words match, any order/position (`0336bad`).
- [x] **Per-family Rescan** button — re-extract just the selected family (`1b3c52d`).
- [x] Fix: editor crash on open — `ContextMenu` parented in a Grid (`958f614`).
- [x] Gallery: UNC-safe image `Uri` + confirm before deleting an image (`fd60009`).
- [x] **Ignore subfolders** in deep scan + sync (configurable in Settings) (`534a6f5`).
- [x] Fix: ignored-folder list **now updates** the browser view when changed (`bd5ec13`;
  lost again in `e3bcb6e` when the setting moved to `ConfigViewModel`, restored 2026-10-04).
- [x] Filter by **Revit version** — RevitYear column + version dropdown (`e08ce65`)
  *(filter later removed in the 2026-07-07 redesign)*.
- [x] Gallery: **reorder images** with ◀/▶ buttons in the editor (`bb87e65`).
- [x] **Skipped-family count** — deep-scan dialog + `last-scan.log` report skips (`46dada3`).
- [x] **Per-family tags** — editable, searchable, shown in detail (`c742a9b`); clickable
  tag chips that filter the list (`c625502`).
- [x] Editor gallery images **decode off the UI thread** (no stutter on slow shares) (`191c214`).
- [x] Deep-scan progress shows **estimated time remaining** (`87fa3e0`).
- [x] **Two-line toolbar** (`f04eef1`).
- [x] **Favourites** — star a family, "favourites only" filter (`f04eef1`).
- [x] **Multi-select version** filter (`f04eef1`) *(removed in the 2026-07-07 redesign)*.
- [x] **"In the project" / "Outdated" filters** in the Sync dropdown (`f04eef1`).
- [x] On deep-scan **Cancel**, keep everything already extracted (`57c9ceb`).
- [x] Scan facets: one **Scan** button + **Update thumbnails** / **Update parameters**
  checkboxes; Sync-only families get picked up by the next facet scan
  (see [specs/2026-07-01-scan-checkboxes-design.md](specs/2026-07-01-scan-checkboxes-design.md)).
- [x] Settings + Help folded into the browser window (2026-07-07 redesign, `0ec5192`).
- [x] Real version check via `_Version` parameter + model-only rows (`0bf00a8`).
- [x] **Revit 2024 process crash on Sync — `"` in a family name** (CER 2026-07-16).
  A project family whose name contains a character Windows paths forbid (Revit allows
  `"` — inch marks — in names) becomes a synthetic model-only row (`name + ".rfa"`);
  `DisplayName`'s `Path.GetFileNameWithoutExtension` then throws
  `ArgumentException: Illegal characters in path` on net48 (net8 doesn't validate).
  The throw happened inside `Sync`'s `finally` → `Dispatcher.Invoke`, which rethrows on
  the ThreadPool thread — unhandled there, it killed all of Revit. Fixed: display names
  now go through `FamilyFileName.WithoutRfaExtension` (plain string strip, also stops
  `/`-bearing names being truncated as paths), and the Sync/Rescan UI-update `Invoke`s
  are guarded so a failure degrades to a warning dialog instead of a process crash.
- [x] **OLE thumbnails never extract** — after a deep scan *no* family shows its embedded
  preview; `ThumbnailExtractor.ExtractFromRfa` returns null for every file. Pre-existing
  and not previously verified in Revit. Extraction reads the `\x05SummaryInformation` OLE
  stream (PIDSI_THUMBNAIL → VT_CF / CF_DIB) and converts the DIB to PNG via System.Drawing;
  every stage has a silent `catch → null`, so the failure point is unknown. **Next step:**
  add temporary per-stage diagnostic logging (stream missing? byte-order marker `0xFFFE`
  mismatch? thumbnail property not found? unexpected clipboard format? DIB→PNG throw?),
  deep-scan a few known-good `.rfa` in Revit, read the log to localise, then fix. Likely
  causes: modern Revit storing the preview outside SummaryInformation, an OpenMcdf 3.x
  stream-name/read difference, or a DIB header variant `System.Drawing` won't load.
- [x] **Read Only DB** — if another user had Revit open, it blocked the shared DB. Three
  causes, all fixed: (1) the refresh button fast-synced the DB with the `.rfa` files on disk
  (one write transaction per file + stale-row pruning) — refresh is now a pure read that
  re-runs the project/version compare, and reconciling the DB with disk is solely the Scan's
  job (its filenames-only mode adds/prunes with no extraction); (2) opening the browser always
  write-opened the DB to run schema migration — it now write-opens only when the DB is missing
  or its schema is behind; (3) `Microsoft.Data.Sqlite` pools connections by default, so every
  "closed" write connection kept its read-write file handle open for the rest of the Revit
  session — pooling is now off on all connections. Consequence of (1): files added to/deleted
  from the library show up after the next Scan, not on refresh.
- [x] **Deep scan is slow** — it opens every family in Revit to read parameters (which
  upgrades older families to the running version in memory), so a first full scan takes a
  long time. ETA shown (`87fa3e0`); resumable (`57c9ceb`); thumbnail-only scans avoid
  opening families — still no chunked/background resumability across app restarts.
- [x] **`SettingsCommand` is no longer on the ribbon** (settings folded into the browser,
  2026-07-07 redesign) but the class remains in `FamilyBrowser/Commands/`. Decide:
  keep as a dev-only entry point or delete.