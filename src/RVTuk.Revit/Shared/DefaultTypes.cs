using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// The type Revit's own tools would place next. Shared because Room Floor and the Topo Tools
    /// converter both create elements of "the type you last used" rather than asking.
    /// </summary>
    public static class DefaultTypes
    {
        /// <summary>The project's default floor type, else its first non-foundation floor type;
        /// <see cref="ElementId.InvalidElementId"/> when it has neither.</summary>
        public static ElementId FloorTypeId(Document doc)
        {
            var id = doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType);
            if (id != ElementId.InvalidElementId && doc.GetElement(id) is FloorType) return id;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .Where(t => !t.IsFoundationSlab)
                .Select(t => t.Id)
                .FirstOrDefault() ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Toposolids have no <see cref="ElementTypeGroup"/>, so this asks for the per-category
        /// default Revit keeps for family types and takes it only if it is a toposolid type.
        /// Whether Revit keeps one for this system category is unverified, and is the first in-Revit
        /// check. The fallback, the first toposolid type by name, keeps the tool working either way.
        /// </summary>
        public static ElementId ToposolidTypeId(Document doc)
        {
            try
            {
                var id = doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_Toposolid));
                if (id != null && id != ElementId.InvalidElementId && doc.GetElement(id) is ToposolidType)
                    return id;
            }
            catch (Exception)
            {
                // Not answered for this category; fall through to the first type by name.
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ToposolidType))
                .Cast<ToposolidType>()
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => t.Id)
                .FirstOrDefault() ?? ElementId.InvalidElementId;
        }
    }
}
