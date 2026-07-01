using System.Collections.Generic;

namespace RVTuk.Core.Models.Comparison
{
    public class CategoryDiffResult
    {
        public string CategoryId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<ItemDiff> Items { get; set; } = new List<ItemDiff>();
        public DiffSummary Summary { get; set; } = new DiffSummary();
    }
}
