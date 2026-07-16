# Native-dialog hand-off (naming rules / sets / DWG setups) — design

**Date:** 2026-07-16 · **Status:** approved (chat, 2026-07-16)

## Problem

The backlog asked for in-tool editors for naming rules, view/sheet sets, and DWG export
setups. Rebuilding those native dialogs is large and drift-prone. Revit lets add-ins
post native commands, and reflection over the API confirms all needed dialogs are
officially postable in both 2024 and 2025 (`PostableCommand.ExportPDF`,
`PostableCommand.ExportCADFormatsDWG`, `PostableCommand.ExportOptionsExportSetupsDWGOrDXF`).

## Decision

Replace all three "editor" backlog items with **hand-off buttons**:

| Button (next to) | Posts | User edits there |
|---|---|---|
| View/sheet set dropdown | `PublishSettings`, falling back to `ExportPDF` | sets (Publish Settings is a dedicated set manager; the PDF dialog's pencil is the fallback when Revit greys it out) |
| File Naming dropdown | `ExportPDF` | naming rules (the pencil — the only place Revit exposes the rule editor) |
| DWG Export Setup dropdown | `ExportOptionsExportSetupsDWGOrDXF` | full "Modify DWG/DXF Export Setup" dialog |

*(Amended 2026-07-16 after user feedback: the set button targets Publish Settings first.)*

Mechanics: a posted command runs only after the current API context ends, so the button
**closes our modal window first**; the native dialog opens; the user edits and closes
it; reopening DWG Export re-reads setups/sets and restores all last-used selections
(already persisted). Tooltips state this flow explicitly.

Failure path: if the command id can't be resolved or can't be posted right now
(`CanPostCommand` false / `PostCommand` throws), the window stays open and shows a
message; nothing else changes. The custom in-tool editors remain the fallback backlog
idea only if this proves unusable in practice.

## Changes

- UI: three small "Edit…" buttons; `DwgExportViewModel.OpenNativeDialog :
  Func<string,bool>?` ("pdf" | "dwgsetups"), wired by the command like the
  window-side callbacks; window closes on `true`.
- Revit: the command wires the delegate via `RevitCommandId.LookupPostableCommandId` +
  `CanPostCommand` + `PostCommand` in try/catch.
- No Core changes; no new persisted state.

## Testing

No Core surface — build all three configs; in Revit: each button closes the window and
opens the right dialog; edits show up on reopen; selections survive the round trip.
