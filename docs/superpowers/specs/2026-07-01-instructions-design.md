# In-Revit Instructions — Design (DRAFT skeleton — fill in)

**Date:** 2026-07-01
**Status:** Draft (skeleton for you to complete)
**Area:** Instructions
**Branch:** TBD

> Editable, in-Revit visual pages with tips and how-to guidance. Note: the Family Browser
> already has a per-family rich-text instructions editor (RichTextBox + gallery) — reuse those
> patterns where possible.

## Problem / motivation
Architects need firm guidance and how-tos *inside* Revit, not in a separate doc. We want
editable pages with text + images that anyone can read, and authorized users can edit.

## Goals
- [ ] Browsable set of instruction pages (list/tree + content view).
- [ ] Rich text + images (screenshots, diagrams).
- [ ] Editable in-app; content stored in the shared DB (or alongside it).
- [ ] (Maybe) link a page to a specific tool/button for contextual help.

## Open design questions
- **Authoring vs read-only:** who can edit — everyone, or a "BIM manager" mode? How gated?
- **Storage:** new table in the shared `RVTuk.db` (pages: id, title, order, xaml/markdown,
  images)? Or markdown files in the library `.Setup` folder? Reuse the family
  instructions' XAML approach and gallery-image storage?
- **Format:** reuse the existing RichTextBox XAML pipeline, or switch to Markdown?
- **Navigation:** flat list, categories, or a tree? Search?
- **Versioning / concurrency:** multiple editors on a network share — last-write-wins, or lock?
- **Contextual help:** should ribbon buttons deep-link to a relevant page?

## Design
### Component 1 — Pages model + repository (RVTuk.Core)
<DTOs + SQLite table(s); CRUD; image storage like the family gallery. No Revit/WPF.>

### Component 2 — UI (RVTuk.UI)
<Reader window (nav + content) and an editor (reuse InstructionsEditor patterns / gallery).
Dark theme. Read-only vs edit mode.>

### Component 3 — Revit entry (RVTuk.Revit)
<Ribbon button to open the Instructions window. Minimal — mostly hosts the UI.>

## Persistence
<Define the schema here. Keep SQL portable; dates UTC ISO-8601; images as files under
`.Setup` or BLOBs, matching the family gallery's choice.>

## Threading
Image decode off the UI thread (the family gallery already does this); DB on ThreadPool.

## Error handling
Read-only fallback on a locked/read-only share; never lose edits silently.

## Testing
- **Unit (Core):** page CRUD, ordering, image linkage.
- **Manual in-Revit:** open, read, edit (if allowed), images render, persists across sessions.

## Open questions
- <add as they arise>
