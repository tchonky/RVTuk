using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// The one place an element is actually moved. A failure rolls its sub-transaction back —
    /// which restores the pin along with the position — then retries once after releasing
    /// whatever was holding it.
    /// </summary>
    public static class MoveAttempt
    {
        /// <summary>
        /// Applies one change to one element under the rules every pass shares: unpin first,
        /// re-pin after, roll back cleanly on failure, then retry once with whatever was holding
        /// the element released. <paramref name="apply"/> is the only part that differs between
        /// passes — a translation, a rotation, or writing a level's elevation.
        /// </summary>
        public static bool TryApply(
            Document doc, Element element, Action apply, RoundingReportBuilder report)
        {
            var wasPinned = element.Pinned;

            if (TryOnce(doc, element, apply, wasPinned)) return true;
            if (!TryRelease(doc, element, report)) return false;

            return TryOnce(doc, element, apply, wasPinned);
        }

        /// <summary>
        /// Releases what is holding the element and commits that on its own, before the retry.
        /// Keeping the two apart is what stops the report claiming a constraint was removed that
        /// a failed retry then rolled back — and the run is still one transaction, so Ctrl+Z
        /// takes the release with it either way.
        /// </summary>
        private static bool TryRelease(Document doc, Element element, RoundingReportBuilder report)
        {
            IReadOnlyList<ReleasedConstraint> released;

            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    released = ConstraintReleaser.Release(doc, element);
                    if (released.Count == 0)
                    {
                        sub.RollBack();
                        return false;
                    }

                    sub.Commit();
                }
                catch (Exception)
                {
                    sub.RollBack();
                    return false;
                }
            }

            foreach (var constraint in released)
            {
                report.RecordReleasedConstraint(
                    constraint.ElementId, constraint.Category, constraint.Description);
            }

            return true;
        }

        public static bool TryMove(
            Document doc, Element element, XYZ translation, RoundingReportBuilder report) =>
            TryApply(doc, element,
                () => ElementTransformUtils.MoveElement(doc, element.Id, translation), report);

        private static bool TryOnce(Document doc, Element element, Action apply, bool wasPinned)
        {
            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    if (wasPinned) element.Pinned = false;
                    apply();
                    if (wasPinned) element.Pinned = true;
                    sub.Commit();
                    return true;
                }
                catch (Exception)
                {
                    // Rolling back restores the pin along with the position.
                    sub.RollBack();
                    return false;
                }
            }
        }
    }
}
