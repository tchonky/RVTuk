# Config Hub + Dual Deep Scan — Design

**Date:** 2026-06-30
**Status:** Approved (design)
**Area:** Family Explorer / Library Indexer
**Branch:** `family-config-hub`

## Problem / motivation

Today all library settings (library root folder, ignored subfolders) and the single
"Start Deep Scan" action live in an **inline settings panel inside the Family Browser
window**, reached via a gear button. Two problems:

1. Settings are buried inside one tool's window. We want a **central Config surface on the
   Revit ribbon** that will grow to hold settings for *other* RVTuk tools too (Comparator,
   future productivity tools). So Config must move out of the Family Browser and onto the
   ribbon as its own hub.
2. There is only one deep-scan action. We want **two explicit modes**: a fast incremental
   scan, and a full re-extraction that refreshes every family — *without* destroying the
   user-curated data attached to families.

A third item from the original request — "deep scan should ignore the ignored subfolders"
— is **already implemented** (see "Already correct" below) and only needs to be preserved.

## Already correct (no behavioural change, just preserve + guard)

- **Deep scan already skips ignored subfolders.** `FamilyIndexer.Scan` checks
  `PathUtil.IsUnderIgnoredFolder` and skips extraction for ignored families while still
  adding them to `scannedPaths` so their existing rows are *not* deleted as stale.
  `IndexLibraryCommand.RunDeepScan` passes `config.IgnoredSubfolders` into the indexer.
- **Re-indexing an existing family preserves curated data.** `IndexRepository.InsertFamily`
  uses `INSERT … ON CONFLICT(RelativePath) DO UPDATE`, so an already-indexed family keeps
  its **row Id**. `UpdateFamilyMetadata` updates only `Category`, replaces `Parameters`, and
  `INSERT OR REPLACE`s the original OLE `Thumbnail`. It never touches the curated fields/
  tables: `Families.InstructionsXaml`, `Families.Tags`, `Families.IsFavorite`,
  `CustomThumbnail`, or `FamilyImage` (gallery). Those are keyed by the (stable) family Id.
- **Stale families are pruned every scan.** `FamilyIndexer.Scan` calls
  `DeleteStaleEntries(scannedPaths)`, which removes families whose `.rfa` is gone and their
  gallery folder.

The implication is important: a "rebuild" that refreshes everything **does not require
`ClearAll()`**. Forcing re-extraction over the existing rows updates parameters + original
thumbnails and prunes deleted families, while leaving all user-added information intact.

## Goals

1. A **Config** button on the RVTuk ribbon opens a Config window showing exactly the content
   of today's inline settings panel (library root, deep scan, ignored subfolders).
2. The Config window is structured as a **hub** (tabs) so other tools' settings can be added
   later without rework. One tab — "Family Library" — exists now.
3. **Two deep-scan buttons** inside the Config window:
   - **Scan New & Changed** — incremental (current default behaviour).
   - **Re-scan All Families** — force re-extraction of every family; prune deleted ones;
     **keep all curated data**.
4. Remove the gear button and inline settings panel from the Family Browser.

## Non-goals

- No physical deletion of the `.db` file and no `ClearAll()` — explicitly rejected because
  it destroys instructions, tags, favourites, custom thumbnails, and gallery images.
- No change to the per-family **Rescan** button in the Family Browser detail pane (unrelated;
  stays).
- No new settings categories for other tools yet — only the structure that allows them.
- No change to OLE/custom-thumbnail sync semantics (`CustomThumbnail.OleSynced`).

## Design

### Component 1 — Ribbon button (`RVTuk.Revit/Application.cs`)

Add a third `PushButtonData` to the RVTuk panel, after Family Browser and Project
Comparator: **Config**, wired to a new `OpenConfigCommand`. Add a `CreateConfigIcon(int)`
gear icon rendered with `DrawingVisual` (reuse the existing gear SVG path via
`Geometry.Parse`, scaled from the 24×24 path coordinates to the icon size), matching the
dark-background style of the other two icons.

### Component 2 — `OpenConfigCommand` (`RVTuk.Revit/Commands/OpenConfigCommand.cs`, new)

An `IExternalCommand` that mirrors `BrowseLibraryCommand`'s host scaffolding:

- Ensure a `System.Windows.Application` exists with `ShutdownMode.OnExplicitShutdown`.
- Load config (no "must be configured" gate — the Config window is *where* you configure the
  folder; the deep-scan buttons are disabled until a valid library folder is set).
- Build the two deep-scan delegates (they need the Revit `UIApplication` for extraction):
  - `Action scanNewAndChanged = () => IndexLibraryCommand.RunDeepScan(uiApp, ConfigManager.LoadConfig(), forceReextractAll: false);`
  - `Action rescanAll        = () => IndexLibraryCommand.RunDeepScan(uiApp, ConfigManager.LoadConfig(), forceReextractAll: true);`
- Open a **modeless, single-instance** `ConfigWindow` tracked as
  `Application.ConfigWindow` (same pattern as `BrowserWindow`): if already open, `Activate()`;
  else create and `Show()`. Modeless so the deep-scan progress window layers cleanly.
- Reuse the same `DispatcherUnhandledException` crash-logging wrapper used by
  `BrowseLibraryCommand` (factor the shared bits if convenient, but duplication is acceptable
  to keep this change contained).

### Component 3 — `ConfigWindow` + `ConfigViewModel` (`RVTuk.UI`, new)

Dark-themed WPF window (`Topmost`, `WindowStartupLocation=CenterScreen`), using
`Themes/DarkTheme.xaml` like the other windows. Layout: a `TabControl` with one `TabItem`
**"Family Library"** whose content is the settings panel moved out of
`FamilyBrowserWindow.xaml` (the `ScrollViewer` block), adapted to two scan buttons:

- **LIBRARY ROOT FOLDER** — read-only `TextBox` bound to `LibraryFolderPath` + **Browse…**
  button. Browsing validates via `LibraryFolderValidator`, persists via `ConfigManager`,
  creates `.Setup`, and updates the VM.
- **DEEP SCAN** — description text + two buttons:
  - **Scan New & Changed** → `ScanNewCommand`
  - **Re-scan All Families** → `RescanAllCommand`
  Both disabled when no valid library folder is configured.
- **IGNORED SUBFOLDERS** — multiline `TextBox` bound to `IgnoredSubfoldersText`
  (TwoWay / LostFocus), saved to config on change — same logic as today.

`ConfigViewModel` responsibilities:

- Holds `AppConfig` (loaded via `ConfigManager`), exposes `LibraryFolderPath`,
  `DerivedDatabasePath` (display), `IgnoredSubfoldersText` (parse/save like the current
  `FamilyBrowserViewModel.IgnoredSubfoldersText`).
- `ScanNewCommand` → invokes injected `Action scanNewAndChanged`.
- `RescanAllCommand` → shows the confirmation dialog (Component 5); on Yes, invokes injected
  `Action rescanAll`.
- `BrowseLibraryCommand` → folder dialog + validate + save + reload.
- After a library-folder change is saved, if `Application.BrowserWindow` is open, reload it
  so it does not show a stale library. (Light coupling via an injected
  `Action onLibraryFolderChanged` supplied by `OpenConfigCommand`, so the UI project keeps no
  reference to the Revit project / window.)

Construction parallels `FamilyBrowserWindow`: the Revit project injects the delegates; the UI
project sees only `Action`/`Func` (no Revit types).

### Component 4 — Force-re-extract scan (`RVTuk.Core` + `RVTuk.Revit`)

- `FamilyIndexer.Scan(Action<string,int,int> progress, CancellationToken ct,
  bool forceReextractAll = false)`. Change the gate to:
  `bool needsExtraction = forceReextractAll || existing == null
      || existing.FileSize != fileSize
      || Math.Abs((existing.ModifiedDate - modifiedDate).TotalSeconds) > 1;`
  Everything else (ignored-folder skip, stale deletion, long-path skip) is unchanged, so
  "Re-scan All" automatically still ignores ignored subfolders and prunes deleted families.
- `IndexLibraryCommand.RunDeepScan(UIApplication uiApp, AppConfig config,
  bool forceReextractAll)` — pass the flag through to `indexer.Scan(...)`.
- Keep the existing incremental call site working (the `IndexLibraryCommand.Execute`
  IExternalCommand path, if still used, passes `forceReextractAll: false`).

### Component 5 — Confirmation dialog for "Re-scan All"

Because nothing is destroyed, this is a *time* warning, not a *data-loss* warning. Before
running the forced re-extraction, show a Yes/No `MessageBox`:

> **Re-scan all families?**
> This re-reads every family in the library to refresh parameters and thumbnails, and removes
> families whose files no longer exist. It can take a long time for a large library. Your
> instructions, pictures, custom thumbnails, tags, and favourites are kept. Continue?

"Scan New & Changed" runs with no prompt.

### Component 6 — Remove the inline settings panel + redundant rebuild

- **`FamilyBrowserWindow.xaml`**: remove the gear `Button` (the Config column in LINE 1) and
  the entire settings `ScrollViewer` block (`IsShowingSettings`). The right side then only
  hosts the family-detail grid.
- **`FamilyBrowserViewModel`**: remove `ToggleSettingsCommand`, `IsShowingSettings`,
  `IgnoredSubfoldersText`, `DeepScanCommand`, the `_deepScan` field and its constructor
  parameter. Simplify `ShowFamilyDetail` to `HasSelection`. Keep `_config.IgnoredSubfolders`
  usage in `ApplyFilter` (the browser still hides ignored families). Keep everything else
  (Sync, per-family Rescan, load/update, favourites, tags).
- **`FamilyBrowserWindow.xaml.cs`**: remove `BrowseLibraryFolder_Click` and the `_deepScan`
  field/parameter.
- **`BrowseLibraryCommand`**: stop building/passing the `deepScan` delegate to the browser.
- **`IndexProgressWindow.xaml` / `IndexProgressViewModel`**: remove the **"Rebuild Index"**
  button, `RebuildIndexCommand`, and the `RebuildRequested` event (the old `ClearAll`
  mechanism). `IndexLibraryCommand.RunDeepScan` no longer subscribes to `RebuildRequested`.
  The progress window keeps **Cancel**.

## Data flow

```
Ribbon "Config"
  └─ OpenConfigCommand
       ├─ builds scanNewAndChanged / rescanAll (bound to UIApplication)
       ├─ builds onLibraryFolderChanged (reload BrowserWindow if open)
       └─ opens single-instance ConfigWindow(ConfigViewModel)
            ├─ Browse…           → validate + ConfigManager.SaveConfig + onLibraryFolderChanged
            ├─ Ignored subfolders→ ConfigManager.SaveConfig (on change)
            ├─ Scan New & Changed→ RunDeepScan(..., forceReextractAll:false)
            └─ Re-scan All       → confirm → RunDeepScan(..., forceReextractAll:true)
                                      └─ FamilyIndexer.Scan(force) → IndexingEvent ping-pong
                                           (skips ignored, prunes stale, preserves curated)
```

## Error handling

- Config window uses the same dispatcher-unhandled-exception crash logging as the browser.
- Invalid library folder on Browse → `MessageBox` warning; config not changed.
- Deep-scan errors continue to surface through the existing `RunDeepScan` try/catch +
  completion `TaskDialog`.
- Deep-scan buttons disabled until a valid library folder is configured (avoids running a
  scan with no DB path).

## Testing

Core logic (`RVTuk.Core`) is unit-testable; UI/Revit wiring is verified in-Revit.

- **Unit (`FamilyIndexer.Scan` force flag):** with a fake/temp `IndexRepository`,
  - `forceReextractAll:false` returns work items only for new/changed files;
  - `forceReextractAll:true` returns work items for **all** non-ignored, non-long-path files;
  - ignored-subfolder files are excluded from work items in both modes but remain in
    `scannedPaths` (not pruned);
  - a family whose file is absent is pruned (`DeleteStaleEntries`) in both modes.
- **Unit (preservation):** after `UpdateFamilyMetadata` on an existing family, its
  `InstructionsXaml`, `Tags`, `IsFavorite` and any `CustomThumbnail`/`FamilyImage` rows are
  unchanged; `Parameters` and `Thumbnail` are refreshed; row `Id` is stable across re-index.
- **Manual in-Revit:** ribbon shows Config; window opens with the three sections; Browse sets
  the folder; "Scan New & Changed" indexes only changes; "Re-scan All" prompts then refreshes
  all and keeps curated data; ignored subfolders are skipped; Family Browser no longer shows a
  gear/settings panel and still hides ignored families.

## Open questions

None blocking. Button wording ("Scan New & Changed", "Re-scan All Families") is final unless
changed during implementation.
