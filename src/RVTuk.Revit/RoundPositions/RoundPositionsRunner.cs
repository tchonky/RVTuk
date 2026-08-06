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

            using (var transaction = new Transaction(doc, "Round Positions"))
            {
                transaction.Start();

                var options = transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(false);
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(new RoundingFailurePreprocessor());
                transaction.SetFailureHandlingOptions(options);

                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_Walls), step, report);

                transaction.Commit();
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
