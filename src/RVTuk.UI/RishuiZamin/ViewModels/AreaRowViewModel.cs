using System.Globalization;
using RVTuk.Core.RishuiZamin;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    /// <summary>One Area row in the Areas list. Wraps the Core <see cref="AreaRecord"/>
    /// plus the Revit element id used to select it in the model.</summary>
    public class AreaRowViewModel : ViewModelBase
    {
        private bool _isSelected;

        public long ElementId { get; }
        public AreaRecord Record { get; }

        public AreaRowViewModel(long elementId, AreaRecord record)
        {
            ElementId = elementId;
            Record = record;
        }

        /// <summary>Bound two-way to the list item's selection.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>The level group this row is listed under (set when the list is filled).</summary>
        public AreaLevelGroupViewModel? Level { get; set; }

        public string? Number => Record.Number;
        public string? Name => Record.Name;
        public int? UsageCode => Record.UsageCode;
        public int? UsageCodePrev => Record.UsageCodePrev;
        public bool HasError => Record.Errors != AreaError.None;

        /// <summary>Left-hand label: "«number» — «name»" (or "(no #)" when the area has no
        /// Number set).</summary>
        public string NumberAndName => AreaSelectionSummary.NumberAndName(Number, Name);

        /// <summary>The number half of the row label ("002 —", or "(no #) —"), shown apart from
        /// <see cref="Name"/> so bidi never mixes the number into a Hebrew name.</summary>
        public string NumberLabel =>
            (string.IsNullOrWhiteSpace(Number) ? "(no #)" : Number!) + (string.IsNullOrWhiteSpace(Name) ? "" : " —");

        /// <summary>The area in m², 2 decimals, culture-independent ("84.30").</summary>
        public string AreaText => Record.AreaValue.ToString("F2", CultureInfo.InvariantCulture);

        /// <summary>The New usage half of the code pill, or null.</summary>
        public string? NewCodeText => UsageCode?.ToString(CultureInfo.InvariantCulture);

        /// <summary>The Old usage half of the code pill ("←1"), or null.</summary>
        public string? OldCodeText => UsageCodePrev == null
            ? null
            : "←" + UsageCodePrev.Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>True when the area has neither a New nor an Old usage code.</summary>
        public bool HasNoCode => UsageCode == null && UsageCodePrev == null;

        public UsageGroup NewGroup => UsageGroups.OfCode(UsageCode);
        public UsageGroup OldGroup => UsageGroups.OfCode(UsageCodePrev);

        /// <summary>The group the row is counted under (New, else Old, else No code).</summary>
        public UsageGroup Group => UsageGroups.Of(Record);

        /// <summary>The usage code, with a permit-history code (USAGE_TYPE_OLD) shown as
        /// "301←1", or "⚠ no code" when neither is set. Used for the tooltip.</summary>
        public string Tag => UsageCode.HasValue || UsageCodePrev.HasValue
            ? (UsageCode?.ToString() ?? "–") + (UsageCodePrev.HasValue ? "←" + UsageCodePrev : "")
            : "⚠ no code";

        /// <summary>Comma-joined reason(s) this row is flagged, for a tooltip.</summary>
        public string? ErrorSummary => HasError ? Record.Errors.ToString() : null;
    }
}
