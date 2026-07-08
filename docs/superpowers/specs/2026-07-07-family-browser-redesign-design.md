# Family Browser Redesign — Design

**Date:** 2026-07-07
**Status:** Approved / shipped
**Area:** Family Management (VISION pillar 1)

> This is a discrete record of a single round of Family Browser changes, per the repo's
> convention of one dated spec per significant round (see `2026-06-22-family-explorer-design.md`
> for a prior example of a "round 2" redesign doc). It has since been folded into the
> consolidated [`family-browser-design.md`](family-browser-design.md), which is the ongoing
> single source of truth — read that doc for the current state; read this one for why these
> specific changes were made in this round.
>
> **Correction, same day (2026-07-07):** §2 below describes the *first-shipped* version of the
> source/status toggles, using exclusion semantics (all on by default; off hides). Real usage
> surfaced a bug immediately: since ⬅️/➡️ are a complementary, exhaustive pair, turning both off
> to isolate ⭐ (favourites) excluded every family instead of narrowing to favourites — there was
> no OR path back in. Corrected to inclusion/OR semantics: all three start **off** (unfiltered);
> pressing any combination shows the union of what's pressed. §2's body below is left as a
> historical record of the first-shipped (wrong) version; `family-browser-design.md` §1 has the
> corrected, current behaviour.

---

## Summary

A UI/UX and settings-architecture redesign of the Family Browser touching the category filter,
the source/status filters, the Sync control, the Load/Update action, family-detail action icons,
the (removed) gallery feature, and where Settings live. Nine changes, described below.

---

## 1. Category filter: ComboBox → checkbox popover

The single-select category `ComboBox` was replaced with a checkbox popover
(`CategoryFilterOption.cs`, new): an "All categories" master checkbox (tri-state:
checked/unchecked/indeterminate) plus one checkbox per category, all **on by default**.
Unchecking a category hides its families from the list immediately. This is a genuine
multi-select (pick any combination of categories), not the old single-select.

## 2. Source/status filters replaced with exclusion toggles

Removed entirely:
- The old Sync popover's "In the project" / "Outdated" checkboxes.
- The separate favourites-only star toggle.
- The Versions (Revit-year) multi-select popover — this filter *capability* is gone, no
  replacement. `VersionFilterOption.cs` was deleted.

Replaced by **three always-visible toggle buttons** in the toolbar, all **on by default**
(nothing hidden):
- ⬅️ — families loaded in the project.
- ⭐ — favourites.
- ➡️ — library-only families (not loaded in the project).

Switching a toggle off **excludes** the families it represents — this is exclusion semantics,
a deliberate inversion of the old inclusion semantics ("show only X"). ⬅️ and ➡️ are a
complementary pair (every family is one or the other); ⭐ cuts across both independently.

## 3. Sync is a plain button

The loop-arrow icon that used to open a popover menu (housing the checkboxes from #2) is now a
plain button. Clicking it directly re-runs the project/version check (unchanged logic — see
`family-browser-design.md` §3). No menu.

## 4. Load/Update merged into one button

Previously two separate buttons: "Load into Project", and a conditionally-shown
"↑ Update in Project". Now a single button:
- Reads **"Load"** normally.
- Reads **"Update"** with an **orange background** (`Brush.UpdateOrange`, `#D9822B`, new in
  `DarkTheme.xaml`) when a newer version exists in the library than what's loaded in the project.

Positioned directly right of the family name/info block, vertically centered against the
thumbnail's height. The old orange/red "↑ Newer version available in library" banner was
removed entirely — the orange Update button now carries that signal on its own, so the same
information isn't shown twice.

## 5. Action icons relocated + Open added

Edit Info and Rescan were previously text buttons in a row below the thumbnail. They moved to a
stacked, icon-only column in the top-right corner of the family detail panel:

1. **Rescan** — circular-arrow icon only, no text.
2. **Edit Info** — notepad+pen icon.
3. **Open** (new) — box-with-arrow-breaking-out icon.

**Open** sends the family's `.rfa` straight to Revit's Family Editor via a new
`Autodesk.Revit.UI.IExternalEventHandler`:

- `src\RVTuk.Revit\ExternalEvents\OpenFamilyEditorEventHandler.cs` (new) — mirrors the existing
  `LoadFamilyEventHandler` pattern (`Prepare`/`Execute`/`WaitForCompletion`), calls
  `app.Application.OpenDocumentFile(path)`.
- Wired into `Application.cs` — new `OpenFamilyEditorHandler` / `OpenFamilyEditorEvent` statics.
- Wired into `BrowseLibraryCommand.cs` — new `Action<string> openInFamilyEditor` delegate passed
  into the window.
- Exposed as `FamilyBrowserViewModel.OpenFamilyEditorCommand`.

## 6. Gallery feature removed entirely

No more Gallery tab in the Family Browser's detail pane (`GalleryItems` collection deleted from
`FamilyBrowserViewModel`); no more gallery management (add/caption/reorder/delete) in the
Instructions Editor window.

**Rationale:** pictures now live only as images embedded inline in the rich-text Instructions
body — a capability that already existed (drag-drop/paste into the instructions editor) and
made the separate gallery redundant.

**Deleted:**
- `FamilyImage.cs` (model)
- `GalleryItemViewModel.cs`
- The `FamilyImage` DB table creation — `BrowserRepository.cs` and `IndexRepository.cs` each had
  their own `CREATE TABLE IF NOT EXISTS FamilyImage`; both removed so brand-new databases never
  get that table.
- Repository methods `GetImages` / `AddImage` / `UpdateCaption` / `DeleteImage` /
  `ReorderImages` / `GetGalleryPath`.

**Important — non-destructive by design:** no migration drops the `FamilyImage` table or deletes
existing rows/files on any already-deployed shared database. The code simply stops
creating/reading/writing gallery data going forward; a production DB may still carry historical
gallery rows and files, untouched. Orphan-folder cleanup (deleting a *deleted family's* leftover
gallery folder in `DeleteStaleEntries`) was kept as-is — that's unrelated pre-existing cleanup,
not a gallery-editing feature, and stays regardless of the gallery UI's removal.

## 7. Settings moved from a ribbon window into the Family Browser itself

**This is the most doc-significant change in this round — an architecture reversal.**

Deleted: the ribbon **Config** button, `ConfigWindow.xaml`/`.xaml.cs`, and
`OpenConfigCommand.cs`.

Settings (library root folder, deep scan with "Update thumbnails"/"Update parameters"
checkboxes + Scan button, ignored subfolders) now render directly in the Family Browser's right
panel, toggled by a new ⚙️ gear button in a new footer row underneath the family list (left
rail). The underlying `ConfigViewModel` class was **kept and reused** — it's now composed as
`FamilyBrowserViewModel.Settings` rather than duplicated.

This directly reverses the design in
[`docs/archive/2026-06-30-config-hub-deep-scan-design.md`](../../archive/2026-06-30-config-hub-deep-scan-design.md)
(archived as part of this same round), which explicitly moved settings *out* of the Family
Browser and onto the ribbon so it could grow into a shared hub for other tools' settings
(Comparator, future tools) as additional tabs. That doc's central premise no longer holds: every
tab it ever grew was the single "Family Library" one, and it's gone now. **This was a deliberate,
explicit product decision** made when this redesign was scoped, not an oversight or a
regression.

## 8. New Help/About panel

A new ℹ️ button in the same new footer row (to the left of ⚙️) toggles the right panel to a
markdown-rendered help/about page instead of the family detail.

Content is fetched over HTTP from a raw GitHub URL — currently a **placeholder constant** in
`FamilyBrowserViewModel.cs`:

```
HelpMarkdownUrl = "https://raw.githubusercontent.com/knafo-klimor/rvtuk-docs/main/help.md"
```

> ⚠️ **This needs to be pointed at the real docs repo before this ships.**

Rendered via a new hand-rolled markdown→`FlowDocument` converter,
`RVTuk.UI.Helpers.MarkdownConverter` (no new NuGet dependency — deliberately dependency-free),
consumed through a new `RichTextBoxHelper.MarkdownSource` attached property (mirrors the
existing `DocumentXaml` attached property used for Instructions). Supports headings, paragraphs,
bold/italic, inline code, `[text](url)` links, bullet lists, and `---` horizontal rules.

## 9. Filter default-visibility behavior changed

Previously, once Sync had run, the app defaulted to hiding library-only families
(`ShowInProjectOnly` defaulted to `true`, an inclusion filter). Now, by default, **nothing is
hidden post-sync** — all three new toggle buttons (§2) start "on" (nothing excluded); users
explicitly narrow down by switching a toggle off. This is a deliberate UX change, not a bug —
it follows directly from the inclusion→exclusion semantics flip in #2.

---

## New / changed files (reference)

| File | Change |
|---|---|
| `CategoryFilterOption.cs` | new |
| `VersionFilterOption.cs` | deleted |
| `GalleryItemViewModel.cs` | deleted |
| `FamilyImage.cs` | deleted |
| `ConfigWindow.xaml` / `.xaml.cs` | deleted |
| `OpenConfigCommand.cs` | deleted |
| `OpenFamilyEditorEventHandler.cs` | new (`src\RVTuk.Revit\ExternalEvents\`) |
| `MarkdownConverter.cs` | new (new `Helpers` folder in `RVTuk.UI`) |
| `DarkTheme.xaml` | `Brush.UpdateOrange` (`#D9822B`) added |

---

## Consolidation note

All nine changes above are reflected in the consolidated
[`family-browser-design.md`](family-browser-design.md) (§1, §1a, §1b, §3, §4, §4a, §4b, §6 note,
§7 note, §8, and the *Deviations* section). This doc is kept as the discrete historical record
of the round; the consolidated doc is what to read and update going forward.
