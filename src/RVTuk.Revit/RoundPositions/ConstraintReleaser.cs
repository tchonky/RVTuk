using System.Collections.Generic;
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
        public static bool Release(Document doc, Element element, RoundingReportBuilder report)
        {
            var category = ElementEligibility.CategoryName(element);
            var released = false;

            foreach (var id in DependentIds(element, BuiltInCategory.OST_Dimensions))
            {
                if (!(doc.GetElement(id) is Dimension dimension) || !dimension.IsLocked) continue;

                dimension.IsLocked = false;
                report.RecordReleasedConstraint(
                    element.Id.Value, category, $"Unlocked dimension {id.Value}");
                released = true;
            }

            var alignmentIds = DependentIds(element, BuiltInCategory.OST_Constraints);
            foreach (var id in alignmentIds)
            {
                // An alignment lock exists only to constrain, so there is nothing to preserve by
                // keeping it.
                report.RecordReleasedConstraint(
                    element.Id.Value, category, $"Deleted alignment constraint {id.Value}");
                released = true;
            }
            if (alignmentIds.Count > 0) doc.Delete(alignmentIds);

            return released;
        }

        private static IList<ElementId> DependentIds(Element element, BuiltInCategory category) =>
            element.GetDependentElements(new ElementCategoryFilter(category));
    }
}
