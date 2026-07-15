# Auto-Dimensioning — Design (DRAFT skeleton — fill in)

**Date:** 2026-07-01
**Status:** Draft (skeleton for you to complete)
**Area:** Productivity tools
**Branch:** TBD

> This folds in the existing standalone **`KKimensions` / DimensionPropagator** project rather
> than reinventing it (see VISION.md). First task is to decide *how much* to absorb.

## Problem / motivation
Creating and maintaining clear dimensions on defined views is repetitive. We want to create
and **regenerate** dimensions on demand, reusing the existing KKimensions logic inside RVTuk
(one ribbon, one install).

## Absorb-vs-rewrite decision (do this first)
- [ ] Locate the `KKimensions` / DimensionPropagator source; list what it does well.
- [ ] Decide: lift its core algorithm into `RVTuk.Core`/`RVTuk.Revit`, or reference it?
- [ ] What are its current limits / bugs we'd want to fix while absorbing?

## Goals
- [ ] Generate dimensions on a defined set of views following firm rules.
- [ ] Regenerate/refresh on demand (idempotent — re-running doesn't pile up duplicates).
- [ ] Define which elements get dimensioned (grids, walls, openings, …) per view type.

## Open design questions
- **Scope per run:** active view? a saved view set? selected elements?
- **Dimension rules:** what gets dimensioned, to what references, in how many chains
  (overall / intermediate / openings)?
- **Idempotency:** how do we recognize "our" dimensions to replace them on regenerate
  (a marker comment/parameter? owned by a workset? naming)?
- **Placement:** offset distances, stacking order, witness-line trimming — configurable?
- **What carries over from KKimensions** vs. redesigned?

## Design
### Component 1 — Dimension rule model + placement math (RVTuk.Core)
<Pure geometry/rules where possible (reference selection, chain layout, offsets) — unit-testable.>

### Component 2 — Revit create/regenerate (RVTuk.Revit)
<ExternalEvent: read references, create Dimension elements in one Transaction; on regenerate,
delete previously-owned dimensions first. Mark them so we can find them next time.>

### Component 3 — UI (RVTuk.UI)
<View-set selection, rule options, Run / Regenerate buttons. Dark theme.>

## Threading
All Revit reads/writes on the main thread via ExternalEvent; layout math in Core.

## Error handling
One transaction per run; skip views/elements that can't be dimensioned with a report rather
than aborting. Never delete dimensions we didn't create.

## Testing
- **Unit (Core):** reference selection + chain/offset math; "owned dimension" detection.
- **Manual in-Revit:** generate, tweak model, regenerate → clean replace, no duplicates, undo.

## Open questions
- <add as they arise>
