using RVTuk.Core.AreaSubmission;

namespace RVTuk.UI.ViewModels
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

        /// <summary>Human-readable one-line label: "«number» — «name» [code]"; a permit-history
        /// code (USAGE_TYPE_OLD) shows as "[301←1]" so unexpected template data is visible.</summary>
        public string Display
        {
            get
            {
                var head = string.IsNullOrWhiteSpace(Number) ? "(no #)" : Number!;
                var name = string.IsNullOrWhiteSpace(Name) ? "" : " — " + Name;
                var code = UsageCode.HasValue || UsageCodePrev.HasValue
                    ? "  [" + (UsageCode?.ToString() ?? "–") + (UsageCodePrev.HasValue ? "←" + UsageCodePrev : "") + "]"
                    : "  [no code]";
                return head + name + code;
            }
        }

        /// <summary>Comma-joined reason(s) this row is flagged, for a tooltip.</summary>
        public string? ErrorSummary => HasError ? Record.Errors.ToString() : null;
    }
}
