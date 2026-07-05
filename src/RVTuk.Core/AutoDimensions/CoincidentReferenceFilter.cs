using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Decides which dimension references to keep when several sit at the same station along
    /// the line (e.g. two joined walls sharing a face plane), which produces zero-length
    /// segments. The first reference at each station survives; smarter selection is a future
    /// refinement.
    /// </summary>
    public static class CoincidentReferenceFilter
    {
        /// <summary>
        /// Given the ordered segment values of a dimension over N references
        /// (segmentValues[i] = distance between reference i and reference i+1, so
        /// N = segmentValues.Count + 1), returns the indices of the references to keep:
        /// index 0, plus every reference whose preceding segment is longer than the tolerance.
        /// </summary>
        public static IReadOnlyList<int> KeepIndices(
            IReadOnlyList<double> segmentValues, double tolerance)
        {
            var keep = new List<int> { 0 };
            for (int i = 0; i < segmentValues.Count; i++)
            {
                if (segmentValues[i] > tolerance)
                    keep.Add(i + 1);
            }
            return keep;
        }
    }
}
