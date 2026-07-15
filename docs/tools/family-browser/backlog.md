# Family Browser — Backlog

Bugs, improvements, and ideas for the Family Browser tool. Toolkit-wide items live in
[`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] **OLE thumbnails never extract** — after a deep scan *no* family shows its embedded
  preview; `ThumbnailExtractor.ExtractFromRfa` returns null for every file. Pre-existing
  and not previously verified in Revit. Extraction reads the `\x05SummaryInformation` OLE
  stream (PIDSI_THUMBNAIL → VT_CF / CF_DIB) and converts the DIB to PNG via System.Drawing;
  every stage has a silent `catch → null`, so the failure point is unknown. **Next step:**
  add temporary per-stage diagnostic logging (stream missing? byte-order marker `0xFFFE`
  mismatch? thumbnail property not found? unexpected clipboard format? DIB→PNG throw?),
  deep-scan a few known-good `.rfa` in Revit, read the log to localise, then fix. Likely
  causes: modern Revit storing the preview outside SummaryInformation, an OpenMcdf 3.x
  stream-name/read difference, or a DIB header variant `System.Drawing` won't load.

## ✨ Improvements

- [ ] **Deep-scan re-entrancy.** The embedded Settings panel exposes one **Scan** button; a
  user can still click it twice in a row while a scan is in progress. Both runs would share
  `IndexingHandler` / `IndexingEvent` and race. Disable the Scan button (or guard
  `RunDeepScan`) while a scan is in progress.
- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters (which
  upgrades older families to the running version in memory), so a first full scan takes a
  long time. ETA shown (`87fa3e0`); resumable (`57c9ceb`); thumbnail-only scans avoid
  opening families — still no chunked/background resumability across app restarts.
- [ ] **`SettingsCommand` is no longer on the ribbon** (settings folded into the browser,
  2026-07-07 redesign) but the class remains in `FamilyBrowser/Commands/`. Decide:
  keep as a dev-only entry point or delete.

## 🚀 Ideas

- [ ] **Tags follow-ups** (base `c742a9b`, clickable chips `c625502`): a tag auto-complete /
  pick-from-existing list so spelling stays consistent; a dedicated "has tag" filter
  separate from the free-text search.
- [ ] **Recently used** — track the last N families loaded into a project for quick access.
- [ ] Toolbar polish: the ⬅️/⭐/➡️ toggle buttons and Sync button use default (light) WPF
  chrome; style them to match the dark theme. Also style the category checkbox popover.

## ⏳ Deferred (decided "later" during the Family Explorer build)

- [ ] Parameter **write-back** — let the tool actually fix/reorganize parameters in the
  families (currently view/audit only).
- [ ] UI styling/layout polish for the parameter regions.

## ✅ Done

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
- [x] Fix: ignored-folder list **now updates** the browser view when changed (`bd5ec13`).
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
