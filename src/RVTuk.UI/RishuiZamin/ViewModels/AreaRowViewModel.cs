using RVTuk.Core.RishuiZamin;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    /// <summary>One Area row in the submission tree. Wraps the Core <see cref="AreaRecord"/>
    /// plus the Revit element id used to select it in the model.</summary>
    public class AreaRowViewModel
    {
        public long ElementId { get; }
        public AreaRecord Record { get; }

        public AreaRowViewModel(long elementId, AreaRecord record)
        {
            ElementId = elementId;
            Record = record;
        }

        public string? Number => Record.Number;
        public string? Name => Record.Name;
        public int? UsageCode => Record.UsageCode;
        public int? UsageCodePrev => Record.UsageCodePrev;
        public bool HasError => Record.Errors != AreaError.None;

        /// <summary>Left-hand label: "«number» — «name»" (or "(no #)" when the area has no
        /// Number set).</summary>
        public string NumberAndName
        {
            get
            {
                var head = string.IsNullOrWhiteSpace(Number) ? "(no #)" : Number!;
                var name = string.IsNullOrWhiteSpace(Name) ? "" : " — " + Name;
                return head + name;
            }
        }

        /// <summary>Right-hand tag: the usage code, with a permit-history code
        /// (USAGE_TYPE_OLD) shown as "301←1" so unexpected template data is visible, or
        /// "⚠ no code" when neither is set.</summary>
        public string Tag => UsageCode.HasValue || UsageCodePrev.HasValue
            ? (UsageCode?.ToString() ?? "–") + (UsageCodePrev.HasValue ? "←" + UsageCodePrev : "")
            : "⚠ no code";

        /// <summary>Comma-joined reason(s) this row is flagged, for a tooltip.</summary>
        public string? ErrorSummary => HasError ? Record.Errors.ToString() : null;
    }
}
