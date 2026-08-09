using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;
using RVTuk.Revit.RoundPositions.Readers;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Every category the tool handles, in the dependency order the tree and Fix all both use.
    /// Adding a category is one entry here plus a reader — nothing else changes.
    /// </summary>
    public static class PassRegistry
    {
        public static IReadOnlyList<CategoryPass> All { get; } = new List<CategoryPass>
        {
            new CategoryPass(FixTier.Walls, "Walls",
                doc => OfCategory(doc, BuiltInCategory.OST_Walls), LineReader.Read),
        };

        internal static IReadOnlyList<Element> OfCategory(Document doc, BuiltInCategory category) =>
            new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToList();

        internal static IReadOnlyList<Element> OfClass(Document doc, System.Type type) =>
            new FilteredElementCollector(doc)
                .OfClass(type)
                .WhereElementIsNotElementType()
                .ToList();
    }
}
