using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Parameter values a converted element could not take, kept on it for the return trip. A
    /// floor's Structural flag, or a shared parameter bound to Floors only, has no home on a
    /// toposolid: it waits here and goes back when the toposolid becomes a floor again.
    /// </summary>
    public static class ParkedParameterStore
    {
        public static readonly Guid SchemaGuid = new Guid("309b77b4-64cd-45c7-bd73-d6a81b1f85ae");
        private const string SchemaName = "RVTukTopoToolsParkedParameters";
        private const string FieldName = "Entries";

        public static IReadOnlyList<ParkedParameter> Read(Element element)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return Array.Empty<ParkedParameter>();

            var entity = element.GetEntity(schema);
            if (!entity.IsValid()) return Array.Empty<ParkedParameter>();

            return ParkedParameterCodec.Decode(entity.Get<string>(FieldName));
        }

        /// <summary>Caller must already be inside a transaction. An empty list removes the record.</summary>
        public static void Write(Element element, IReadOnlyList<ParkedParameter> entries)
        {
            if (entries.Count == 0)
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null && element.GetEntity(existing).IsValid()) element.DeleteEntity(existing);
                return;
            }

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(FieldName, ParkedParameterCodec.Encode(entries));
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
