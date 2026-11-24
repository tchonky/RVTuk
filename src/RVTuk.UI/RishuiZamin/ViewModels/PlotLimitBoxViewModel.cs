using System.Globalization;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    /// <summary>One of the Table tab's number boxes (plot area or an allowed %). <see cref="Value"/>
    /// is the parsed number (null when empty or invalid); <see cref="IsInvalid"/> marks text that
    /// <see cref="PlotLimits.TryParseValue"/> refuses.</summary>
    public sealed class PlotLimitBoxViewModel : ViewModelBase
    {
        private string _text = "";
        private bool _isInvalid;
        private double? _value;

        public string Text
        {
            get => _text;
            set
            {
                SetProperty(ref _text, value ?? "");
                var ok = PlotLimits.TryParseValue(_text, out var parsed);
                IsInvalid = !ok;
                Value = ok ? parsed : null;
            }
        }

        public bool IsInvalid
        {
            get => _isInvalid;
            private set => SetProperty(ref _isInvalid, value);
        }

        public double? Value
        {
            get => _value;
            private set => SetProperty(ref _value, value);
        }

        // Round-trip format: a rounded box would differ from the stored value and save it again
        // on the next commit, unasked.
        internal void Load(double? value) =>
            Text = value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
    }
}
