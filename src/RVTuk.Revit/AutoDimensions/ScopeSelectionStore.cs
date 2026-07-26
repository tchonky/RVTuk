using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Per-project memory of the scope pane's last successful run — the checked category mask and
    /// the checked view ids — on the document's ProjectInformation element. Written in the same
    /// transaction as the run itself, never on a checkbox toggle: a selection the user never ran
    /// isn't worth a transaction, and the persisted state should mean "what was last built".
    ///
    /// The view ids live in one string field (see IdListCodec for why, not an array field).
    /// </summary>
    public static class ScopeSelectionStore
    {
        // Bumped when the opening-reach field was added: a Schema is immutable once registered
        // in a session, so a new field means a new guid. Any selection saved under the previous
        // schema is simply orphaned, and the pane falls back to its defaults once.
        private static readonly Guid SchemaGuid = new Guid("a1e93f47-8b25-4c6e-9d03-2f7a5c18be64");
        private const string SchemaName = "RVTukAutoDimensionsScopeSelectionV2";
        private const string CategoryMaskField = "CategoryMask";
        private const string ViewIdsField = "CheckedViewIds";
        private const string OpeningReachField = "OpeningReachMillimetres";

        public static ScopeSelection? Read(Document doc)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return null;

            var entity = projectInfo.GetEntity(schema);
            if (!entity.IsValid()) return null;

            var reach = entity.Get<int>(OpeningReachField);
            return new ScopeSelection(
                entity.Get<int>(CategoryMaskField),
                IdListCodec.Decode(entity.Get<string>(ViewIdsField)),
                reach > 0 ? reach : ScopeDefaults.OpeningReachMillimetres);
        }

        /// <summary>Must be called inside an open transaction.</summary>
        public static void Write(
            Document doc,
            int categoryMask,
            IReadOnlyList<long> checkedViewIds,
            int openingReachMillimetres)
        {
            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return;

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(CategoryMaskField, categoryMask);
            entity.Set(ViewIdsField, IdListCodec.Encode(checkedViewIds));
            entity.Set(OpeningReachField, openingReachMillimetres);
            projectInfo.SetEntity(entity);
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
            // Millimetres as a plain int: a double field would need a unit spec, and this is a
            // drafting setting the user types, not a measured length.
            builder.AddSimpleField(OpeningReachField, typeof(int));
            return builder.Finish();
        }
    }
}
