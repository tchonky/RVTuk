using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The text type ("_DP-Topo Height") that marks a text note as a topo line's height. Created
    /// from the document's default text type with an opaque background, so a text centred on its
    /// line masks the line under it, like a contour label.
    /// </summary>
    public static class HeightTextType
    {
        public const string Name = "_DP-Topo Height";

        public static bool IsHeightText(TextNote text) => text.TextNoteType?.Name == Name;

        /// <summary>
        /// Returns the type's id, creating it first if needed. An existing type is left as the user
        /// set it. Caller must already be inside a transaction.
        /// </summary>
        public static ElementId EnsureExists(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstOrDefault(type => type.Name == Name);
            if (existing != null) return existing.Id;

            var source = doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType
                ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();

            var created = (TextNoteType)source.Duplicate(Name);
            created.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)
                ?.Set((int)TextElementBackground.TBGR_OPAQUE);
            return created.Id;
        }
    }
}
