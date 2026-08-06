using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Releases whatever is holding an element in place, but only after a move has already been
    /// refused — never pre-emptively. A locked dimension is unlocked rather than deleted, so the
    /// annotation and its design intent survive and only the constraint goes.
    /// </summary>
    public static class ConstraintReleaser
    {
        /// <summary>
        /// Returns what it released and records nothing: this runs inside a sub-transaction, and a
        /// roll-back would leave the report naming constraints that are still in the model. The
        /// caller records once the commit has succeeded.
        /// </summary>
        public static IReadOnlyList<ReleasedConstraint> Release(Document doc, Element element)
        {
            var category = ElementEligibility.CategoryName(element);
            var released = new List<ReleasedConstraint>();

            foreach (var id in DependentIds(element, BuiltInCategory.OST_Dimensions))
            {
                if (!(doc.GetElement(id) is Dimension dimension) || !dimension.IsLocked) continue;

                dimension.IsLocked = false;
                released.Add(new ReleasedConstraint(
                    element.Id.Value, category, $"Unlocked dimension {id.Value}"));
            }

            var alignmentIds = DependentIds(element, BuiltInCategory.OST_Constraints);
            if (alignmentIds.Count > 0)
            {
                // An alignment lock exists only to constrain, so there is nothing to preserve by
                // keeping it. Revit can delete fewer ids than asked for, so the report follows what
                // came back rather than what went in.
                var deletedIds = new HashSet<long>(doc.Delete(alignmentIds).Select(id => id.Value));

                foreach (var id in alignmentIds.Where(id => deletedIds.Contains(id.Value)))
                {
                    released.Add(new ReleasedConstraint(
                        element.Id.Value, category, $"Deleted alignment constraint {id.Value}"));
                }
            }

            return released;
        }

        private static IList<ElementId> DependentIds(Element element, BuiltInCategory category) =>
            element.GetDependentElements(new ElementCategoryFilter(category));
    }
}
