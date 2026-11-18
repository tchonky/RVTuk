using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The text type ("_DP-Topo Height") that marks a text note as a topo line's height. Created
    /// from the document's default text type with an opaque background, so a text centred on its
    /// line masks the line under it, like a contour label, and in the lines' orange
    /// (<see cref="TopoLineStyle.Colour"/>).
    ///
    /// The type is found by this name and nothing else. Renaming it in the project disconnects
    /// every height text already drawn: they stop counting as height texts, so the next session
    /// creates a new type and gives every line a second text at the last typed height, which the
    /// next Update then applies instead of the heights typed before.
    /// </summary>
    public static class HeightTextType
    {
        /// <summary>The type's name, and the only way it is found: renaming the type in the project
        /// disconnects every height text already drawn.</summary>
        public const string Name = "_DP-Topo Height";

        public static bool IsHeightText(TextNote text) => text.TextNoteType?.Name == Name;

        /// <summary>True when the document already has the type. Reads only.</summary>
        public static bool Exists(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Any(type => type.Name == Name);

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
            created.get_Parameter(BuiltInParameter.LINE_COLOR)
                ?.Set(ColourInteger(TopoLineStyle.Colour));
            return created.Id;
        }

        /// <summary>A colour as the integer a type's Color parameter holds: R + G·256 + B·65536.</summary>
        private static int ColourInteger(Color colour) =>
            colour.Red + colour.Green * 256 + colour.Blue * 65536;
    }
}
