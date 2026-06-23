using System.Collections.Generic;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Outcome of accepting an item into the Standard.</summary>
    public class MergeResult
    {
        public bool Applied { get; set; }
        /// <summary>Non-null when blocked, e.g. "exists" (name conflict not resolved).</summary>
        public string? Conflict { get; set; }
        /// <summary>Dependency names that did not resolve and need attention.</summary>
        public List<string> ClosureGaps { get; set; } = new List<string>();
    }
}
