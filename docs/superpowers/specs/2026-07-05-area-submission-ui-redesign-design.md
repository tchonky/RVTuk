# Area Submission Window Redesign — Design

**Date:** 2026-07-05
**Status:** Approved (design)
**Area:** Productivity tools — Area Calc (Rishui Zamin) UI
**Branch:** TBD

> UI-only redesign of the existing `AreaSubmissionWindow` / `AreaSubmissionViewModel`
> (see [`2026-07-01-rishui-zamin-area-submission-design.md`](2026-07-01-rishui-zamin-area-submission-design.md)
> for the feature's data flow, DXF/DAT generation, and Revit-side extraction — none of that
> changes here). Also introduces a global theme palette change affecting every RVTuk window.
> Mockups were built interactively with the brainstorming visual companion; final approved
> state lives in `RVTuk/.superpowers/brainstorm/2032-1783240487/content/interactive-prototype-v2.html`.

## Problem / motivation

The current window (480×600, toolbar-driven pane switch between Config and the Areas tree)
has four usability problems raised in review:

1. **Config vs Areas pane switching** — the toolbar implicitly swaps the whole bottom pane;
   it isn't obvious these are two separate "screens" or how to get back.
2. **Spotting/understanding errors** — flagged area rows are just red bold text with a
   hover-only tooltip; nothing surfaces at a glance.
3. **Area tree readability** — number, name, and usage code are crammed into one dense line;
   level headers are plain text.
4. **Config form layout** — Building No / Asset / Scale / marker format / output path are one
   flat stacked form with no grouping or priority, and the "Setup Usage Keys" toolbar button
   (a rare, one-time action) sits next to Refresh/Export (used every session).

Separately, the addin's dark theme (`DarkTheme.xaml`) uses a VSCode-inspired orange/near-black
palette that doesn't match the host application. Since RVTuk is a Revit add-in, its dialogs
should read as part of Revit, not a separate tool bolted on.

## Goals
- Replace implicit pane-switching with a standard, recognizable affordance (tabs).
- Make invalid/incomplete state visible without opening a tooltip or scanning a dense list.
- Group the Settings fields by concern; give the rare "Usage Keys" action its own home.
- Re-theme the whole add-in (all windows) to read as native Revit chrome instead of a
  VSCode-style tool.

## Non-goals
- No change to `AreaRecord`, `AreaValidator`, `DxfWriter`, `DatWriter`, or the Revit-side
  extraction/selection/export logic — this is a view + view-model presentation change only.
- No change to the underlying `AreaSubmissionConfig` fields or their meaning.
- No redesign of any other window's *layout* (Comparator, FamilyBrowser, Settings, Config,
  InstructionsEditor, IndexProgress) — only their theme *colors* change, via the shared
  `DarkTheme.xaml` brush values.
- No functional change to Export/Refresh/Setup Usage Keys commands themselves.

## Design

### Component 1 — `DarkTheme.xaml` palette (project: UI, global)
Brush values only; no structural/style-template changes beyond what's listed in Component 3.

| Brush | Current | New |
|---|---|---|
| `Brush.Bg` | `#1E1E1E` | `#2B2B2B` |
| `Brush.Panel` | `#252526` | `#333333` |
| `Brush.Control` | `#2D2D2D` | `#3C3C3C` |
| `Brush.Input` | `#3C3C3C` | `#232323` |
| `Brush.Border` | `#3F3F46` | `#4A4A4A` |
| `Brush.BorderFocus` | `#007ACC` | `#4A90D9` |
| `Brush.Accent` | `#FF8C00` | `#4A90D9` |
| `Brush.AccentDark` | `#CC7000` | `#2B6CB0` |
| `Brush.Text` | `#D4D4D4` | `#E3E3E3` |
| `Brush.TextMuted` | `#858585` | `#9A9A9A` |
| `Brush.Warning`/error tint | `#FF6B35` | `#D64545` border, `#3A2424` fill |

`Brush.Success` (`#4EC94E`), `Brush.Selection` (`#264F78`), `Brush.TextDisabled` (`#656565`),
and `Brush.Splitter` are unchanged. Every existing `Style` in `DarkTheme.xaml` (Button,
TextBox, ComboBox, DataGrid, TabControl/TabItem, …) already references these keys by
`StaticResource`, so the recolor is a values-only edit — no template changes, no window XAML
touched for this part.

### Component 2 — new `GroupHeaderStyle` (project: UI, `DarkTheme.xaml`)
A new reusable style for the Settings tab's grouped panels, visually matching Revit's
Properties-palette section bars (header bar + decorative chevron, body below):
- A `Border`+`TextBlock` header bar (`Brush.Panel`-toned background, `Brush.Border` 1px
  bottom border, small bold uppercase-ish label, static "▾" glyph — **not** an interactive
  `Expander**; groups are always expanded, the chevron is decorative to match the reference).
- A body `Border` below using `Brush.Bg`-toned background for the fields.
- Exposed as a `x:Key="GroupHeaderPanel"` (or similar) `Style`/`ControlTemplate` fragment so
  `AreaSubmissionWindow.xaml` can wrap each Settings group in it without repeating markup
  three times — a `HeaderedContentControl` with this template is the natural WPF fit.

### Component 3 — `AreaSubmissionWindow.xaml` (project: UI)
- Replace the `IsConfigPane`/`IsAreasPane`-`Visibility`-toggled `StackPanel`/`TreeView` pair
  with a `TabControl` (reusing the theme's existing `TabControl`/`TabItem` style) with two
  `TabItem`s: **Settings** and **Areas**.
  - Each `TabItem` header carries a small warning badge: Settings shows `!` when
    `HasInvalidSettings` is true; Areas shows the flagged-row count when > 0. Both bind to
    the view model (Component 4) and hide entirely when there's nothing to flag.
- Toolbar becomes a fixed row (`Grid.Row`, sibling to the `TabControl`, not inside it) holding
  only **Refresh** and **Export** — visible regardless of the active tab.
- **Settings tab** content: three `GroupHeaderPanel`-wrapped sections —
  - **Submission** — Building number, Asset, Scale.
  - **Markers & Output** — marker format radios (existing tooltips kept, no visual change
    beyond re-theming), output file base name, output folder + Browse.
  - **Project setup** — the relocated "Setup Usage Keys" button plus its explanatory text
    shown inline (not a tooltip).
  - Each of Building number / Scale / Output file base name / Output folder gets an
    `invalid`-state trigger: red border (`#D64545`) + red label text when that field fails
    validation (Component 4). No banner/summary text on this tab — the per-field highlight
    plus the tab badge are the only indicators.
- **Areas tab** content: unchanged tree structure and behavior (level grouping, click-to-select
  in model), restyled:
  - A summary banner at the top when `flagged > 0`: "*N* of *M* areas flagged — check before
    exporting" (same visual language as the old error styling, recolored to the new red tint).
  - Level headers show a row count, e.g. "Level 01 (6)", plus a small ⚠ if any row in that
    group is flagged.
  - Each row renders as `Number — Name` on the left and a small rounded "tag" on the right
    showing the usage code (or "⚠ no code"); flagged rows get a subtle red-tinted row
    background (replacing today's red-bold-text-only treatment) in addition to the tag color.

### Component 4 — `AreaSubmissionViewModel` (project: UI)
- New computed validation surface, recomputed whenever a relevant `Config` field changes:
  - `BuildingNoInvalid` — `Config.BuildingNo <= 0`.
  - `ScaleInvalid` — `Config.Scale <= 0`.
  - `FileBaseNameInvalid` — `string.IsNullOrWhiteSpace(Config.FileBaseName)`.
  - `OutputFolderInvalid` — `string.IsNullOrWhiteSpace(Config.OutputFolder)`.
  - `HasInvalidSettings` — true if any of the above is true; drives the Settings tab badge.
- `AreaSubmissionConfig` (Core) is a plain POCO with no property-change notification today.
  Since `BuildingNo`/`Scale`/`FileBaseName` currently have no notifying wrapper (unlike
  `OutputFolder`, which already routes through the VM's own `OutputFolder` property), add
  thin notifying wrapper properties on the VM for `BuildingNo`, `Scale`, and `FileBaseName`
  the same way `OutputFolder` already works today — bind the XAML `TextBox`es to these VM
  properties (not directly to `Config.*`), each setter calling `OnPropertyChanged` for
  itself plus the relevant `*Invalid`/`HasInvalidSettings` properties.
- `Levels`/`AreaLevelGroupViewModel`/`AreaRowViewModel` are unchanged in data shape; only
  their `DataTemplate` in the window XAML changes for the new row/tag visuals. A new
  read-only `FlaggedCount` on the VM (or computed inline in XAML via a converter) drives the
  Areas tab badge and the summary banner text.
- `SubmissionPane` enum / `CurrentPane` can be dropped in favor of the `TabControl`'s own
  `SelectedIndex`/`SelectedItem`, **or** kept and two-way bound to `TabControl.SelectedIndex`
  if `Refresh()`'s existing `CurrentPane = SubmissionPane.Areas` behavior (jump to the Areas
  tab after a successful read) is easiest to preserve that way. Recommendation: keep it —
  smallest diff, and `Refresh()`'s tab-switch-on-load behavior stays intentional and explicit
  rather than implicit in a converter.

## Data flow
Unchanged from the original feature design — this redesign only touches presentation:
```
Ribbon "Area Calc" → AreaSubmissionWindow
  Settings tab → fill AreaSubmissionConfig (via VM wrapper properties; invalid fields highlight red)
  Refresh  → ExternalEvent: AreaExtractor(open sheet) → Area tree, jump to Areas tab
  (row click) → ExternalEvent: SelectAreaHandler
  Export   → AreaValidator → DxfWriter + DatWriter → files → success / error dialog
             (still blocked the same way today if Config is incomplete or areas are unread —
             the Settings tab badge / field highlighting are additive visibility, not a new
             validation path; the export-time check in AreaValidator is unchanged)
  Setup Usage Keys (now on Settings tab) → ExternalEvent: SetupRishuiZaminParams
```

## Threading
No change — extraction, selection, and Usage Keys setup already run via the existing
`ThreadPool` → `ExternalEvent` → `Dispatcher.Invoke` pattern; the redesign doesn't add new
background work.

## Persistence
No change — `OutputFolder` continues to round-trip through `ConfigManager`/`AppConfig` as
today.

## Error handling
- Field-level invalid styling is presentation only; it does not change what `AreaValidator`
  treats as a hard export blocker (already covers missing `BuildingNo`/`OutputFolder`/`Scale`
  per the original spec) — the VM's `*Invalid` flags should mirror the same conditions
  `AreaValidator` already checks, not introduce new rules, so the UI and the export-time
  check never disagree about what counts as "invalid."
- No change to how extraction/export failures are surfaced (still the existing
  `ExportCompleted` event → `MessageBox`).

## Testing
- **Unit (Core):** none needed — no Core logic changes.
- **Manual in-Revit:**
  - Every other RVTuk window (Comparator, FamilyBrowser, Settings, Config,
    InstructionsEditor, IndexProgress) still renders correctly with the new palette — no
    hardcoded colors elsewhere in those XAML files that would clash (spot-check during
    implementation).
  - Area Calc: Settings tab shows red field highlighting + tab badge when Output folder or
    File base name is cleared, or Building number/Scale is set to 0; badge clears when fixed.
  - Refresh loads areas and switches to the Areas tab; flagged rows show the red tint + tag;
    summary banner count matches; clicking a row selects the Area in the model.
  - Export still blocked with the existing message when Config is incomplete or no areas are
    loaded (behavior unchanged, only surfaced differently beforehand).

## Open questions
- Whether `GroupHeaderPanel` is worth generalizing now for reuse in other windows (e.g.
  Settings/Config windows have similar flat forms) or scoped to just this window for now —
  recommend scoping to this window only; generalize later if a second window wants it.
