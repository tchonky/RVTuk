# ReviTchucky — Backlog

Running list of additions and modifications. Brain-dump goes under the right
section; check off with `[x]` and the commit hash when shipped.

---

## 🐞 Bugs / things to fix

- [ ]  Make Family Browser window always on top.
- [ ]  wrap the name of the family to fit in the panel, up to the size of the thumbnail (2 or 3 lines.)

## ✨ Improvements (to existing features)

- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. Ideas to explore: better progress + time estimate,
  make it resumable, or a faster path for families that don't need full metadata.

- [ ]  When deep scanning, when pressing 'cancel', write all the information that you have already
so you don loose the scan time. Also, in the next deep scan, because we're checking any modification,
if there is no modification, it will work as a resuming the deep scan.
- [ ]  Possibility of deep scan just the thumbnails (faster) and/or the parameters. (slower)

## 🚀 New features / ideas

- [ ]  When a family is selected, create a button on the right that deep scan just that family.
- [ ]  Option to ignore one or more subfolders in the deep scan
- [ ]  When writing to search for a family, words separated with space, can appear on any place in the name of the family.
example: If I search for 'door single' it can match with 'door - single', 'single door', kk_single wooden door', etc.
- [ ]  A tags section for each family and a possibility to search by tags
- [ ]  Option to show one or more versions of the revit families. Like, only show families that are 2023 or up to 2024.

## ⏳ Known deferred (from the Family Explorer build/review — decided "later")

- [ ] Verify gallery images load from a `\\server\share` (UNC) path; if not, switch
  `GalleryItemViewModel` to `new Uri(path, UriKind.Absolute)`.
- [ ] Editor gallery loads image files synchronously on the UI thread — could stutter
  on a slow share with many images; load off-thread if galleries grow.
- [ ] Add a confirmation prompt before **Delete** removes a gallery image (permanent).
- [ ] Drag-to-reorder gallery images (the `ReorderImages` API exists; UI not wired).
- [ ] Surface a count of families skipped during a scan (too-new, too-long path,
  unreadable) so the admin knows why some are missing. Create a log and write in the DB folder
- [ ] Parameter **write-back** — let the tool actually fix/reorganize parameters in
  the families (currently view/audit only).
- [ ] UI styling/layout polish for the new gallery + parameter regions.

## ✅ Done

- [x] Family Explorer: network-share concurrency, parameter audit (Group/Kind +
  filter), image gallery — branch `family-explorer`.
- [x] Fix: WPF `Application.Current` null crash on Browse Library (`90b96d6`).
- [x] Fix: `LibraryFolderPath` read-only TwoWay binding crash (`7771823`).
- [x] Fix: scan aborting on Windows MAX_PATH; now skips over-long paths (`4b9998f`).
