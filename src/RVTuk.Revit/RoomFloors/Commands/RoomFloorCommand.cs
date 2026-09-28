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
            int misclicks = 0;

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
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // EnsureWorkPlane may have failed to commit its work-plane transaction above;
                    // PickPoint then throws instead of prompting. Stop the loop rather than let it
                    // propagate, so the summary of anything already done still shows.
                    break;
                }

                // The user may have switched views mid-pick: the point belongs to whichever view
                // is active now, and a non-plan view ends the loop.
                if (!(uidoc.ActiveView is ViewPlan clicked) || clicked.GenLevel == null) break;

                var room = RoomFinder.FindAt(clicked, point);
                if (room == null) misclicks++;
                else Build(doc, room, floorTypeId, skipped);
            }

            if (misclicks > 0)
                skipped.Add(misclicks == 1
                    ? "1 click outside any room: no room there"
                    : $"{misclicks} clicks outside any room: no room there");
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
            return "\u2068" + label + "\u2069";
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
            // "Skipped" here is a mix of rooms (the floor wasn't made or replaced) and, at most,
            // one collapsed line for misclicks — neither of those is a "room", so the heading
            // says "item(s)" rather than counting them as rooms.
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = skipped.Count == 1
                    ? "1 item was skipped"
                    : $"{skipped.Count} items were skipped",
                MainContent = string.Join("\n", skipped.Select(s => "• " + s)),
            };
            dialog.Show();
        }
    }
}
