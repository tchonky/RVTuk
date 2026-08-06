using Autodesk.Revit.DB;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// A model-wide run raises hundreds of "elements are joined but do not intersect"-class
    /// warnings. Without this every one of them stops the run with a dialog.
    /// </summary>
    public class RoundingFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
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
