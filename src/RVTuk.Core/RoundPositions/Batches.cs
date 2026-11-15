using System;
using System.Collections.Generic;

namespace RVTuk.Core.RoundPositions
{
    public static class Batches
    {
        /// <summary>
        /// A guess, not a measurement. Revit regenerates once per sub-transaction commit, so a
        /// bigger batch means fewer regenerations — but a batch that fails is re-run element by
        /// element, so a bigger batch also makes the fallback more expensive. The first in-Revit
        /// timing decides whether this moves.
        /// </summary>
        public const int DefaultSize = 250;

        public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IReadOnlyList<T> items, int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

            var batches = new List<IReadOnlyList<T>>();
            for (var start = 0; start < items.Count; start += size)
            {
                var take = Math.Min(size, items.Count - start);
                var batch = new List<T>(take);
                for (var i = 0; i < take; i++) batch.Add(items[start + i]);
                batches.Add(batch);
            }

            return batches;
        }
    }
}
