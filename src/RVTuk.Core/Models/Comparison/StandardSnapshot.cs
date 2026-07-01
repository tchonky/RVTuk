using System.Collections.Generic;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>The editable, curated master "Standard": a mutable snapshot built by accepting
    /// items from project snapshots. Editing it never touches a Revit model.</summary>
    public class StandardSnapshot
    {
        public SnapshotMeta Meta { get; set; } = new SnapshotMeta { SourceKind = "Standard", IsMutable = true };
        public List<CategorySnapshot> Categories { get; set; } = new List<CategorySnapshot>();

        /// <summary>Per-item record of where each accepted item came from.</summary>
        public List<ItemProvenance> Provenance { get; set; } = new List<ItemProvenance>();
    }
}
