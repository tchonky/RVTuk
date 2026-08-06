using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// One transaction for the whole run, so the cleanup is a single Ctrl+Z, with a
    /// sub-transaction per element that actually needs to move.
    /// </summary>
    public static class RoundPositionsRunner
    {
        public static RoundingReport Run(Document doc, bool allowHalfCentimetres)
        {
            var step = GridSnapper.StepFor(allowHalfCentimetres);
            var report = new RoundingReportBuilder();
            TransactionStatus status;

            using (var transaction = new Transaction(doc, "Round Positions"))
            {
                transaction.Start();

                var options = transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(false);
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(new RoundingFailurePreprocessor());
                transaction.SetFailureHandlingOptions(options);

                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_Walls), step, report);

                status = transaction.Commit();
            }

            // Revit can still roll the whole transaction back at commit time — an error it
            // signalled by posting rather than throwing lets its sub-transaction commit, so the
            // report by then already counts moves and released constraints. Returning it would
            // claim work on a model that is byte-identical to before the run, and the constraint
            // list is the only record those constraints ever existed.
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException(
                    "Revit rolled the whole run back, so nothing in the model changed: no elements " +
                    "were moved and no alignment constraints were deleted. This happens when Revit " +
                    "cannot accept one of the moves — resolve the warnings Revit reported, then run " +
                    "Round Positions again.");
            }

            return report.Build();
        }

        private static IReadOnlyList<Element> Collect(Document doc, BuiltInCategory category) =>
            new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToList();
    }
}
