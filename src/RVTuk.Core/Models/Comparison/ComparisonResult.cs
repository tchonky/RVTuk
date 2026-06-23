using System.Collections.Generic;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Top-level comparison output surfaced to the UI and the HTML report.</summary>
    public class ComparisonResult
    {
        public SnapshotMeta SideA { get; set; } = new SnapshotMeta();
        public SnapshotMeta SideB { get; set; } = new SnapshotMeta();
        public List<CategoryDiffResult> Categories { get; set; } = new List<CategoryDiffResult>();
    }
}
