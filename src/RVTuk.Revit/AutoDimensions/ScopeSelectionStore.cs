using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Per-project memory of the scope pane's last successful run — the checked category mask,
    /// the checked view ids and the chosen dimension type — on the document's ProjectInformation
    /// element. Written in the same transaction as the run itself, never on a checkbox toggle: a
    /// selection the user never ran isn't worth a transaction, and the persisted state should
    /// mean "what was last built".
    ///
    /// Ids live in string fields (see IdListCodec for why, not array fields; extensible storage
    /// has no long field at all, which settles it for the dimension type).
    /// </summary>
    public static class ScopeSelectionStore
    {
        // Bumped when the opening-reach field gave way to the dimension type: a Schema is
        // immutable once registered in a session, so a changed field means a new guid. Any
        // selection saved under the previous schema is simply orphaned, and the pane falls back
        // to its defaults once.
        private static readonly Guid SchemaGuid = new Guid("6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51");
        private const string SchemaName = "RVTukAutoDimensionsScopeSelectionV3";
        private const string CategoryMaskField = "CategoryMask";
        private const string ViewIdsField = "CheckedViewIds";
        private const string DimensionTypeIdField = "DimensionTypeId";

        public static ScopeSelection? Read(Document doc)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return null;

            var entity = projectInfo.GetEntity(schema);
            if (!entity.IsValid()) return null;

            return new ScopeSelection(
                entity.Get<int>(CategoryMaskField),
                IdListCodec.Decode(entity.Get<string>(ViewIdsField)),
                ParseId(entity.Get<string>(DimensionTypeIdField)));
        }

        /// <summary>Must be called inside an open transaction.</summary>
        public static void Write(
            Document doc,
            int categoryMask,
            IReadOnlyList<long> checkedViewIds,
            long dimensionTypeId)
        {
            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return;

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(CategoryMaskField, categoryMask);
            entity.Set(ViewIdsField, IdListCodec.Encode(checkedViewIds));
            entity.Set(
                DimensionTypeIdField,
                dimensionTypeId.ToString(CultureInfo.InvariantCulture));
            projectInfo.SetEntity(entity);
        }

        /// <summary>Zero for anything unreadable — the pane reads that as "nothing chosen".</summary>
        private static long ParseId(string? stored)
        {
            return long.TryParse(
                stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0;
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
            builder.AddSimpleField(CategoryMaskField, typeof(int));
            builder.AddSimpleField(ViewIdsField, typeof(string));
            builder.AddSimpleField(DimensionTypeIdField, typeof(string));
            return builder.Finish();
        }
    }
}
