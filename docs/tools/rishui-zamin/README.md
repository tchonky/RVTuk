# Rishui Zamin (ribbon button: "Area Calc")

**What it is:** reads the Areas on the open sheet and exports the paired `.dxf` + `.dat`
files the רישוי זמין (Rishui Zamin) area-calculation robot expects (`RZ_FRAME` /
`RZ_FLOOR` / `RZ_AREA` layers + attribute blocks). The window's "Setup Usage Keys"
action binds the `RZ_*` shared parameters to Areas and creates/tops-up the usage-key
schedules — no separate ribbon command.

**Setting usage codes and reading the areas** (2026-11-22): the Areas tab shows each area's
m², each floor's m² per usage group (Primary green, Service cyan, Other grey, Process yellow,
no code red) with Σ = Primary + Service, and the same sums for the whole sheet at the top.
Select one or several areas (Ctrl/Shift-click) and set their New or Old usage from the
detail strip: the picker searches by code, Hebrew name or group and suggests codes from the
areas' names. It sets the same New Usage / Old Usage key you would pick in Properties, in
one undo step, offers to run Setup Usage Keys first when the key schedules are missing, and
empties the office template's old "Usage Type" parameters on those areas. See
[specs/2026-11-22-area-usage-assign-design.md](specs/2026-11-22-area-usage-assign-design.md).

**Area table and plot limits** (2026-11-23): the Table tab lays the sheet out like the
robot's "חישוב שטחים בניין" table, to check the numbers before uploading. It has one row per
floor (by elevation), then Above ground, Below ground and Building. It has one column per
usage code in use, under Primary, Service and Other, each group with its Σ, and % of plot for
Primary and Service. There is a No code column when some area has none, and a Removed column
for 300–302. Type the plot area and the plan's allowed % of plot for Primary, Service and
Total. A strip checks the Building row against them: green ok, amber at 95 % of a limit or
more, red over. The values are kept in the project (Extensible Storage on Project
Information, one undo step per change). See
[specs/2026-11-23-area-table-design.md](specs/2026-11-23-area-table-design.md).

**Status:** shipped (v1 launch surface). Also shipped to Revit 2023 as
**KKarea**, a standalone one-button add-in hosting only this tool (RVTuk itself was dropped
from 2023; Revit 2026 and 2027 get the full RVTuk add-in).

**Names:** code `RishuiZamin`; ribbon button displays "Area Calc" (internal id
`AreaCalc`). Older code/docs say `AreaSubmission` / `autoarea` — same tool.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/RishuiZamin/` (writers, validator, usage catalog, DXF templates) |
| UI    | `src/RVTuk.UI/RishuiZamin/` (Views, ViewModels) |
| Revit | `src/RVTuk.Revit/RishuiZamin/` (Commands, ExternalEvents, extractor, schedule builder) |
| KKarea host (2023) | `src/KKarea.Revit/` (links the Revit-layer sources; must never reference `RVTuk.Revit` itself) |
| Tests | `tests/RVTuk.Core.Tests/RishuiZamin/`, `tests/RVTuk.UI.Tests/RishuiZamin/` |

## Docs

- [notes.md](notes.md) — domain notes on the רישוי זמין submission format
- [rules.md](rules.md) — the robot's validation rules
- [backlog.md](backlog.md) — bugs / improvements / ideas
- [specs/](specs/) and [plans/](plans/) — dated historical designs and implementation
  plans (including the KKarea Revit-2023 host)
