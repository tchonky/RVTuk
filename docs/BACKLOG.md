# RVTuk — Backlog

Running list of additions and modifications. Brain-dump goes under the right
section; check off with `[x]` and the commit hash when shipped.
For the product vision and roadmap behind these items, see [`../VISION.md`](../VISION.md).

---

## ▶ Status / next session (read me first)

- All work is on branch **`family-explorer`**; **`main`** = safe pre-feature baseline. **Not merged.**
- **Both configs build clean** (`Release2024`/net48, `Release2025`/net8). A large UI batch
  (tags, favourites, two-line toolbar, multi-version filter, in-project/outdated filters, scan
  ETA, off-thread gallery) is **committed but NOT yet verified in Revit**.
- **To test:** close Revit 2024 → elevated `.\Deploy.ps1 2024` → restart Revit → Browse Library.
  Confirm: toolbar popups open, favourites ★ persist, tags edit/search, version multi-select,
  Sync dropdown filters. ⚠️ "In the project" is **checked by default**, so the list narrows to
  project families after the first Sync — user may want it default-unchecked (awaiting feedback).
- **Then** decide on merging `family-explorer` → `main`.
- Working style: replies terse; **minimal code comments** (comment once it works); run git for the
  user (git novice) and explain simply; move items to Done here with the commit hash as they ship.

---

## 🐞 Bugs / things to fix

- [ ] **OLE thumbnails never extract** — after a deep scan *no* family shows its embedded
  preview; `ThumbnailExtractor.ExtractFromRfa` returns null for every file. Pre-existing
  (unchanged by the Config-hub work) and not previously verified in Revit. Extraction reads the
  `\x05SummaryInformation` OLE stream (PIDSI_THUMBNAIL → VT_CF / CF_DIB) and converts the DIB to
  PNG via System.Drawing; every stage has a silent `catch → null`, so the failure point is
  unknown. **Next step:** add temporary per-stage diagnostic logging (stream missing? byte-order
  marker `0xFFFE` mismatch? thumbnail property not found? unexpected clipboard format? DIB→PNG
  throw?), deep-scan a few known-good `.rfa` in Revit, read the log to localise, then fix.
  Likely causes: modern Revit storing the preview outside SummaryInformation, an OpenMcdf 3.x
  stream-name/read difference, or a DIB header variant `System.Drawing` won't load.

- [~] When closed, Revit crashes (access violation `c0000005` in `siappdll.dll`).
      **Diagnosed: NOT RVTuk.** `siappdll.dll` / `3DxRevit.dll` is the 3Dconnexion
      SpaceMouse driver (`C:\Program Files\3Dconnexion\3DxWare\...`). The crash is on a
      non-main native thread during Revit shutdown; every RVTuk journal entry is a
      normal startup/command event. Fix is on 3Dconnexion's side: update or temporarily
      disable the 3Dconnexion add-in / SpaceMouse driver. (CER dump:
      `…\Local\Autodesk\CER\92ed161c…\29`.)
      

## ✨ Improvements (to existing features)

- [ ] **Config: ignored-list change doesn't refresh an open browser.** Editing IGNORED
  SUBFOLDERS in the Config window saves config but does not refresh an already-open Family
  Browser (the old inline panel refreshed instantly via `ApplyFilter`). Wire the
  ignored-list change to the existing browser-reload delegate (`onLibraryFolderChanged`
  in `OpenConfigCommand` → `FamilyBrowserWindow.ReloadConfig`). Deferred: needs an in-Revit
  check that the reload doesn't flicker/steal focus.
- [ ] **Deep-scan re-entrancy.** With the modeless Config window and two scan buttons, a user
  can start a second scan while one is running; both share `IndexingHandler` /
  `IndexingEvent` and would race. Disable the scan buttons (or guard `RunDeepScan`) while a
  scan is in progress. Pre-existing risk, now easier to hit.


- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. ETA now shown (`87fa3e0`); resumable (`57c9ceb`);
  now also fixed for the "opens every family" part when only thumbnails are needed
  (see below) — still no chunked/background resumability across app restarts.
- [x] When deep scanning, on **Cancel** keep everything already extracted so the time
  isn't lost (`57c9ceb`). New/changed rows now carry a sentinel size/date; the real
  ModifiedDate+FileSize ride on `ExtractionWorkItem` and `UpdateFamilyMetadata` writes them
  only after extraction commits, so a cancelled family stays stale and is re-scanned next
  time. *(Verify the cancel/resume end-to-end in Revit.)*
- [x] Option to deep-scan **just thumbnails** (fast) and/or **just parameters** (slow), and fix
  the fast Sync's families being invisible to the deep scan forever — replaced the two Config
  scan buttons with one **Scan** button + **Update thumbnails** / **Update parameters**
  checkboxes; each facet's "needs refresh" now also fires when that family is simply missing
  the data (not just when the file changed), so a Sync-only family gets picked up on the next
  facet scan. See `docs/superpowers/specs/2026-07-01-scan-checkboxes-design.md`.

## 🚀 New features / ideas

- [ ] **Tags follow-ups** (base `c742a9b`, clickable chips `c625502`): a tag auto-complete /
  pick-from-existing list so spelling stays consistent; a dedicated "has tag" filter
  separate from the free-text search.
- [ ] **Recently used** — track the last N families loaded into a project for quick access.
- [ ] Toolbar polish: the new Version/Sync/Favourites buttons use default (light) WPF chrome;
  style them to match the dark theme. Also style the popup checkboxes.

## ⏳ Known deferred (from the Family Explorer build/review — decided "later")

- [ ] Parameter **write-back** — let the tool actually fix/reorganize parameters in
  the families (currently view/audit only).
- [ ] UI styling/layout polish for the new gallery + parameter regions.

## ✅ Done

- [x] Family Explorer: network-share concurrency, parameter audit (Group/Kind +
  filter), image gallery — branch `family-explorer`.
- [x] Fix: WPF `Application.Current` null crash on Browse Library (`90b96d6`).
- [x] Fix: `LibraryFolderPath` read-only TwoWay binding crash (`7771823`).
- [x] Fix: scan aborting on Windows MAX_PATH; now skips over-long paths (`4b9998f`).
- [x] Family Browser window **always on top**; long family names **wrap** to 2–3 lines (`3813ecf`).
- [x] **Multi-word search** — all words match, any order/position (`0336bad`).
- [x] **Per-family Rescan** button — re-extract just the selected family (`1b3c52d`).
- [x] Fix: editor crash on open — `ContextMenu` parented in a Grid (`958f614`).
- [x] Gallery: UNC-safe image `Uri` + confirm before deleting an image (`fd60009`).
- [x] **Ignore subfolders** in deep scan + sync (configurable in Settings) (`534a6f5`).
- [x] Fix: ignored-folder list **now updates** the browser view when changed (`bd5ec13`).
- [x] Filter the list by **Revit version** — RevitYear column + version dropdown (`e08ce65`).
- [x] Gallery: **reorder images** with ◀/▶ buttons in the editor (`bb87e65`).
- [x] **Skipped-family count** — deep-scan dialog + `last-scan.log` report skips (`46dada3`).
- [x] **Per-family tags** — editable in the info editor, searchable, shown in detail (`c742a9b`);
  clickable tag chips that filter the list (`c625502`).
- [x] Editor gallery images now **decode off the UI thread** (no stutter on slow shares) (`191c214`).
- [x] Deep-scan progress shows **estimated time remaining** (`87fa3e0`).
- [x] **Two-line toolbar**: line 1 Category + multi-select Version + Config; line 2 Search +
  Favourites + Sync dropdown (`f04eef1`).
- [x] **Favourites** — star a family (list row + detail), "favourites only" filter (`f04eef1`).
- [x] **Multi-select version** filter (checkbox popup, pick several years) (`f04eef1`).
- [x] **"In the project" / "Outdated" filters** in the Sync dropdown; runs the project check on
  first open, filters apply after (`f04eef1`).
- [x] `Deploy.ps1`: per-version resilience (skip a locked/open Revit year), version filter,
  colored summary (`621f880`).
