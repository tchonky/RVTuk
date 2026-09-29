using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RVTuk.Revit.Shared;

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
            // hosts (FamilyInstance, Opening, a slab edge) and whatever annotates it (a floor
            // tag, a spot elevation/dimension). Refuse rather than lose someone's work.
            int hostedCount = old.Sum(f => DependentsCounter.Hosted(f));
            int annotationCount = old.Sum(f => DependentsCounter.Annotations(f));
            if (hostedCount > 0 || annotationCount > 0)
            {
                var parts = new List<string>();
                if (annotationCount > 0) parts.Add($"{annotationCount} tag(s)/dimension(s)");
                if (hostedCount > 0) parts.Add($"{hostedCount} hosted element(s)");
                return $"its floor carries {string.Join(" and ", parts)} that replacing would delete — update it by hand";
            }

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

        /// <summary>The room's finish-face boundary as loops flattened to a single Z, largest
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

            // The floor's height comes from its offset parameter, not from the sketch, so all
            // loops are flattened to one Z — the first boundary curve's start point, since the
            // curves are already horizontal. This avoids depending on the level's project
            // elevation, which can differ from where the room's boundary curves actually sit.
            // Translating (not rebuilding as lines) keeps arcs from curved walls intact.
            double z = segmentLists.SelectMany(s => s).First().GetCurve().GetEndPoint(0).Z;
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

            return LoopOrder.OuterFirst(loops);
        }
    }
}
