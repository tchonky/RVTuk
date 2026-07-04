using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Tracks, per reference line, the ElementId of the Dimension it last produced — via
    /// Extensible Storage (invisible to the user, travels with the line through copy/move).
    /// Validates the tracked dimension still exists, is a Dimension, and belongs to the active
    /// view before ever handing it back for deletion (guards a line copied to another view from
    /// deleting that other view's legitimate dimension).
    /// </summary>
    public static class AutoDimensionTracker
    {
        private static readonly Guid SchemaGuid = new Guid("2f1c9b6e-6b7d-4a6d-9c9a-8e6a1f6d9b2a");
        private const string SchemaName = "RVTukAutoDimensionTracking";
        private const string FieldName = "DimensionIdValue";

        public static Dimension? TryGetTrackedDimension(Element line, ElementId activeViewId)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var entity = line.GetEntity(schema);
            if (!entity.IsValid()) return null;

            var idValue = entity.Get<long>(FieldName);
            var elementId = new ElementId(idValue);

            if (line.Document.GetElement(elementId) is not Dimension dimension) return null;
            if (dimension.OwnerViewId != activeViewId) return null;

            return dimension;
        }

        public static void SetTrackedDimension(Element line, ElementId dimensionId)
        {
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(FieldName, dimensionId.Value);
            line.SetEntity(entity);
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
