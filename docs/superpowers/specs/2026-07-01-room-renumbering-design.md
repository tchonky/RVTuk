# Room Renumbering — Design (DRAFT skeleton — fill in)

**Date:** 2026-07-01
**Status:** Draft (skeleton for you to complete)
**Area:** Productivity tools
**Branch:** TBD

> Seeded skeleton — answer the prompts, delete what doesn't apply.

## Problem / motivation
Renumbering rooms by hand is slow and error-prone; existing scripts are rigid (fixed order).
We want an **interactive** renumber that works by level / by category / by any parameter.

## Goals
- [ ] Renumber selected (or all) rooms following a chosen order.
- [ ] Order strategies: by level; spatially (by location); by an existing parameter value.
- [ ] Configurable start number, increment, and prefix/suffix (e.g. level code).
- [ ] Preview before committing; single undoable transaction.

## Open design questions (decide these first)
- **Selection scope:** active view? whole level? user pick? whole model?
- **Ordering:** for "spatial" order, what rule — left→right then top→bottom? snake? by proximity?
  Do we need a click-to-order mode (user clicks rooms in sequence)?
- **Numbering scheme:** pure integer? `<LevelCode>-<NNN>`? zero-padding width?
- **Collisions:** if a target number already exists on another room, what happens
  (two-pass via temp numbers? skip? error)?
- **Which parameter:** the built-in `Number`, or also `Name`, or any project parameter?
- **Cross-phase / linked rooms:** in scope?

## Design
### Component 1 — Core ordering/numbering logic (RVTuk.Core, no Revit types)
<Pure function: given a list of room DTOs (id, level, x/y, current params) + options →
ordered list of (roomId → new number). Unit-testable without Revit.>

### Component 2 — Revit gather + apply (RVTuk.Revit)
<ExternalEvent handler: read rooms into DTOs on the main thread; after preview/confirm,
write new numbers in one Transaction. Two-pass if needed to avoid duplicate-number errors.>

### Component 3 — UI (RVTuk.UI)
<Dialog: scope, order strategy, start/increment/prefix, live preview table (old → new),
Apply/Cancel. Dark theme.>

## Threading
Gather + apply on Revit main thread via ExternalEvent; ordering logic is pure Core.

## Error handling
Validate the scheme; detect/resolve number collisions; wrap the write in one transaction so
a failure rolls back cleanly. Never partially renumber.

## Testing
- **Unit (Core):** ordering strategies; collision resolution; prefix/padding formatting.
- **Manual in-Revit:** preview matches result; undo restores; collisions handled.

## Open questions
- <add as they arise>
