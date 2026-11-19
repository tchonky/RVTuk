using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Per toposolid or floor: the outline corners Topo Tools has set to a line's height, with the
    /// height each had of its own, so they can go back. Stored on the element (like
    /// <see cref="TopoLedgerStore"/>) so the record outlives the line that raised the corner.
    /// </summary>
    public static class OutlineCornerStore
    {
        private static readonly Guid SchemaGuid = new Guid("cca5f35d-98c8-466c-9175-4d51afe00df1");
        private const string SchemaName = "RVTukTopoToolsOutlineHeights";
        private const string FieldName = "CornersByXy";

        public static IReadOnlyList<CornerRecord> Read(Element element) =>
            OutlineCornerRecordCodec.Decode(Raw(element));

        /// <summary>The stored text as it is, "" when there is none — for the session snapshot.</summary>
        public static string Raw(Element element)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return "";

            var entity = element.GetEntity(schema);
            if (!entity.IsValid()) return "";

            return entity.Get<string>(FieldName) ?? "";
        }

        /// <summary>Caller must already be inside a transaction. An empty list removes the record.</summary>
        public static void Write(Element element, IReadOnlyList<CornerRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null) element.DeleteEntity(existing);
                return;
            }

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(FieldName, OutlineCornerRecordCodec.Encode(records));
            element.SetEntity(entity);
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
