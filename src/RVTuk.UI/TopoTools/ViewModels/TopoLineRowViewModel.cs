using System;
using RVTuk.Core.TopoTools;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.TopoTools.ViewModels
{
    /// <summary>
    /// One topo line in the pane's list. Unlike the Auto Dimensions rows this cannot be the plain
    /// record it displays: the height is edited here — there is nowhere else, since the Lines
    /// category refuses a visible parameter — and the row is highlighted when its line is selected
    /// in the view.
    ///
    /// The row stays dumb about threading. Committing a height calls the delegate the pane supplies,
    /// and the pane is what moves that onto the pool; a row calling an ExternalEvent directly from
    /// a WPF binding would block Revit's main thread against its own event.
    /// </summary>
    public class TopoLineRowViewModel : ViewModelBase
    {
        private readonly Action<long, string> _commitElevation;
        private readonly Action<long> _selectInView;

        public TopoLineRowViewModel(
            TopoLineInfo info, Action<long, string> commitElevation, Action<long> selectInView)
        {
            _commitElevation = commitElevation;
            _selectInView = selectInView;

            LineId = info.LineId;
            DisplayName = info.DisplayName;
            StatusText = info.StatusText;
            _elevationText = info.ElevationText;

            SelectInViewCommand = new RelayCommand(() => _selectInView(LineId));
        }

        public long LineId { get; }
        public string DisplayName { get; }
        public string StatusText { get; }
        public RelayCommand SelectInViewCommand { get; }

        private string _elevationText;

        /// <summary>
        /// The height as text, in the document's units, parsed on the Revit side by
        /// <c>UnitFormatUtils.TryParse</c>. Empty clears the height — 0 would be a real elevation.
        /// </summary>
        public string ElevationText
        {
            get => _elevationText;
            set
            {
                var text = value ?? "";
                if (string.Equals(_elevationText, text, StringComparison.Ordinal)) return;

                SetProperty(ref _elevationText, text);
                _commitElevation(LineId, text);
            }
        }

        private bool _isSelected;

        /// <summary>True while this line is part of Revit's current selection.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
