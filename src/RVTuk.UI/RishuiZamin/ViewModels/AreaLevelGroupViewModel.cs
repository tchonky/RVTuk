using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

        /// <summary>The floor header's coloured figures: each group with m² on this floor, then
        /// "No code" when some area has none.</summary>
        public IReadOnlyList<UsageTotalPart> Parts
        {
            get
            {
                var t = Totals;
                var parts = new List<UsageTotalPart>();
                if (t.Primary != 0) parts.Add(new UsageTotalPart(UsageGroup.Primary, t.Primary));
                if (t.Service != 0) parts.Add(new UsageTotalPart(UsageGroup.Service, t.Service));
                if (t.Other != 0) parts.Add(new UsageTotalPart(UsageGroup.Other, t.Other));
                if (t.Process != 0) parts.Add(new UsageTotalPart(UsageGroup.Process, t.Process));
                if (t.NoCodeCount > 0) parts.Add(new UsageTotalPart(UsageGroup.NoCode, t.NoCode));
                return parts;
            }
        }

        /// <summary>"Σ «Primary + Service»".</summary>
        public string TotalText => "Σ " + Totals.Total.ToString("F2", CultureInfo.InvariantCulture);
    }
}
