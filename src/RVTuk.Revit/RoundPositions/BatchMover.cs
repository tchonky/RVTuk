using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Optimistic batch, pessimistic retry. Revit regenerates when a sub-transaction commits, not
    /// on each API call, so moving a whole batch inside one sub-transaction costs one regeneration
    /// instead of one per element — which is the entire difference between this tool being usable
    /// and not. A batch that throws is rolled back and re-run element by element, which restores
    /// exact per-element attribution and pays the slow path only where the trouble actually is.
    /// </summary>
    public static class BatchMover
    {
        public static int Move(
            Document doc,
            IReadOnlyList<(Element Element, XYZ Translation)> moves,
            int batchSize,
            RoundingReportBuilder report)
        {
            var moved = 0;

            foreach (var batch in Batches.Split(moves, batchSize))
            {
                if (TryWholeBatch(doc, batch))
                {
                    moved += batch.Count;
                    continue;
                }

                foreach (var move in batch)
                {
                    if (MoveAttempt.TryMove(doc, move.Element, move.Translation, report))
                    {
                        moved++;
                    }
                    else
                    {
                        report.RecordSkipped(
                            move.Element.Id.Value,
                            ElementEligibility.CategoryName(move.Element),
                            SkipReason.RevitRejected);
                    }
                }
            }

            return moved;
        }

        private static bool TryWholeBatch(
            Document doc, IReadOnlyList<(Element Element, XYZ Translation)> batch)
        {
            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    foreach (var move in batch)
                    {
                        // Unpinning inside the batch is safe: a rollback restores every pin along
                        // with every position, so a failed batch leaves nothing half-done.
                        var wasPinned = move.Element.Pinned;
                        if (wasPinned) move.Element.Pinned = false;
                        ElementTransformUtils.MoveElement(doc, move.Element.Id, move.Translation);
                        if (wasPinned) move.Element.Pinned = true;
                    }

                    sub.Commit();
                    return true;
                }
                catch (Exception)
                {
                    sub.RollBack();
                    return false;
                }
            }
        }
    }
}
