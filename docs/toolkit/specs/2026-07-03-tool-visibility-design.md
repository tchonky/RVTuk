# Design: Tool visibility toggles (modular ribbon)

**Status:** approved design, saved for later — not yet implemented.

## Goal

Let each user choose which RVTuk tools appear on the Revit ribbon. Everything still
installs as one add-in; visibility is a per-user setting toggled in the Config window,
applied immediately (no Revit restart).

## Decisions made

- **Modularity level:** show/hide via settings (not install-time selection, not a plugin architecture).
- **Scope:** per-user, stored in the local RVTuk config (`%AppData%\Autodesk\Revit\Addins\RVTuk\config.json`).
- **Apply timing:** immediately, by flipping `RibbonItem.Visible` live.

## Design

### 1. Config model (`RVTuk.Core`)

Add `List<string> HiddenTools` to `AppConfig` (empty = everything visible, so existing
configs need no migration). Values are stable tool IDs matching the ribbon button names:
`"BrowseLibrary"`, `"CompareProjects"`, `"AreaCalc"`.

**`Config` is not toggleable** — otherwise a user could lock themselves out of the
settings that un-hide tools.

### 2. Ribbon (`RVTuk.Revit/Application.cs`)

`CreateRibbon` still creates all four buttons (so live-toggle works), but keeps the
returned `PushButton` references in a static `Dictionary<string, RibbonItem>` and sets
`Visible = false` on those listed in `HiddenTools` at startup.

A new static method `Application.ApplyToolVisibility(AppConfig)` flips `Visible` live;
the Config window calls it after saving.

### 3. Config window (`RVTuk.UI`)

A new "Tools" section with three checkboxes (Family Browser, Project Comparator,
Area Calc). Since UI must not reference Revit, the window receives an
`Action<AppConfig>`-style delegate (same pattern as the existing Revit delegates)
that `RVTuk.Revit` wires to `ApplyToolVisibility`. Ticking/unticking saves config
and applies immediately.

## Error handling

Unknown tool IDs in `HiddenTools` are ignored.

## Testing

Build both configs (`Release2024`, `Release2025`); manual check in Revit that toggles
hide/show live and persist across restarts.
