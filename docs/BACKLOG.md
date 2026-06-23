# ReviTchucky — Backlog

Running list of additions and modifications. Brain-dump goes under the right
section; check off with `[x]` and the commit hash when shipped.

---

## 🐞 Bugs / things to fix

- [~] When closed, Revit crashes (access violation `c0000005` in `siappdll.dll`).
      **Diagnosed: NOT ReviTchucky.** `siappdll.dll` / `3DxRevit.dll` is the 3Dconnexion
      SpaceMouse driver (`C:\Program Files\3Dconnexion\3DxWare\...`). The crash is on a
      non-main native thread during Revit shutdown; every ReviTchucky journal entry is a
      normal startup/command event. Fix is on 3Dconnexion's side: update or temporarily
      disable the 3Dconnexion add-in / SpaceMouse driver. (CER dump:
      `…\Local\Autodesk\CER\92ed161c…\29`.)
      

## ✨ Improvements (to existing features)

- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. ETA now shown (`87fa3e0`); still want: make it
  resumable, or a faster path for families that don't need full metadata.
- [ ] When deep scanning, on **Cancel** keep everything already extracted so the time
  isn't lost; the next scan then resumes naturally (unchanged families are skipped).
  ⚠️ Needs a fix: today a family's row is written with the new size/date *before* its
  metadata is extracted, so a cancelled family looks "up to date" and won't be re-read
  next time. Defer the size/date update until after extraction succeeds. (Note: also
  interacts with the fast Sync, which writes size/date too — design carefully.)
  Concrete plan (do deliberately, with a Revit test): `FamilyIndexer.Scan` should insert
  new/changed rows with a sentinel size/date (e.g. 0 / `DateTime.MinValue`) and have
  `InsertFamily` stop updating ModifiedDate/FileSize on conflict; carry the real
  ModifiedDate+FileSize on `ExtractionWorkItem`; `IndexRepository.UpdateFamilyMetadata`
  writes them only after extraction commits. Cancelling then leaves stale size/date →
  re-scanned next time. ⚠️ Sync's `UpsertFamily` writes real size/date with no metadata,
  so a Sync-then-DeepScan already skips those families — decide whether Sync should mark
  rows "needs deep scan" too.
- [ ] Option to deep-scan **just thumbnails** (fast) and/or **just parameters** (slow).

## 🚀 New features / ideas

- [ ] **Tags follow-ups** (base `c742a9b`, clickable chips `c625502`): a tag auto-complete /
  pick-from-existing list so spelling stays consistent; a dedicated "has tag" filter
  separate from the free-text search.
- [ ] **Favorites / pinned families** — star a family, quick-filter to starred only.
- [ ] **Recently used** — track the last N families loaded into a project for quick access.

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
- [x] `Deploy.ps1`: per-version resilience (skip a locked/open Revit year), version filter,
  colored summary (`621f880`).
