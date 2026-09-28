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
