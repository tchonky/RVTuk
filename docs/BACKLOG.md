# ReviTchucky — Backlog

Running list of additions and modifications. Brain-dump goes under the right
section; check off with `[x]` and the commit hash when shipped.

---

## 🐞 Bugs / things to fix

- [ ] I'm not sure, but when closed, Revit crashes, giving the following:
        Date/Time: 2026-06-23 11:49:52 +03:00
        Application: Revit.exe
        Error: Access violation - code c0000005 (first/second chance not available)
        Crashed Module Name: siappdll.dll
        Exception Address: 0x0000020d8023d39e
        Exception Code: c0000005
        Exception Flags: 0
        Exception Parameters: 0, 20d9a350004
      also check: C:\Users\danie\AppData\Local\Autodesk\CER\92ed161c792ec7321aadc8abc2616368c8ce96b2\29
      

## ✨ Improvements (to existing features)

- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. Ideas to explore: better progress + time estimate,
  make it resumable, or a faster path for families that don't need full metadata.
- [ ] When deep scanning, on **Cancel** keep everything already extracted so the time
  isn't lost; the next scan then resumes naturally (unchanged families are skipped).
  ⚠️ Needs a fix: today a family's row is written with the new size/date *before* its
  metadata is extracted, so a cancelled family looks "up to date" and won't be re-read
  next time. Defer the size/date update until after extraction succeeds. (Note: also
  interacts with the fast Sync, which writes size/date too — design carefully.)
- [ ] Option to deep-scan **just thumbnails** (fast) and/or **just parameters** (slow).

## 🚀 New features / ideas

- [ ] A **tags** section per family + search by tags.

## ⏳ Known deferred (from the Family Explorer build/review — decided "later")

- [ ] Editor gallery loads image files synchronously on the UI thread — could stutter
  on a slow share with many images; load off-thread if galleries grow.
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
