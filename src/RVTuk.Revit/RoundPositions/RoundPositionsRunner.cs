using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    public static class RoundPositionsRunner
    {
        /// <summary>
        /// Read-only: opens no transaction and triggers no regeneration, so it is cheap enough to
        /// re-run after every fix.
        /// </summary>
        public static ScanResult Scan(
            Document doc, bool allowHalfCentimetres,
            System.Threading.CancellationToken cancellation = default)
        {
            var step = GridSnapper.StepFor(allowHalfCentimetres);
            var builder = new ScanResultBuilder();

            foreach (var pass in PassRegistry.All)
            {
                if (cancellation.IsCancellationRequested) break;
                pass.Scan(doc, step, builder, cancellation);
            }

            return builder.Build();
        }

        /// <summary>
        /// One transaction, so a fix is one Ctrl+Z. Passes run in tier order because each tier can
        /// move what sits below it — a level carries its whole floor, a wall drags its openings.
        /// </summary>
        public static RoundingReport Fix(
            Document doc, bool allowHalfCentimetres, ISet<long> ids,
            System.Threading.CancellationToken cancellation = default)
        {
            var step = GridSnapper.StepFor(allowHalfCentimetres);
            var report = new RoundingReportBuilder();
            var stopwatch = Stopwatch.StartNew();
            TransactionStatus status;
            RoundingFailurePreprocessor? preprocessor = null;

            using (var transaction = new Transaction(doc, "Round Positions"))
            {
                transaction.Start();

                var failures = new RoundingFailurePreprocessor();
                var options = transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(false);
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(failures);
                transaction.SetFailureHandlingOptions(options);
                preprocessor = failures;

                foreach (var pass in PassRegistry.All)
                {
                    if (cancellation.IsCancellationRequested) break;
                    pass.Apply(doc, step, ids, Batches.DefaultSize, report, cancellation);
                }

                status = transaction.Commit();
            }

            stopwatch.Stop();

            // Revit can still roll the whole transaction back at commit time — an error it signalled
            // by posting rather than throwing lets its sub-transaction commit, so the report by then
            // already counts moves and released constraints. Returning it would claim work on a model
            // that is byte-identical to before the fix, and the constraint list is the only record
            // those constraints ever existed.
            if (status != TransactionStatus.Committed)
            {
                var atRisk = preprocessor?.ElementsAtRisk ?? new ElementId[0];
                if (atRisk.Length > 0)
                {
                    var shown = string.Join(", ", atRisk.Take(10).Select(id => id.Value.ToString()));
                    var more = atRisk.Length > 10 ? $" and {atRisk.Length - 10} more" : string.Empty;
                    throw new InvalidOperationException(
                        $"Stopped: moving these elements would have made Revit delete {atRisk.Length} " +
                        "other element(s), so the whole fix was rolled back and nothing changed. " +
                        "This is usually a window or door that would be left outside its wall. " +
                        $"Element id(s) at risk: {shown}{more}. Fix a smaller group to find the wall " +
                        "responsible, and leave that one for now.");
                }

                throw new InvalidOperationException(
                    "Revit rolled the whole fix back, so nothing in the model changed: no elements " +
                    "were moved and no alignment constraints were deleted. This happens when Revit " +
                    "cannot accept one of the moves — resolve what Revit reported, then try again.");
            }

            return report.Build() with { ElapsedMilliseconds = stopwatch.ElapsedMilliseconds };
        }
    }
}
