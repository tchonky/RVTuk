using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The topo line a height text belongs to, stored on the text.
    ///
    /// The field is a plain Int64 (a supported simple-field type in Revit 2024), not an ElementId:
    /// Revit does not copy ElementId fields to elements made by copy/paste or array, while the
    /// linker relies on a copied text still pointing at the original line so it can tell the copy
    /// from the original and hand the copy the new line.
    /// </summary>
    public static class HeightTextLinkStore
    {
        private static readonly Guid SchemaGuid = new Guid("ebcc7eef-5290-473c-bc7a-79e14b69cea0");
        private const string SchemaName = "RVTukTopoHeightTextLink";
        private const string FieldName = "LineId";

        public static bool TryGetLineId(TextNote text, out long lineId)
        {
            lineId = 0;
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return false;

            var entity = text.GetEntity(schema);
            if (!entity.IsValid()) return false;

            lineId = entity.Get<long>(FieldName);
            return true;
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void Set(TextNote text, long lineId)
        {
            var entity = new Entity(GetOrCreateSchema());
            entity.Set(FieldName, lineId);
            text.SetEntity(entity);
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
            builder.AddSimpleField(FieldName, typeof(long));
            return builder.Finish();
        }
    }
}
