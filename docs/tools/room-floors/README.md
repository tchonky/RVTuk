# Room Floors

**What it is:** Automatic Ceiling, for floors. Press **Room Floor** on the RVTuk panel, then click
inside rooms in a floor plan. Each click creates a finish floor that follows the room's
finish-face outline, with columns and shafts cut out as holes. Rooms already selected when you
press the button get their floors first. Esc ends the command, and one dialog lists anything that
was skipped.

**Click a room again to update its floor.** Each floor remembers which room it was made from (an
Extensible Storage link on the floor, `RoomFloorLinkStore`). A second click deletes the old floor
and creates a new one, **keeping the old floor's type and height offset**, so a finish type
chosen in Properties survives the update. A floor that hosts elements (face-based families, an
opening) is never replaced, because deleting it would delete them too. That room is listed as
skipped instead.

**Type and height.** New floors use the project's default floor type; change it in Properties
afterwards. The floor sits on the room's level with its top at the room's base: its height offset
equals the room's Base Offset.

**Stops at the wall face.** Like the room boundary, the floor does not run into door openings.
Door sills are filled by hand for now (see the backlog).

**Status:** v1, Revit 2024/2025. Behind `RegisterRoomFloors` (on) in `src/RVTuk.Revit/Application.cs`.
Not in KKarea (2023).

**Entry point:** `RVTuk.Revit.RoomFloors.Commands.RoomFloorCommand`, a plain external command
with no pane and no external events.

**Spec:** [`specs/2026-09-28-room-floors-design.md`](specs/2026-09-28-room-floors-design.md) ·
**Plan:** [`plans/2026-09-28-room-floors.md`](plans/2026-09-28-room-floors.md)

## In-Revit checklist

Tick each case only once it has actually been checked in Revit.

| # | Case | Expected | 2024 | 2025 |
|---|------|----------|------|------|
| 1 | Rectangular room | Floor matches the room's finish-face outline, top at the level | [ ] | [ ] |
| 2 | L-shaped room | Correct outline, no error | [ ] | [ ] |
| 3 | Room with a free-standing column | Floor has a hole at the column | [ ] | [ ] |
| 4 | Room bounded partly by room separation lines | Floor follows the separation line | [ ] | [ ] |
| 5 | Room with a Base Offset of 50 mm | Found by a click; floor top at +50 mm | [ ] | [ ] |
| 6 | Move a wall, click the room again | Old floor gone, new floor fits | [ ] | [ ] |
| 7 | Change the floor's type and offset in Properties, click again | New floor keeps that type and offset | [ ] | [ ] |
| 8 | Delete a floor by hand, click the room | Fresh floor with the default type, no error | [ ] | [ ] |
| 9 | Click outside any room / on an unenclosed room | Listed in the summary at Esc, no floor | [ ] | [ ] |
| 10 | Pre-select three rooms, press the button, Esc | Three floors, no picking needed | [ ] | [ ] |
| 11 | Ctrl+Z after three clicks | Undoes the last floor only | [ ] | [ ] |
| 12 | Press the button in a 3D view or section | Message, command ends | [ ] | [ ] |
| 13 | Plan view on a later phase than some rooms | Only rooms of the view's phase are hit | [ ] | [ ] |
| 14 | Place a face-based family on a room's floor, click the room again | Skipped with "its floor hosts 1 element(s)…"; floor and family untouched | [ ] | [ ] |
| 15 | Hebrew room named e.g. `מטבח 2`, unenclosed, clicked | Summary line reads correctly; the name and the reason do not swap places | [ ] | [ ] |
| 16 | Workshared: the room's floor is borrowed by another user, click the room | Skipped with Revit's message; the old floor stays | [ ] | [ ] |
| 17 | Room bounded by a curved wall | Floor follows the arc | [ ] | [ ] |
| 18 | A new floor plan view whose work plane was never set | Picking works; no error | [ ] | [ ] |
| 19 | Mid-command, switch to another level's plan and click a room there | That room gets its floor; switching to a 3D view ends the command | [ ] | [ ] |
| 20 | A room from the level below whose upper limit reaches this level | Not hit from this level's plan | [ ] | [ ] |
