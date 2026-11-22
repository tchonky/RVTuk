using System.Collections.ObjectModel;
using System.Linq;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    /// <summary>The areas on one level, shown as a group in the Areas list.</summary>
    public class AreaLevelGroupViewModel
    {
        public string LevelName { get; }
        public ObservableCollection<AreaRowViewModel> Rows { get; } = new();

        public AreaLevelGroupViewModel(string levelName)
        {
            LevelName = string.IsNullOrWhiteSpace(levelName) ? "(no level)" : levelName;
        }

        /// <summary>True if any row in this level is flagged — lets the group header show a marker.</summary>
        public bool HasError => Rows.Any(r => r.HasError);

        public string Header => $"{LevelName}  ({Rows.Count})";

        /// <summary>m² per usage group on this level.</summary>
        public UsageTotals Totals => UsageTotals.Sum(Rows.Select(r => r.Record));
    }
}
