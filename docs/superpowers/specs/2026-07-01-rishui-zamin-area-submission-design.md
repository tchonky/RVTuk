# Rishui Zamin Area Submission ("Area Calc") — Design

**Date:** 2026-07-01
**Status:** Approved (design)
**Area:** Productivity tools (new)
**Branch:** TBD

> Generates the geometry + data files for Israel's רישוי זמין (Rishui Zamin) area calculation
> (חישוב שטחים ואחוזי בנייה). Background, decoded file formats, the official RZ_* schema, and the
> usage-code taxonomy live in [`docs/autoarea/rishui-zamin-notes.md`](../../autoarea/rishui-zamin-notes.md);
> reference samples are in `tests/Examples Autoarea/` (esp. the ASCII `Garmoshka.dxf`). Inspired
> by TekenPlus but built for the **Israeli** system (not Dutch NEN 2580).

## Problem / motivation

Preparing the Rishui Zamin area submission by hand — drawing area polygons on the exact `RZ_*`
layers, tagging each with the right usage code, and producing the file package — is slow and
error-prone, and a bad file is rejected by the national "robot". We want a one-window tool that
reads the areas already modelled in Revit and emits the submission files, catching errors first.

## Scope

- **In:** generate the **`.dxf`** (area geometry + tags) and **`.dat`** for the **open sheet**;
  a pre-flight error check; a one-time Setup command for the key schedule.
- **Out (v1):** the **`.dwfx`** (the user exports the plotted sheets from Revit natively —
  see Q6); georeferencing / ITM anchor (Q4); multi-building / per-unit ASSET (Q5); the
  separate Tel-Aviv / Jerusalem robots; multi-sheet batch (repeat per open sheet for now).

The national robot *computes and colours* the areas from the geometry we submit, so the tool's
job is to produce correctly-structured geometry + usage codes, not the final area table.

## Decisions (from brainstorming)

1. **Area source:** Revit **Areas** (Area Plan views), not Rooms — their boundaries can follow
   the gross/external face required by חישוב שטחים.
2. **Function → code:** a shipped shared parameter **`RZ_UsageType`** on Areas, set through an
   Area **key schedule pre-filled with the official functions + codes**; the tool reads the code.
3. **Page source:** the **currently open sheet** (one is normal). A sheet may host several Area
   Plan views of different levels → `FLOOR` from each area's level; `PAGE_NO` = the sheet's page.
4. **Coordinates:** plain model/plan coordinates; **no** geo-anchor.
5. **Building/asset:** single building; `BUILDING_NO` a project field (default 1); `ASSET`
   blank or one project-level value.
6. **Files:** tool makes **DXF + DAT**; user exports the DWFX manually.
7. **Workflow:** one window, top toolbar (Config / Refresh / Export), swappable bottom pane
   (see UI below).

## Design

### Ribbon
New **"Area Calc"** button on the RVTuk panel → opens `AreaSubmissionWindow`.

### RVTuk.Core (pure business logic — no Revit/WPF; unit-tested)
- **`UsageCatalog`** — the official usage taxonomy as data: `{ Code, Kind (Primary/Service/Other),
  Name }` for primary 1–33, service 101–130, other 250–257. Consumed by the key-schedule Setup
  and by validation.
- **Models:**
  - `AreaSubmissionConfig` — `BuildingNo` (default 1), `Asset` (optional), `Scale` (default
    1:100), `OutputFolder`, `FileBaseName`.
  - `AreaRecord` — `Level`, `Number`, `Name`, `UsageCode`, `Floor`, `PageNo`, `IsUnderground`,
    `AreaValue`, `BoundaryLoops` (list of XY point loops), `Errors` (flags).
- **`AreaValidator`** — pure rules over `AreaRecord`s + config:
  - **Errors (block export):** area with no `UsageCode`; boundary not closed / self-intersecting
    / zero-area; missing required config (`BuildingNo`, `OutputFolder`, `Scale`).
  - **Warnings (don't block):** area with no `Number` or `Name`.
- **`DxfWriter`** — `List<AreaRecord>` + config → the ASCII DXF text, reproducing the documented
  structure of `Garmoshka.dxf`: layers `RZ_FRAME` / `RZ_FLOOR` / `RZ_AREA`, closed `LWPOLYLINE`
  loops, and an `RZ_AREA_SYM` block `INSERT` per area whose `ATTRIB`s carry (group 2 = tag,
  group 1 = value): `USAGE_TYPE`, `USAGE_TYPE_OLD`, `AREA`, `ASSET`, `PAGE_NO`, `BUILDING_NO`,
  `FLOOR`, `LEVEL_ELEVATION`, `IS_UNDERGROUND`. (`RZ_FRAME`/`RZ_FLOOR` geometry per level derived
  as in *Open items*.)
- **`DatWriter`** — emits `DWFX_SCALE\t{scaleValue}\n` exactly (bare `\n`).

### RVTuk.Revit (Revit API — main thread via ExternalEvent)
- **`AreaExtractor`** — for the open sheet, find its placed Area Plan views; for each Area read
  boundary segments (`Area.GetBoundarySegments` → XY loops), `Area.Area` value, its `Level`, and
  `RZ_UsageType`; derive `Floor` (level), `PageNo` (sheet), `IsUnderground` (level below ground —
  see Open items). Returns Core `AreaRecord`s. Read-only.
- **`SelectAreaHandler`** — selects an Area element in the model when its row is clicked.
- **`ExportAreaSubmission`** — runs `AreaValidator`; if clean, `DxfWriter` + `DatWriter` write
  `<FileBaseName>.dxf` / `.dat` to `OutputFolder`.
- **`SetupRishuiZaminParams`** — one transaction: bind the `RZ_UsageType` shared parameter to the
  Areas category and create the Area **key schedule** pre-filled from `UsageCatalog`. Ships a
  shared-parameter file (`RZ_AreaParams.txt`).

### RVTuk.UI (WPF, dark theme; Revit passed in as `Func`/`Action` delegates — no Revit types)
- **`AreaSubmissionWindow` + `AreaSubmissionViewModel`:**
  - Top toolbar: **Config**, **Refresh**, **Export**.
  - **Config** → bottom pane shows the `AreaSubmissionConfig` fields.
  - **Refresh** → invokes the extract delegate, runs `AreaValidator`, shows the **Area tree**:
    grouped by **level** (expand/collapse), sorted by **Number**; error rows flagged red / with
    an icon; selecting a row invokes the select-in-model delegate.
  - **Export** → validate; clean → write files → "Export successful" dialog; otherwise a dialog
    listing the errors (missing info / unfilled Config).

### Data flow
```
Ribbon "Area Calc" → AreaSubmissionWindow
  Config  → fill AreaSubmissionConfig
  Refresh → ExternalEvent: AreaExtractor(open sheet) → AreaValidator → Area tree (flags)
  (row click) → ExternalEvent: SelectAreaHandler
  Export  → AreaValidator → DxfWriter + DatWriter → files → success / error dialog
Setup (once) → ExternalEvent: SetupRishuiZaminParams (bind param + build key schedule)
```

## Threading / safety
Model reads, selection, and Setup run on Revit's main thread via `ExternalEvent` (the existing
ping-pong). DXF/DAT generation is pure Core. The tool is **read-only to the model** except the
one-time Setup (its own transaction). Selecting an area is a UI selection, not a transaction.

## Persistence
`BuildingNo`/`Asset`/`Scale` are per-project → entered per session (not stored globally). The
last-used `OutputFolder` may be remembered in `AppConfig` for convenience.

## Error handling
- Extraction tolerates areas it can't read (reports them as errors rather than aborting).
- Export surfaces folder/permission failures in the result dialog.
- Never modifies the model outside Setup.

## Testing
- **Unit (Core):** `DxfWriter` output diffed against a fragment of `Garmoshka.dxf`; `DatWriter`
  exact bytes (`DWFX_SCALE\t10\n`); `AreaValidator` for each error/warning rule; `UsageCatalog`.
- **Manual in-Revit:** Setup builds the key schedule; open a sheet; Refresh lists areas grouped
  by level with errors flagged; clicking a row selects the Area; Export writes DXF+DAT that the
  robot accepts (the real acceptance test).

## Open items — pin from the sample during implementation (not guesses)
- DXF coordinate **units** (m vs mm) and exact `LWPOLYLINE`/`INSERT`/`ATTRIB` formatting —
  matched byte-for-byte against `Garmoshka.dxf`.
- `RZ_FRAME` / `RZ_FLOOR` geometry derivation (frame bounding rectangle per floor; floor outline
  from the area union or a level boundary) — matched to the sample.
- `IS_UNDERGROUND` rule (level elevation below ground vs a parameter).
- `USAGE_TYPE_OLD` value for new construction (mirror `USAGE_TYPE`, or blank).
- The `DWFX_SCALE` value for non-1:100 jobs (sample = `10` for 1:100).
