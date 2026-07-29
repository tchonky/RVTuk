using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The project's linear dimension types, for the pane's dropdown — linear being the only
    /// style NewDimension produces.
    ///
    /// Falls back to every dimension type when that filter finds none: an empty dropdown would
    /// leave the user with no way to run at all, which is a worse failure than offering a type
    /// the run may not be able to use.
    /// </summary>
    public static class DimensionTypeFinder
    {
        public static IReadOnlyList<DimensionTypeInfo> Find(Document doc)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .ToList();

            var linear = all.Where(IsLinear).ToList();

            return (linear.Count > 0 ? linear : all)
                .Select(t => new DimensionTypeInfo(t.Id.Value, t.Name))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static bool IsLinear(DimensionType type)
        {
            try
            {
                return type.StyleType == DimensionStyleType.Linear;
            }
            catch
            {
                // A type whose style Revit will not report is not one to offer.
                return false;
            }
        }
    }
}
