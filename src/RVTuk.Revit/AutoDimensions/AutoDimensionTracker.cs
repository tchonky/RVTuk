using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Tracks, per reference line, the Dimension that line last produced *in each target view* —
    /// via Extensible Storage (invisible to the user, travels with the line through copy/move).
    /// One line now fans out to several views, so the entry is a target-view-id → dimension-id
    /// map rather than a single id.
    ///
    /// Validates the tracked dimension still exists, is a Dimension, and belongs to the view being
    /// processed before ever handing it back for deletion (guards a line copied to another view
    /// from deleting that other view's legitimate dimension).
    ///
    /// The map is stored as one string field: Extensible Storage does not document a 64-bit
    /// integer field or map-key type, and Revit 2024+ element ids are 64-bit.
    /// </summary>
    public static class AutoDimensionTracker
    {
        // New guid: the field layout changed from a single id to a map. The pre-scope-pane schema
        // (2f1c9b6e-…) is abandoned — the tool never shipped, so no model needs migrating.
        private static readonly Guid SchemaGuid = new Guid("7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4");
        private const string SchemaName = "RVTukAutoDimensionTrackingByView";
        private const string FieldName = "DimensionIdsByViewId";

        public static Dimension? TryGetTrackedDimension(Element line, ElementId targetViewId)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var entity = line.GetEntity(schema);
            if (!entity.IsValid()) return null;

            var map = IdMapCodec.Decode(entity.Get<string>(FieldName));
            if (!map.TryGetValue(targetViewId.Value, out var dimensionIdValue)) return null;

            if (line.Document.GetElement(new ElementId(dimensionIdValue)) is not Dimension dimension) return null;
            if (dimension.OwnerViewId != targetViewId) return null;

            return dimension;
        }

        /// <summary>
        /// Overwrites this line's entry for one target view, leaving its entries for other views
        /// alone. Stale entries (a view or dimension since deleted) are harmless — reads validate
        /// before returning — and self-heal the next time that view is processed.
        /// </summary>
        public static void SetTrackedDimension(Element line, ElementId targetViewId, ElementId dimensionId)
        {
            var schema = GetOrCreateSchema();

            var map = new Dictionary<long, long>();
            var existing = line.GetEntity(schema);
            if (existing.IsValid())
            {
                foreach (var pair in IdMapCodec.Decode(existing.Get<string>(FieldName)))
                    map[pair.Key] = pair.Value;
            }
            map[targetViewId.Value] = dimensionId.Value;

            var entity = new Entity(schema);
            entity.Set(FieldName, IdMapCodec.Encode(map));
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
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }
    }
}
