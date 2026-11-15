using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// A bulk fix raises hundreds of "elements are joined but do not intersect"-class warnings.
    /// Without this every one of them stops the run with a dialog.
    ///
    /// But a Revit warning is a notification, not a question — the operation proceeds either way.
    /// So suppressing one whose consequence is "this element will be deleted" does not prevent the
    /// deletion, it hides it. This tool rounds positions; it must never destroy an element as a
    /// side effect, and a window silently deleted because its wall moved 2 mm is exactly the
    /// damage it is supposed to prevent.
    /// </summary>
    public class RoundingFailurePreprocessor : IFailuresPreprocessor
    {
        /// <summary>Element ids Revit proposed to delete, from the run that was rolled back.
        /// Read by the runner so the user is told which elements were at risk, not just that
        /// something failed.</summary>
        public ElementId[] ElementsAtRisk { get; private set; } = new ElementId[0];

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();

            // Checked before anything is dismissed: once a warning is deleted its resolution is
            // no longer inspectable.
            var destructive = messages
                .Where(m => m.HasResolutionOfType(FailureResolutionType.DeleteElements))
                .ToList();

            if (destructive.Count > 0)
            {
                ElementsAtRisk = destructive
                    .SelectMany(m => m.GetFailingElementIds())
                    .Distinct()
                    .ToArray();

                // Roll the whole fix back rather than let Revit delete anything. Blunt — one bad
                // wall costs the entire fix — but the alternative is destroying model content to
                // save a rerun.
                return FailureProcessingResult.ProceedWithRollBack;
            }

            foreach (var failure in messages)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failure);
                }
            }

            // Errors are left for the caller: the sub-transaction that raised one gets rolled
            // back, which is how an element ends up recorded as skipped rather than half-moved.
            return FailureProcessingResult.Continue;
        }
    }
}
