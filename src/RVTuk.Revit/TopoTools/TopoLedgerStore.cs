using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Per toposolid: which points each topo line owns on it. Invisible to the user and travelling
    /// with the element.
    ///
    /// Deliberately stored on the <b>toposolid</b>, not on the line as AutoDimensionTracker does.
    /// Storage on a line dies with the line, orphaning its points forever; on the toposolid, a run
    /// sees a recorded line id that no longer resolves and cleans up after it.
    /// </summary>
    public static class TopoLedgerStore
    {
        private static readonly Guid SchemaGuid = new Guid("3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62");
        private const string SchemaName = "RVTukTopoToolsPointLedger";
        private const string FieldName = "PointsByLineId";

        public static IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> Read(Element toposolid)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return new Dictionary<long, IReadOnlyList<XyzPoint>>();

            var entity = toposolid.GetEntity(schema);
            if (!entity.IsValid()) return new Dictionary<long, IReadOnlyList<XyzPoint>>();

            return TopoPointLedgerCodec.Decode(entity.Get<string>(FieldName));
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void Write(
            Element toposolid, IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger)
        {
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(FieldName, TopoPointLedgerCodec.Encode(ledger));
            toposolid.SetEntity(entity);
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
