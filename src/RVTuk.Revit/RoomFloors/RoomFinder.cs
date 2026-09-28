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
        /// <summary>The room on <paramref name="view"/>'s level and in its phase that contains
        /// the clicked plan position, or null. Each candidate is tested at the vertical middle of
        /// its own bounding box, not at the level's elevation plus its base offset: that stays
        /// inside the room's volume regardless of the project's elevation base, and even when
        /// Area and Volume computation raises the room's usable bottom above its base.</summary>
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
                .FirstOrDefault(r =>
                {
                    var bb = r.get_BoundingBox(null);
                    if (bb == null) return false;
                    var z = (bb.Min.Z + bb.Max.Z) / 2;
                    return r.IsPointInRoom(new XYZ(point.X, point.Y, z));
                });
        }
    }
}
