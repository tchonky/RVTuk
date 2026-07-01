using RVTuk.Core.Models.Comparison;

namespace RVTuk.UI.ViewModels
{
    public class FieldDiffViewModel : ViewModelBase
    {
        private readonly FieldDiff _field;

        public FieldDiffViewModel(FieldDiff field)
        {
            _field = field;
        }

        public string Label => _field.Label;
        public string ValueA => _field.ValueA ?? "—";
        public string ValueB => _field.ValueB ?? "—";
        public bool IsEqual => _field.IsEqual;
        public string Status => _field.IsEqual ? "≈" : "△";
    }
}
