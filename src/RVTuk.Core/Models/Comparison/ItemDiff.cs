using System.Collections.Generic;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>The diff for one matched (or one-sided) item, e.g. a view template.</summary>
    public class ItemDiff
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DiffKind Kind { get; set; }
        public List<FieldDiff> Fields { get; set; } = new List<FieldDiff>();

        /// <summary>Completeness score in [0,1] for each side (see CompletenessScorer).</summary>
        public double CompletenessA { get; set; }
        public double CompletenessB { get; set; }

        public ItemProvenance? Provenance { get; set; }

        /// <summary>Write-back seam. Always null in v1 (report-only).</summary>
        public string? FutureApplyToken { get; set; }
    }
}
