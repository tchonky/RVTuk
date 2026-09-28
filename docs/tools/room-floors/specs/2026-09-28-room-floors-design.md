# Room Floors — a finish floor from a room's outline, one click per room

**Date:** 2026-09-28
**Status:** Design, approved

## Why

Revit's ceiling tool has *Automatic Ceiling*: click inside a room, get a ceiling that follows it.
Floors have no equivalent. A finish floor per room means sketching each boundary by hand or with
*Pick Walls*, closing the loops, cutting out the columns, and doing it again whenever a wall moves.
The room already knows its outline, so the tool takes it from there.

## Scope

**In:** one ribbon button that creates a **finish floor** per room from the room's boundary. You
click rooms in a plan view the way Automatic Ceiling works. Clicking a room again replaces its
floor, so a room whose walls moved gets a floor that fits again.

**Out of v1:**
- structural slabs that run under walls;
- extending the floor into door openings. The floor stops at the wall face, exactly like the room
  boundary, and door gaps are filled by hand;
- rooms in linked models;
- choosing the floor type up front, or taking it from the room's *Floor Finish* parameter;
- a batch "whole level" mode;
- a pane or a dialog.

Each of these would be a clean addition on this foundation.

**Revit 2024/2025 only.** Like the other RVTuk tools it lives in `RVTuk.Revit`. KKarea (2023) hosts
only Rishui Zamin and does not get it.

## Workflow

1. Press **Room Floor** on the RVTuk panel. The active view must be a floor plan (`ViewPlan` with a
   `GenLevel`). Any other view gets a short message and the command ends.
2. **Rooms already selected** when the button is pressed get their floors first. The pre-selection
   is filtered to `Room`, and non-room elements are ignored silently.
3. The command then loops on `Selection.PickPoint` with the status prompt
   *"Click inside a room to create its floor — Esc to finish"*. Each click creates, or replaces,
   that room's floor **immediately, in its own transaction** named "Room Floor". The user sees each
   floor appear as they click, and each Ctrl+Z undoes one floor.
4. **Esc** (`OperationCanceledException`) ends the loop. If any click or pre-selected room was
   skipped, one `TaskDialog` lists each one with its reason. If none was skipped, the command ends
   silently.
5. The command returns `Result.Succeeded` even when nothing was created. Each floor was already
   committed in its own transaction, so there is nothing for Revit to roll back.

### Finding the room under a click

`PickPoint` returns a point on the view's work plane. A plain element pick (`PickObject`) would only
hit a room where its interior fill or reference is visible, which it usually is not. So the room is
resolved from the point:

- **Candidates:** the rooms whose `LevelId` is the view's `GenLevel` and whose phase (`ROOM_PHASE`)
  is the view's phase (`VIEW_PHASE`).
- **Hit test:** each candidate is tested with `room.IsPointInRoom(new XYZ(x, y, z))`. `z` is that
  room's own base, `level.Elevation + ROOM_LOWER_OFFSET`, plus a small lift (0.01 ft) so the point
  is inside the room's volume rather than on its floor plane. A room with a base offset is still
  found.
- **Result:** the first candidate that contains the point wins. None means the skip reason
  "no room here".

## Geometry

- **Boundary:** `room.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = Finish })`.
  This is the finish-face boundary, the same outline Automatic Ceiling follows. Each segment list
  becomes one `CurveLoop` built from `segment.GetCurve()`. Every loop is included, so **inner loops
  (columns, shafts) become holes**.
- **Unusable rooms:** the room is skipped with a reason if it
  - is unplaced (`Location == null`);
  - is not enclosed or redundant (`Area <= 0`);
  - returns no boundary segments.
- **Flattening:** the curves are projected to the level's elevation before `Floor.Create`. The
  boundary curves lie at the room's base, and the floor's height is carried by its offset
  parameter, not by the sketch.
- **Creation:** `Floor.Create(doc, loops, floorTypeId, room.LevelId)`. Revit sorts outer and inner
  loops itself. The implementation plan's first Revit check confirms this for a room with a column
  hole. If it does not hold, the builder orders the loops by area, largest (the outer) first.
- **Height:** the floor's `FLOOR_HEIGHTABOVELEVEL_PARAM` is set to the room's `ROOM_LOWER_OFFSET`.
  Revit floors are top-referenced, so the **top of the finish sits at the room's base**.
- **Bad geometry:** if Revit rejects the geometry (tiny segments, self-intersection), the exception
  message becomes that room's skip reason. The failed click's transaction is rolled back and the
  loop continues.

### Floor type

The **project's default floor type**: `doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType)`.
If that is invalid, the first `FloorType` that is not a foundation slab
(`FloorType.IsFoundationSlab == false`) is used. If the project has no usable floor type at all,
the command says so and ends before the first pick. Users change the type afterwards in
Properties, and a replace keeps that choice (below).

## Tracking and replacing

Each floor the tool creates carries an **Extensible Storage entity** holding the UniqueId of the
room it was made from, in the same shape as `TopoLedgerStore`.

| Schema property | Value |
|---|---|
| Name | `RVTukRoomFloorsLink` |
| GUID | new, fixed in code |
| Vendor | `KnafoKlimor` |
| Access | public read/write |
| Field | one string field, `RoomUniqueId` |

- **The link lives on the floor, not on the room.** Deleting a floor by hand therefore deletes its
  link too, and nothing can go stale.
- **UniqueId rather than ElementId,** so the link survives workshared sync and copy-paste
  renumbering.

**On every room hit:**

1. Find the existing floors linked to this room, using
   `FilteredElementCollector(doc).OfClass(typeof(Floor)).WherePasses(new ExtensibleStorageFilter(schemaGuid))`
   and then the matching `RoomUniqueId`.
2. If there are any, **read the first one's type and height offset**, delete them all, and create
   the new floor with that type and offset rather than the defaults. A type the user changed in
   Properties (e.g. "Tiles 20mm") and a height they nudged both survive the update. More than one
   linked floor can only come from copy-paste; all of them go, and the first one's settings win.
3. Otherwise, create the floor with the default type and the room's base offset.
4. Write the link entity on the new floor.

The delete and the create run in the same transaction, so a rejected new floor rolls the old one
back and the room keeps the floor it had.

## Code layout

A new tool folder, `RoomFloors/`, in `RVTuk.Revit` only. There is no Core or UI code: everything
is Revit geometry and API calls, and there is no window.

| File | Role |
|---|---|
| `RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs` | `IExternalCommand`. Checks the view, resolves the floor type, handles the pre-selection, runs the pick loop, shows the skip summary. |
| `RVTuk.Revit/RoomFloors/RoomFinder.cs` | View + point → `Room?`, as in "Finding the room under a click". |
| `RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs` | Room → `CurveLoop`s → `Floor`. Returns either the floor or a skip reason. Includes the replace logic. |
| `RVTuk.Revit/RoomFloors/RoomFloorLinkStore.cs` | The Extensible Storage schema: write the link, find the floors linked to a room. |

`Application.cs` gets:

- a `RegisterRoomFloors` flag, **on**, next to `RegisterAutoDimensions`;
- a `PushButtonData("RoomFloors", "Room\nFloor", …, typeof(RoomFloorCommand))` on the RVTuk panel,
  after Topo Tools, with the tooltip *"Click rooms in a plan view to create a finish floor that
  follows each room's outline. Click a room again to update its floor."*;
- a `CreateRoomFloorsIcon(size)` drawn like the existing icons.

Namespaces follow the folders: `RVTuk.Revit.RoomFloors`, `RVTuk.Revit.RoomFloors.Commands`.
The canonical tool name is **`RoomFloors`** and the ribbon label is **"Room Floor"**.

## Threading

This is a plain `IExternalCommand` with no external events: the whole pick loop runs inside the
command's API context on Revit's main thread, the same way the DWG Exporter's modal dialog does.

## Docs

- `docs/tools/room-floors/README.md`: what it is, status, entry point, and the in-Revit checklist
  below.
- `docs/tools/room-floors/backlog.md`: the out-of-v1 items listed under Scope.
- `CLAUDE.md`:
  - add `RoomFloors` to the canonical tool names and the terminology table;
  - add it to the v1 launch surface;
  - add a feature bullet.

## Testing

There is no pure logic worth moving into Core, so there are no new xunit tests. Verification is:

1. `dotnet build RVTuk.sln -c Release2024` and `-c Release2025` both succeed.
2. `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` still passes.
3. The in-Revit checklist (in the README, ticked as each is actually checked):

| # | Case | Expected |
|---|------|----------|
| 1 | Rectangular room | Floor matches the room's finish-face outline, top at the level |
| 2 | L-shaped room | Correct outline, no self-intersection error |
| 3 | Room with a free-standing column | Floor has a hole at the column |
| 4 | Room bounded partly by room separation lines | Floor follows the separation line |
| 5 | Room with a base offset of 50 mm | Found by a click; floor top at +50 mm |
| 6 | Move a wall, click the room again | Old floor gone, new floor fits |
| 7 | Change the floor's type and offset in Properties, click again | New floor keeps that type and offset |
| 8 | Delete a floor by hand, click the room | Fresh floor with the default type, no error |
| 9 | Click outside any room / on an unenclosed room | Listed in the summary at Esc, no floor |
| 10 | Pre-select three rooms, press the button, Esc | Three floors, no picking needed |
| 11 | Ctrl+Z after three clicks | Undoes the last floor only |
| 12 | Press the button in a 3D view or section | Message, command ends |
| 13 | Same view on a later phase | Only rooms of the view's phase are hit |
