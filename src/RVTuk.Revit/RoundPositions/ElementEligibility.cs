using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Why an element must be left alone, checked before anything is measured. Pinning is
    /// deliberately absent: a pinned element is unpinned, moved and re-pinned rather than skipped.
    /// </summary>
    public static class ElementEligibility
    {
        public static SkipReason? ReasonToSkip(Document doc, Element element)
        {
            if (element is RevitLinkInstance) return SkipReason.InRevitLink;
            if (element is ImportInstance) return SkipReason.ImportedCad;

            if (element.GroupId != ElementId.InvalidElementId) return SkipReason.InGroup;
            if (element.DesignOption != null) return SkipReason.InDesignOption;

            if (doc.IsWorkshared)
            {
                // NotOwned means nobody holds it, which is fine — only another user's checkout
                // actually blocks us.
                var status = WorksharingUtils.GetCheckoutStatus(doc, element.Id);
                if (status == CheckoutStatus.OwnedByOtherUser) return SkipReason.OwnedByOtherUser;
            }

            return null;
        }

        public static string CategoryName(Element element) =>
            element.Category?.Name ?? element.GetType().Name;
    }
}
