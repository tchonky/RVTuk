# Neo Properties — Design

**Date:** 2026-07-04
**Status:** Approved
**Area:** Productivity
**Branch:** TBD (create from main when implementation starts)

## Problem / motivation

The native Revit Properties palette shows parameters in Revit's fixed group order, with no
way to pin frequently-checked parameters to the top. For day-to-day work, the BIM lead wants
a companion dockable pane that shows the same information — read-only — but reordered:
important parameters pinned first, then the remaining groups in a chosen order. This spec
covers a minimal first test of the mechanism (dockable pane + live selection sync), not a
finished feature.

## Goals
- A dockable pane, "Neo Properties," that behaves like the native Properties palette
  (dock/float/tab alongside it), toggled from a ribbon button.
- Shows parameters for the currently selected element, live-updating on selection change.
- Parameters appear pinned-first (hardcoded placeholder list), then remaining built-in
  parameter groups in a fixed custom order — proving the reordering mechanism works.
- Read-only.

## Non-goals
- Editing parameter values.
- Multi-select aggregation (shows a placeholder message instead).
- Per-category custom ordering.
- Config-file-driven or user-editable ordering (hardcoded for this test).
- Persistence of pane state beyond what Revit/WPF give for free.

## Design

### Component 1 — NeoPropertiesPaneProvider (project: Revit)
Implements `IDockablePaneProvider`. Registered in `Application.OnStartup` with a new
`DockablePaneId` (own GUID, distinct from Revit's built-in Properties pane). Supplies a
`NeoPropertiesView` (WPF `UserControl` from `RVTuk.UI`) as pane content via
`DockablePaneProviderData`.

### Component 2 — NeoPropertiesCommand (project: Revit)
Ribbon button (`IExternalCommand`) in a new ribbon panel on the RVTuk tab. Toggles pane
visibility via `DockablePane.Show()` (Revit has no built-in toggle-visibility API beyond
show; button simply shows/activates the pane).

### Component 3 — Selection sync handler (project: Revit)
Subscribes to `UIApplication.SelectionChanged` in `OnStartup`. On each event:
- Reads `UIDocument.Selection.GetElementIds()`.
- If count == 0 or > 1: pushes a placeholder state ("No element selected" /
  "Select a single element") to the view-model.
- If count == 1: reads the element's `Parameters` directly (cheap, sub-ms per element —
  same cost the native Properties palette pays), builds an ordered list of
  `(GroupName, ParamName, ValueString)` using the pinned-list + custom-group-order logic
  below, and pushes it to the view-model.

Runs entirely on the Revit UI thread (selection-changed already has API access) — no
`ExternalEvent`, no background thread needed for this read-only, parameters-only case.

### Component 4 — Ordering logic (project: Revit, colocated with the selection handler)
A small hardcoded lookup:
- **Pinned parameter names** (placeholder set, order matters): `Mark`, `Comments`,
  `Family and Type`. Any of these present on the element are shown first, in this order,
  regardless of their native group.
- **Remaining parameters**: grouped by their native `BuiltInParameterGroup`, groups emitted
  in a fixed custom order (placeholder: reasonable default resembling Revit's own grouping,
  e.g. Identity Data, Constraints, Dimensions, everything else, Other). Parameters within
  a group keep Revit's own order.
- Pure function: `IList<Parameter> -> IReadOnlyList<(string Group, string Name, string Value)>`,
  easy to unit-test without a live Revit session (can be exercised by faking the small
  ordering step; the `Parameter` reads themselves need a real Element so are exercised
  manually).

### Component 5 — NeoPropertiesView / NeoPropertiesViewModel (project: UI)
Plain WPF `UserControl` + view-model, no Revit types — takes the ordered
`(Group, Name, Value)` list (or placeholder text) and renders grouped headers with
parameter rows underneath, dark theme matching existing RVTuk panels.

## Data flow

```
Ribbon button (NeoPropertiesCommand) → DockablePane.Show()

UIApplication.SelectionChanged → selection handler (Revit project)
  → 0 or 2+ selected: placeholder state
  → 1 selected: read Parameters → apply pinned/group ordering
  → push ordered list into NeoPropertiesViewModel → WPF binding redraws
```

## Threading

Everything here runs on the Revit UI thread. `SelectionChanged` fires on the main thread
with full API access, and reading parameters off one element is cheap enough (low
single-digit ms) that no `ExternalEvent`/`ThreadPool` hop is needed for this v1. If a later
iteration adds cross-referencing RVTuk's own DB (e.g. family metadata) or multi-select
aggregation, that heavier work should move to the existing background-thread +
`Dispatcher` pattern — not this handler.

## Persistence (if any)

None. Pinned list and group order are hardcoded constants in this version.

## Error handling

- Selection handler must not throw: wrap parameter reads in try/catch per-parameter (a
  malformed or inaccessible parameter shouldn't blank the whole pane) and log/skip.
- Pane must never block or delay Revit's own selection handling — keep the handler fast
  and synchronous-only for cheap reads, per the Threading section.
- No writes to the model anywhere in this feature, so no risk of destabilizing it.

## Testing

- **Unit (Core or Revit, whichever hosts the pure ordering function):** given a fake list
  of `(GroupName, ParamName)` pairs, verify pinned params appear first in the specified
  order, and remaining groups appear in the fixed custom order with intra-group order
  preserved.
- **Manual in-Revit:**
  1. Open the pane via the new ribbon button; confirm it docks/floats/tabs like Properties.
  2. Select no element → placeholder message.
  3. Select one element with some of the pinned parameters → confirm those appear first,
     in the specified order, followed by remaining groups in the custom order.
  4. Select an element missing some pinned parameters → confirm it skips gracefully (no
     blank rows, no crash).
  5. Select multiple elements → placeholder message, no stale data from prior selection.
  6. Rapidly change selection several times → confirm no lag or visible stutter.

## Open questions
- None blocking — pinned list and group order are explicitly placeholders to be swapped
  once the mechanism is validated.
