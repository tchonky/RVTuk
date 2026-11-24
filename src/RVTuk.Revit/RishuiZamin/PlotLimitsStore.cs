using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin
{
    /// <summary>
    /// The Area Calc Table tab's plot area and allowed % of plot, kept on the project's
    /// Project Information element so each project keeps its own plan's numbers. Four double
    /// fields; a negative value means "not filled in".
    /// </summary>
    public static class PlotLimitsStore
    {
        private static readonly Guid SchemaGuid = new Guid("e6d292f7-e823-45c0-a5a6-b95108884f31");
        private const string SchemaName = "RVTukRishuiZaminPlotLimits";
        private const string PlotField = "PlotAreaM2";
        private const string PrimaryField = "PrimaryPercent";
        private const string ServiceField = "ServicePercent";
        private const string TotalField = "TotalPercent";

        /// <summary>The stored values, or <see cref="PlotLimits.Empty"/> when there are none.</summary>
        public static PlotLimits Read(Document doc)
        {
            var schema = Schema.Lookup(SchemaGuid);
            var info = doc.ProjectInformation;
            if (schema == null || info == null) return PlotLimits.Empty;
            var entity = info.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return PlotLimits.Empty;
            return new PlotLimits(Get(entity, PlotField), Get(entity, PrimaryField), Get(entity, ServiceField), Get(entity, TotalField));
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void Write(Document doc, PlotLimits limits)
        {
            var entity = new Entity(GetOrCreateSchema());
            Set(entity, PlotField, limits.PlotAreaM2);
            Set(entity, PrimaryField, limits.PrimaryPercent);
            Set(entity, ServiceField, limits.ServicePercent);
            Set(entity, TotalField, limits.TotalPercent);
            doc.ProjectInformation.SetEntity(entity);
        }

        private static double? Get(Entity e, string field)
        {
            var v = e.Get<double>(field, UnitTypeId.General);
            return v < 0 ? null : v;
        }

        private static void Set(Entity e, string field, double? value) =>
            e.Set(field, value ?? -1.0, UnitTypeId.General);

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId("KnafoKlimor");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            foreach (var name in new[] { PlotField, PrimaryField, ServiceField, TotalField })
                builder.AddSimpleField(name, typeof(double)).SetSpec(SpecTypeId.Number);
            return builder.Finish();
        }
    }
}
