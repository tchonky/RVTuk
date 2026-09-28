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
