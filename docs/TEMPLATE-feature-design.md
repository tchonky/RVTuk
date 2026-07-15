# <Feature name> — Design

**Date:** YYYY-MM-DD
**Status:** Draft | Approved
**Area:** <Family Explorer | Comparator | Productivity | Instructions | …>
**Branch:** <feature-branch>

> Copy this file to `YYYY-MM-DD-<topic>-design.md` and fill it in. Keep each section as short
> as the topic allows. Delete bracketed prompts as you go.

## Problem / motivation
<What hurts today? Who feels it (BIM lead / BIM manager / project architect)? Why now?>

## Goals
- <Concrete, testable outcome 1>
- <Outcome 2>

## Non-goals
- <Explicitly out of scope, so it doesn't creep in>

## Design

### Component 1 — <name> (project: Core | UI | Revit)
<What it does, its inputs/outputs, where it lives. Remember the dependency rule:
Core has no Revit/WPF; UI has no Revit; only RVTuk.Revit touches the Revit API, marshalled
through ExternalEvent + ManualResetEventSlim.>

### Component 2 — …

## Data flow
<Ribbon button / trigger → command → window/VM → background work → Revit ping-pong → DB.
A short arrow diagram is enough.>

## Threading
<Which work runs on the Revit main thread (ExternalEvent) vs the ThreadPool; Dispatcher use.>

## Persistence (if any)
<New SQLite tables/columns? Keep SQL portable (Microsoft.Data.Sqlite, one statement per
ExecuteScalar). Dates as UTC ISO-8601; relative paths via PathUtil.GetRelativePath.>

## Error handling
<What can fail, and what the user sees. Never destabilise the open model.>

## Testing
- **Unit (Core):** <pure logic to cover>
- **Manual in-Revit:** <steps to verify the Revit/UI wiring>

## Open questions
- <Decisions still needed before/while building>
