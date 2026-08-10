using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// A topo line's shared (survey) elevation, stored in the tool's own Extensible Storage on the
    /// line itself.
    ///
    /// This started life as a shared parameter, `TOPO_Elevation`, bound to the Lines category. That
    /// is impossible: `OST_Lines` has <c>AllowsBoundParameters == false</c>, so <c>BindingMap.Insert</c>
    /// refuses any *visible* shared or project parameter on it and returns false — which is also why
    /// Revit's own Parameter Properties dialog shows "Lines" with no checkbox. Only a
    /// non-user-visible parameter binds, and an invisible parameter cannot be typed into, which was
    /// the whole point. Changing the element does not rescue it either: no view-specific curve of
    /// arbitrary shape accepts a bound parameter.
    ///
    /// So the height lives here and the pane is its editor. The cost is that it is invisible outside
    /// that pane — which is why the pane links selection both ways.
    ///
    /// The field declares <c>SetSpec(SpecTypeId.Length)</c>, so the number says what it is rather
    /// than being a bare double; it is read and written in feet, Revit's internal unit, so no
    /// conversion happens on the way through.
    /// </summary>
    public static class TopoElevationStore
    {
        private static readonly Guid SchemaGuid = new Guid("6d21f8b4-0c93-4e57-a8d6-b45f70e29c18");
        private const string SchemaName = "RVTukTopoLineElevation";
        private const string FieldName = "SharedElevation";

        /// <summary>
        /// The line's shared elevation, or false when none was ever set. Absence of the entity is
        /// the only "unset" — 0.000 is a legitimate shared elevation and must never read as
        /// "forgotten".
        /// </summary>
        public static bool TryGet(Element line, out double elevationFeet)
        {
            elevationFeet = 0;

            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return false;

            var entity = line.GetEntity(schema);
            if (!entity.IsValid()) return false;

            elevationFeet = entity.Get<double>(FieldName, UnitTypeId.Feet);
            return true;
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void Set(Element line, double elevationFeet)
        {
            var entity = new Entity(GetOrCreateSchema());
            entity.Set(FieldName, elevationFeet, UnitTypeId.Feet);
            line.SetEntity(entity);
        }

        /// <summary>Un-sets the height. Caller must already be inside a transaction.</summary>
        public static void Clear(Element line)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return;
            line.DeleteEntity(schema);
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
            builder.AddSimpleField(FieldName, typeof(double)).SetSpec(SpecTypeId.Length);
            return builder.Finish();
        }
    }
}
