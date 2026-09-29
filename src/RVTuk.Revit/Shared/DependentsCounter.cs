using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// What deleting an element would silently delete with it. Room Floor refuses to replace a
    /// floor that has any; the Topo Tools converter asks first. Shared for that reason.
    /// </summary>
    public static class DependentsCounter
    {
        /// <summary>Family instances, openings and hosted sweeps (e.g. slab edges).</summary>
        public static int Hosted(Element element)
        {
            var filter = new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementClassFilter(typeof(FamilyInstance)),
                new ElementClassFilter(typeof(Opening)),
                new ElementClassFilter(typeof(HostedSweep)),
            });
            return element.GetDependentElements(filter).Count;
        }

        /// <summary>Tags and dimensions (e.g. floor tags, spot elevations).</summary>
        public static int Annotations(Element element)
        {
            var filter = new LogicalOrFilter(
                new ElementClassFilter(typeof(IndependentTag)),
                new ElementClassFilter(typeof(Dimension)));
            return element.GetDependentElements(filter).Count;
        }

        /// <summary>A toposolid's subdivisions; 0 for anything else.</summary>
        public static int Subdivisions(Element element) =>
            element is Toposolid toposolid ? toposolid.GetSubDivisionIds().Count : 0;
    }
}
