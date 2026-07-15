# Rishui Zamin (ribbon button: "Area Calc")

**What it is:** reads the Areas on the open sheet and exports the paired `.dxf` + `.dat`
files the רישוי זמין (Rishui Zamin) area-calculation robot expects (`RZ_FRAME` /
`RZ_FLOOR` / `RZ_AREA` layers + attribute blocks). The window's "Setup Usage Keys"
action binds the `RZ_*` shared parameters to Areas and creates/tops-up the usage-key
schedules — no separate ribbon command.

**Status:** shipped (v1 launch surface). Also shipped to Revit 2023 as **KKarea**, a
standalone one-button add-in hosting only this tool (RVTuk itself was dropped from 2023).

**Names:** code `RishuiZamin`; ribbon button displays "Area Calc" (internal id
`AreaCalc`). Older code/docs say `AreaSubmission` / `autoarea` — same tool.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/RishuiZamin/` (writers, validator, usage catalog, DXF templates) |
| UI    | `src/RVTuk.UI/RishuiZamin/` (Views, ViewModels) |
| Revit | `src/RVTuk.Revit/RishuiZamin/` (Commands, ExternalEvents, extractor, schedule builder, `RZ_AreaParams.txt`) |
| 2023 host | `src/KKarea.Revit/` (links the Revit-layer sources; must never reference `RVTuk.Revit` itself) |
| Tests | `tests/RVTuk.Core.Tests/RishuiZamin/` |

## Docs

- [notes.md](notes.md) — domain notes on the רישוי זמין submission format
- [rules.md](rules.md) — the robot's validation rules
- [backlog.md](backlog.md) — bugs / improvements / ideas
- [specs/](specs/) and [plans/](plans/) — dated historical designs and implementation
  plans (including the KKarea Revit-2023 host)
