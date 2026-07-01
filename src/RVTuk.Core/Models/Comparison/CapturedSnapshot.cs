using System.Collections.Generic;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Transport for a freshly captured (or loaded) snapshot handed from the Revit layer
    /// to the UI. Carries the meta + per-category snapshots, or an error message.</summary>
    public class CapturedSnapshot
    {
        public SnapshotMeta Meta { get; set; } = new SnapshotMeta();
        public List<CategorySnapshot> Categories { get; set; } = new List<CategorySnapshot>();
        public string? Error { get; set; }
    }
}
