# Room Floors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Room Floor" ribbon button: click rooms in a plan view and get a finish floor that
follows each room's outline. Clicking a room again replaces its floor, keeping the old floor's
type and height offset.

**Architecture:** A plain `IExternalCommand` in a new `RVTuk.Revit/RoomFloors/` tool folder, with
three helpers:

- `RoomFinder`: a clicked point → the room under it.
- `RoomFloorBuilder`: room → floor, one transaction per room, with the replace logic.
- `RoomFloorLinkStore`: an Extensible Storage link from each floor to its room's UniqueId.

There is no Core code, no UI code and no external events: the pick loop runs in the command's own
API context.

**Tech Stack:** C#, Revit API 2024/2025 (Nice3point packages), WPF `DrawingVisual` for the ribbon
icon.

**Spec:** `docs/tools/room-floors/specs/2026-09-28-room-floors-design.md`

## Global Constraints

- Revit 2024/2025 only. All code lives in `src/RVTuk.Revit`. Nothing goes in `RVTuk.Core`,
  `RVTuk.UI` or `KKarea.Revit`.
- Namespaces equal the root namespace plus the folder path: `RVTuk.Revit.RoomFloors` and
  `RVTuk.Revit.RoomFloors.Commands`.
- The code must compile for both `net48` (Release2024) and `net8.0-windows` (Release2025):
  - nullable is enabled;
  - no BCL APIs newer than .NET Framework 4.8;
  - no `ElementId(int)` constructor, which is gone in 2025.
- Tool name `RoomFloors`, ribbon button name `"RoomFloors"`, ribbon label `"Room\nFloor"`.
- Tooltip, verbatim: `Click rooms in a plan view to create a finish floor that follows each room's outline. Click a room again to update its floor.`
- Transaction name `"Room Floor"`. Status prompt: `Click inside a room to create its floor — Esc to finish`.
- Extensible Storage schema:
  - name `RVTukRoomFloorsLink`;
  - GUID `a9b2b113-115d-48a4-80a5-3063ddc3caca`;
  - vendor `KnafoKlimor`;
  - public read/write;
  - one string field, `RoomUniqueId`.
- Floor type: `doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType)`, else the first
  `FloorType` with `IsFoundationSlab == false`.
- Boundary: `SpatialElementBoundaryLocation.Finish`, with every loop included (inner loops become
  holes).
- Height: `FLOOR_HEIGHTABOVELEVEL_PARAM` = the room's `BaseOffset`. On replace, the old floor's
  type and offset are kept.

## Review Focus

These are the inputs the spec is silent on that are most likely to bite, each pinned by a
case in the Task 3 in-Revit checklist and by code in the task named:

1. **The old floor hosts elements.** These are face-hosted families or an opening cut in the
   floor. Deleting the floor to replace it would silently delete them. **Expected:** the room is
   skipped with the reason "its floor hosts N element(s)…", and the old floor and its hosted
   elements are untouched. Owned by Task 1 (`CountHostedElements`); checklist case 14.
2. **Hebrew room names in the skip summary.** The office's room names are Hebrew mixed with
   numbers and Latin. **Expected:** each skipped room's label reads correctly and doesn't swap
   places with the reason text. Owned by Task 2 (the label is wrapped in Unicode first-strong
   isolates `U+2068`/`U+2069`); checklist case 15.
3. **Workshared model, where another user owns the old floor.** **Expected:** the room is skipped
   with Revit's own message, and the old floor stays. Owned by Task 1 (exception and commit-status
   handling); checklist case 16.
4. **A room bounded by a curved wall.** **Expected:** the floor follows the arc. Owned by Task 1
   (arcs are translated, not rebuilt as lines); checklist case 17.
5. **A plan view with no work plane set, or switching views mid-command.** `PickPoint` needs a
   work plane. **Expected:** the command sets one silently. After a view switch, clicks resolve
   against the view that's now active, and a non-plan view ends the loop. Owned by Task 2
   (`EnsureWorkPlane`, re-reading `ActiveView` after each pick); checklist cases 18–19.

---

## File Structure

| File | Responsibility |
|---|---|
| Create `src/RVTuk.Revit/RoomFloors/RoomFloorLinkStore.cs` | The Extensible Storage link: write a floor→room link; find the floors linked to a room. |
| Create `src/RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs` | The default floor type. Room → finish-face `CurveLoop`s → `Floor`, with replace, in one transaction. |
| Create `src/RVTuk.Revit/RoomFloors/RoomFinder.cs` | Plan view + clicked point → the room under it (the view's level and phase). |
| Create `src/RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs` | The command: view and type checks, the pre-selection, the pick loop, the skip summary. |
| Modify `src/RVTuk.Revit/Application.cs` | The `RegisterRoomFloors` flag, the ribbon button and the icon. |
| Create `docs/tools/room-floors/README.md` | What it is, status, entry point, the in-Revit checklist. |
| Create `docs/tools/room-floors/backlog.md` | The out-of-v1 items. |
| Modify `CLAUDE.md` | Terminology, canonical names, launch surface, feature bullet. |

`RVTuk.Revit.csproj` is SDK-style and picks up new `.cs` files automatically, so it needs no edit.

**Testing reality:** there are no automated Revit tests in this repo, and the spec puts no logic in
Core, so there are no new xunit tests. Each code task is verified by building **both** configs.
The behaviour is verified by the in-Revit checklist in Task 3, which the human runs after
deploying.

---

### Task 1: Floor link storage and the floor builder

**Files:**
- Create: `src/RVTuk.Revit/RoomFloors/RoomFloorLinkStore.cs`
- Create: `src/RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces:
  - `RoomFloorLinkStore.Write(Floor floor, Room room)`: must be called inside a transaction.
  - `RoomFloorLinkStore.FindLinkedFloors(Document doc, Room room) : IReadOnlyList<Floor>`.
  - `RoomFloorBuilder.DefaultFloorTypeId(Document doc) : ElementId`: returns
    `ElementId.InvalidElementId` when there is no usable type.
  - `RoomFloorBuilder.CreateOrReplace(Document doc, Room room, ElementId defaultFloorTypeId) : string?`:
    runs its own transaction and returns `null` on success, else the skip reason. When it skips,
    nothing in the document has changed.

- [ ] **Step 1: Create `RoomFloorLinkStore.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RVTuk.Revit.RoomFloors
{
    /// <summary>
    /// Which room a Room Floor floor was made from.
    ///
    /// Stored on the <b>floor</b>, not the room: deleting a floor by hand takes its link with it,
    /// so a link can never point at a floor that is gone. UniqueId rather than ElementId, so the
    /// link survives workshared sync.
    /// </summary>
    public static class RoomFloorLinkStore
    {
        private static readonly Guid SchemaGuid = new Guid("a9b2b113-115d-48a4-80a5-3063ddc3caca");
        private const string SchemaName = "RVTukRoomFloorsLink";
        private const string FieldName = "RoomUniqueId";

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void Write(Floor floor, Room room)
        {
            var entity = new Entity(GetOrCreateSchema());
            entity.Set(FieldName, room.UniqueId);
            floor.SetEntity(entity);
        }

        /// <summary>Every floor this tool made from <paramref name="room"/>. Normally zero or
        /// one; more only after a copy-paste duplicated a linked floor.</summary>
        public static IReadOnlyList<Floor> FindLinkedFloors(Document doc, Room room)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return Array.Empty<Floor>();

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .Cast<Floor>()
                .Where(f => f.GetEntity(schema).Get<string>(FieldName) == room.UniqueId)
                .ToList();
        }

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId("KnafoKlimor");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }
    }
}
```

- [ ] **Step 2: Create `RoomFloorBuilder.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RVTuk.Revit.RoomFloors
{
    /// <summary>
    /// Room → finish floor. The outline is the room's finish-face boundary (the one Automatic
    /// Ceiling follows); inner loops become holes. A room that already has a floor from this tool
    /// gets it replaced, keeping the old floor's type and height offset, so a type chosen in
    /// Properties survives the update.
    /// </summary>
    public static class RoomFloorBuilder
    {
        /// <summary>The project's default floor type, else its first non-foundation floor type;
        /// <see cref="ElementId.InvalidElementId"/> when it has neither.</summary>
        public static ElementId DefaultFloorTypeId(Document doc)
        {
            var id = doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType);
            if (id != ElementId.InvalidElementId && doc.GetElement(id) is FloorType) return id;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .Where(t => !t.IsFoundationSlab)
                .Select(t => t.Id)
                .FirstOrDefault() ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Creates <paramref name="room"/>'s floor — replacing any floor this tool made for it
        /// before — in one "Room Floor" transaction. Returns null on success, else why the room was
        /// skipped; a skipped room leaves the document exactly as it was.
        /// </summary>
        public static string? CreateOrReplace(Document doc, Room room, ElementId defaultFloorTypeId)
        {
            if (room.Location == null) return "the room is not placed";
            if (room.Area <= 0) return "the room is not enclosed, or is redundant";

            var loops = BoundaryLoops(room, out string? loopError);
            if (loops == null) return loopError;

            var old = RoomFloorLinkStore.FindLinkedFloors(doc, room);

            // Replacing is delete + create, and deleting a floor silently deletes whatever it
            // hosts. Refuse rather than lose someone's work.
            int hosted = old.Sum(CountHostedElements);
            if (hosted > 0)
                return $"its floor hosts {hosted} element(s) that replacing would delete — update it by hand";

            // Read before the delete below.
            var typeId = old.Count > 0 ? old[0].GetTypeId() : defaultFloorTypeId;
            double offset = old.Count > 0
                ? old[0].get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).AsDouble()
                : room.BaseOffset;

            using (var tx = new Transaction(doc, "Room Floor"))
            {
                tx.Start();
                try
                {
                    // Delete and create share the transaction: if Revit rejects the new floor, the
                    // rollback brings the old one back.
                    if (old.Count > 0) doc.Delete(old.Select(f => f.Id).ToList());

                    var floor = Floor.Create(doc, loops, typeId, room.LevelId);
                    floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(offset);
                    RoomFloorLinkStore.Write(floor, room);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ex.Message;
                }

                if (tx.Commit() != TransactionStatus.Committed) return "Revit rejected the floor";
            }
            return null;
        }

        /// <summary>The room's finish-face boundary as loops at its level's elevation, largest
        /// (the outer) first; null with <paramref name="error"/> set when it cannot be built.</summary>
        private static IList<CurveLoop>? BoundaryLoops(Room room, out string? error)
        {
            error = null;
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
            };
            var segmentLists = room.GetBoundarySegments(options);
            if (segmentLists == null || segmentLists.Count == 0)
            {
                error = "the room has no boundary";
                return null;
            }

            // The floor's height comes from its offset parameter, not from the sketch, so the
            // loops go to the level's own elevation. Translating (not rebuilding as lines) keeps
            // arcs from curved walls intact.
            double z = room.Level.ProjectElevation;
            var loops = new List<CurveLoop>();
            try
            {
                foreach (var segments in segmentLists)
                {
                    var loop = new CurveLoop();
                    foreach (var segment in segments)
                    {
                        var curve = segment.GetCurve();
                        loop.Append(curve.CreateTransformed(
                            Transform.CreateTranslation(new XYZ(0, 0, z - curve.GetEndPoint(0).Z))));
                    }
                    loops.Add(loop);
                }
            }
            catch (Exception ex)
            {
                error = "its boundary does not close: " + ex.Message;
                return null;
            }

            // Outer loop first. Revit may sort the loops itself; ordering them here costs
            // nothing and removes the question.
            return loops.OrderByDescending(PlanExtentArea).ToList();
        }

        /// <summary>Area of the loop's plan bounding box: enough to tell the outer loop from the
        /// holes, which it always encloses.</summary>
        private static double PlanExtentArea(CurveLoop loop)
        {
            var points = loop.SelectMany(c => c.Tessellate()).ToList();
            return (points.Max(p => p.X) - points.Min(p => p.X))
                 * (points.Max(p => p.Y) - points.Min(p => p.Y));
        }

        /// <summary>Family instances and openings that deleting <paramref name="floor"/> would
        /// delete with it.</summary>
        private static int CountHostedElements(Floor floor)
        {
            var filter = new LogicalOrFilter(
                new ElementClassFilter(typeof(FamilyInstance)),
                new ElementClassFilter(typeof(Opening)));
            return floor.GetDependentElements(filter).Count;
        }
    }
}
```

- [ ] **Step 3: Build both configs**

Run:
```powershell
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025
```
Expected: both succeed with no new warnings from `RoomFloors/`. The most likely 2025-only break is
an `ElementId` API; the code above uses none that changed.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/RoomFloors/RoomFloorLinkStore.cs src/RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs
git commit -m "feat(room-floors): room-to-floor builder with a replace link on each floor"
```

---

### Task 2: Room finder, command and ribbon button

**Files:**
- Create: `src/RVTuk.Revit/RoomFloors/RoomFinder.cs`
- Create: `src/RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs`, in five places:
  - the usings at the top;
  - the flags block around lines 76–91;
  - `CreateRibbon`, after the Topo Tools block around line 369;
  - the icon methods, after `CreateTopoToolsIcon` around line 413.

**Interfaces:**
- Consumes:
  - `RoomFloorBuilder.DefaultFloorTypeId(Document) : ElementId`;
  - `RoomFloorBuilder.CreateOrReplace(Document, Room, ElementId) : string?` (null = success).
- Produces:
  - `RoomFinder.FindAt(ViewPlan view, XYZ point) : Room?`;
  - `RVTuk.Revit.RoomFloors.Commands.RoomFloorCommand`, the ribbon's command class.

- [ ] **Step 1: Create `RoomFinder.cs`**

```csharp
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RVTuk.Revit.RoomFloors
{
    /// <summary>
    /// The room under a clicked point. A point pick, not an element pick: rooms are only
    /// clickable where their interior fill or reference is visible, which it usually is not.
    /// </summary>
    public static class RoomFinder
    {
        /// <summary>Lift above a room's base (feet) so the test point is inside its volume
        /// rather than on its bottom face.</summary>
        private const double Lift = 0.01;

        /// <summary>The room on <paramref name="view"/>'s level and in its phase that contains
        /// the clicked plan position, or null. Each candidate is tested at its own base, so a
        /// room with a base offset is still found.</summary>
        public static Room? FindAt(ViewPlan view, XYZ point)
        {
            var level = view.GenLevel;
            var phaseId = view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();

            return new FilteredElementCollector(view.Document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Location != null && r.LevelId == level.Id)
                .Where(r => phaseId == null
                         || r.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() == phaseId)
                .FirstOrDefault(r => r.IsPointInRoom(
                    new XYZ(point.X, point.Y, level.ProjectElevation + r.BaseOffset + Lift)));
        }
    }
}
```

- [ ] **Step 2: Create `Commands/RoomFloorCommand.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.RoomFloors.Commands
{
    /// <summary>
    /// Room Floor: Automatic Ceiling, for floors. Click inside rooms in a plan view; each click
    /// creates (or replaces) that room's finish floor at once, in its own transaction, so Ctrl+Z
    /// undoes one floor. Rooms already selected go first. Esc ends; anything skipped is listed.
    /// Runs entirely in the command's API context — no external events.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RoomFloorCommand : IExternalCommand
    {
        private const string Title = "Room Floor";
        private const string Prompt = "Click inside a room to create its floor — Esc to finish";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewPlan start) || start.GenLevel == null)
            {
                TaskDialog.Show(Title, "Open a floor plan first: Room Floor picks rooms in a plan view.");
                return Result.Cancelled;
            }

            var floorTypeId = RoomFloorBuilder.DefaultFloorTypeId(doc);
            if (floorTypeId == ElementId.InvalidElementId)
            {
                TaskDialog.Show(Title, "This project has no floor type to use. Load or create one first.");
                return Result.Cancelled;
            }

            var skipped = new List<string>();

            var preselected = uidoc.Selection.GetElementIds()
                .Select(doc.GetElement)
                .OfType<Room>()
                .ToList();
            foreach (var room in preselected) Build(doc, room, floorTypeId, skipped);

            while (uidoc.ActiveView is ViewPlan view && view.GenLevel != null)
            {
                EnsureWorkPlane(view);

                XYZ point;
                try
                {
                    point = uidoc.Selection.PickPoint(Prompt);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                // The user may have switched views mid-pick: the point belongs to whichever view
                // is active now, and a non-plan view ends the loop.
                if (!(uidoc.ActiveView is ViewPlan clicked) || clicked.GenLevel == null) break;

                var room = RoomFinder.FindAt(clicked, point);
                if (room == null) skipped.Add("A click outside any room: no room there");
                else Build(doc, room, floorTypeId, skipped);
            }

            if (skipped.Count > 0) ShowSummary(skipped);
            return Result.Succeeded;
        }

        private static void Build(Document doc, Room room, ElementId floorTypeId, List<string> skipped)
        {
            var reason = RoomFloorBuilder.CreateOrReplace(doc, room, floorTypeId);
            if (reason != null) skipped.Add($"{Label(room)}: {reason}");
        }

        /// <summary>"101 Kitchen", wrapped in first-strong isolates (U+2068…U+2069): room names
        /// are often Hebrew, and without the isolate a Hebrew name ending in a number drags the
        /// ": reason" that follows it into its right-to-left run.</summary>
        private static string Label(Room room)
        {
            var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            var label = $"{room.Number} {name}".Trim();
            if (label.Length == 0) label = $"Room {room.Id}";
            return "⁨" + label + "⁩";
        }

        /// <summary>PickPoint needs a work plane, and a plan whose work plane was never set has
        /// none. Sets it to the view's level, in its own small transaction.</summary>
        private static void EnsureWorkPlane(ViewPlan view)
        {
            if (view.SketchPlane != null) return;

            using (var tx = new Transaction(view.Document, "Room Floor: set work plane"))
            {
                tx.Start();
                view.SketchPlane = SketchPlane.Create(view.Document, view.GenLevel.Id);
                tx.Commit();
            }
        }

        private static void ShowSummary(List<string> skipped)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = skipped.Count == 1
                    ? "1 room was skipped"
                    : $"{skipped.Count} rooms were skipped",
                MainContent = string.Join("\n", skipped.Select(s => "• " + s)),
            };
            dialog.Show();
        }
    }
}
```

- [ ] **Step 3: Add the using and the flag to `Application.cs`**

With the other tool usings at the top of the file (after `using RVTuk.Revit.TopoTools.ExternalEvents;`),
add:

```csharp
using RVTuk.Revit.RoomFloors.Commands;
```

After the `RegisterTopoTools` field (it ends `private static readonly bool RegisterTopoTools = true;`),
add:

```csharp

        /// <summary>
        /// Room Floor ships on the RVTuk panel: a one-shot command (no pane, no dialog) that makes
        /// a finish floor per clicked room, like Automatic Ceiling does for ceilings.
        /// </summary>
        private static readonly bool RegisterRoomFloors = true;
```

- [ ] **Step 4: Add the ribbon button**

In `CreateRibbon`, immediately after the closing `}` of the `if (RegisterTopoTools) { … }` block
and before `if (!RegisterNeoProperties) return;`, add:

```csharp

            if (RegisterRoomFloors)
            {
                var roomFloorBtn = new PushButtonData(
                    "RoomFloors",
                    "Room\nFloor",
                    assemblyPath,
                    typeof(RoomFloorCommand).FullName!)
                {
                    ToolTip = "Click rooms in a plan view to create a finish floor that follows each room's outline. Click a room again to update its floor."
                };
                roomFloorBtn.LargeImage = CreateRoomFloorsIcon(32);
                roomFloorBtn.Image      = CreateRoomFloorsIcon(16);

                panel.AddItem(roomFloorBtn);
            }
```

- [ ] **Step 5: Add the icon**

Immediately after the `CreateTopoToolsIcon` method, add:

```csharp

        private static BitmapSource CreateRoomFloorsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // An L-shaped room: white walls around an orange floor.
                var outline = new StreamGeometry();
                using (var g = outline.Open())
                {
                    g.BeginFigure(new WpfPoint(s * 0.14, s * 0.14), true, true);
                    g.PolyLineTo(new[]
                    {
                        new WpfPoint(s * 0.86, s * 0.14),
                        new WpfPoint(s * 0.86, s * 0.86),
                        new WpfPoint(s * 0.50, s * 0.86),
                        new WpfPoint(s * 0.50, s * 0.50),
                        new WpfPoint(s * 0.14, s * 0.50),
                    }, true, true);
                }
                outline.Freeze();

                var wall = new Pen(new SolidColorBrush(Colors.White), Math.Max(1, s * 0.06));
                wall.Freeze();
                ctx.DrawGeometry(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), wall, outline);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
```

- [ ] **Step 6: Build both configs and run the Core tests**

Run:
```powershell
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```
Expected: both builds succeed, and the Core tests pass unchanged (Core was not touched; this
confirms nothing else broke).

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Revit/RoomFloors/RoomFinder.cs src/RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs src/RVTuk.Revit/Application.cs
git commit -m "feat(room-floors): Room Floor ribbon button — click rooms to get finish floors"
```

---

### Task 3: Docs and the in-Revit checklist

**Files:**
- Create: `docs/tools/room-floors/README.md`
- Create: `docs/tools/room-floors/backlog.md`
- Modify: `CLAUDE.md`

**Interfaces:** documentation only. It names `RoomFloorCommand`, `RoomFinder`, `RoomFloorBuilder`,
`RoomFloorLinkStore` and `RegisterRoomFloors` exactly as Tasks 1–2 define them.

- [ ] **Step 1: Create `docs/tools/room-floors/README.md`**

```markdown
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
```

- [ ] **Step 2: Create `docs/tools/room-floors/backlog.md`**

```markdown
# Room Floors — backlog

## Ideas (out of v1, each a clean addition)

- Extend the floor into door openings: a strip under each door hosted in the room's bounding
  walls, to the middle of the wall.
- Pick the floor type (and offset) up front, or take it from the room's *Floor Finish* parameter.
- Batch mode: every room on a level in one go.
- Rooms in linked models.
- Structural slabs that run under walls, not just finish floors.
- Replace a floor that hosts elements by editing its sketch (`SketchEditScope`) instead of
  deleting it, so the hosted elements survive.

## Bugs

(none yet)

## Done

- v1: click-to-create, replace keeping type and offset, skip summary (2026-09-28).
```

- [ ] **Step 3: Update `CLAUDE.md`**

Make these five edits:

1. **Terminology table, "Tool" row.** Change the end of the row from
   `**Auto Dimensions**, **Topo Tools**, **Neo Properties**. |`
   to
   `**Auto Dimensions**, **Topo Tools**, **Room Floors** (ribbon label "Room Floor"), **Neo Properties**. |`
2. **Canonical names.** Change
   `` `AutoDimensions`, `TopoTools`, `NeoProperties`. Inside every project ``
   to
   `` `AutoDimensions`, `TopoTools`, `RoomFloors`, `NeoProperties`. Inside every project ``
3. **v1 launch surface.** In the paragraph starting `**v1 launch surface:**`, change
   `Neo Properties is code-complete but hidden`
   to
   `**Room Floors** (ribbon "Room Floor") sits on the same panel, gated by `RegisterRoomFloors` (on). Neo Properties is code-complete but hidden`
4. **Feature bullet.** Insert this bullet immediately before the `- **Neo Properties** (hidden for v1)` bullet:

   ```markdown
   - **Room Floors** (ribbon "Room Floor") — Automatic Ceiling for floors: click inside rooms in a floor plan and each gets a finish floor following its finish-face outline (columns become holes), at the room's level with its top at the room's base. Uses the project's default floor type. Each floor carries an Extensible Storage link to its room's UniqueId, so clicking the room again replaces the floor while keeping the type and offset set in Properties; a floor that hosts elements is never replaced. A plain external command — no pane, no external events. Revit 2024/2025 only. See [`docs/tools/room-floors/README.md`](docs/tools/room-floors/README.md).
   ```
5. **Architecture, folder list.** Change
   `` tool** (`FamilyBrowser/`, `RishuiZamin/`, `AutoDimensions/`, `NeoProperties/`) plus ``
   to
   `` tool** (`FamilyBrowser/`, `RishuiZamin/`, `AutoDimensions/`, `TopoTools/`, `RoomFloors/`, `NeoProperties/`, …) plus ``

- [ ] **Step 4: Commit**

```bash
git add docs/tools/room-floors/README.md docs/tools/room-floors/backlog.md CLAUDE.md
git commit -m "docs(room-floors): README with the in-Revit checklist, backlog, CLAUDE.md"
```

- [ ] **Step 5: Hand off for the in-Revit check**

The human deploys from an elevated shell with `.\Deploy.ps1 2024` (and `2025`), restarts Revit,
and runs the README checklist. Cases 1 and 3 come first: case 3 confirms the outer-loop ordering
the spec flagged. Each case is ticked in the README only once it has actually been checked, and
every failure becomes a bug in `backlog.md`, then a fix.
