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

        /// <summary>Every floor this tool made from <paramref name="room"/>, on the room's own
        /// level and not in a group. Normally zero or one; more only after a copy-paste
        /// duplicated a linked floor on that same level. Paste-Aligned-to-levels copies carry the
        /// original room's UniqueId too, but they are not "this room's floor" — grouped copies and
        /// copies on other levels are left alone rather than replaced or deleted.</summary>
        public static IReadOnlyList<Floor> FindLinkedFloors(Document doc, Room room)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return Array.Empty<Floor>();

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .Cast<Floor>()
                .Where(f => f.GetEntity(schema).Get<string>(FieldName) == room.UniqueId)
                .Where(f => f.LevelId == room.LevelId && f.GroupId == ElementId.InvalidElementId)
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
