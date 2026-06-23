# ReviTchucky — Backlog

Running list of additions and modifications. Brain-dump goes under the right
section; check off with `[x]` and the commit hash when shipped.

---

## 🐞 Bugs / things to fix

- [ ] When click the family to edit the instructions, Browser crashes and gives the error:
ReviTchucky Family Browser encountered an error:
XamlParseException: Add value to collection of type
'System.Windows.Controls.UlElementCollection' threw an
exception.
Details written to
C:\Users\danie\AppData\Local\ReviTchucky\crash.log

## ✨ Improvements (to existing features)

- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. Ideas to explore: better progress + time estimate,
  make it resumable, or a faster path for families that don't need full metadata.
- [ ] When deep scanning, on **Cancel** keep everything already extracted so the time
  isn't lost; the next scan then resumes naturally (unchanged families are skipped).
  ⚠️ Needs a fix: today a family's row is written with the new size/date *before* its
  metadata is extracted, so a cancelled family looks "up to date" and won't be re-read
  next time. Defer the size/date update until after extraction succeeds.
- [ ] Option to deep-scan **just thumbnails** (fast) and/or **just parameters** (slow).

## 🚀 New features / ideas

- [ ] Option to **ignore one or more subfolders** in the deep scan.
- [ ] A **tags** section per family + search by tags.
- [ ] Filter the list by **Revit version** (e.g. show only families saved in 2023, or
  2023–2024). Needs the per-family Revit year surfaced from the index (already captured
  during extraction as `FileRevitYear`).

## ⏳ Known deferred (from the Family Explorer build/review — decided "later")

- [ ] Verify gallery images load from a `\\server\share` (UNC) path; if not, switch
  `GalleryItemViewModel` to `new Uri(path, UriKind.Absolute)`.
- [ ] Editor gallery loads image files synchronously on the UI thread — could stutter
  on a slow share with many images; load off-thread if galleries grow.
- [ ] Add a confirmation prompt before **Delete** removes a gallery image (permanent).
- [ ] Drag-to-reorder gallery images (the `ReorderImages` API exists; UI not wired).
- [ ] Surface a count of families skipped during a scan (too-new, too-long path,
  unreadable) so the admin knows why some are missing. Create a log and write in the DB folder.
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
