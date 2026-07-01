using System.Collections.ObjectModel;
using System.Linq;

namespace RVTuk.UI.ViewModels
{
    /// <summary>A collapsible group of areas on one level, shown in the submission tree.</summary>
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
    }
}
