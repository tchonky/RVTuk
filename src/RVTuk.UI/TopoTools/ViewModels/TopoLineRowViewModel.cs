using System;
using RVTuk.Core.TopoTools;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.TopoTools.ViewModels
{
    /// <summary>
    /// One topo line in the pane's list. Unlike the Auto Dimensions rows this cannot be the plain
    /// record it displays: the height is edited here — there is nowhere else, since the Lines
    /// category refuses a visible parameter — and the row's highlight is Revit's selection, in both
    /// directions.
    ///
    /// The row stays dumb about threading and about what "selected" means elsewhere. It reports
    /// that something changed; the pane decides whether that means talking to Revit. A row calling
    /// an ExternalEvent directly from a WPF binding would block Revit's main thread against its own
    /// event.
    /// </summary>
    public class TopoLineRowViewModel : ViewModelBase
    {
        private readonly Action<long, string> _commitElevation;
        private readonly Action _selectionChanged;

        public TopoLineRowViewModel(
            TopoLineInfo info, Action<long, string> commitElevation, Action selectionChanged)
        {
            _commitElevation = commitElevation;
            _selectionChanged = selectionChanged;

            LineId = info.LineId;
            DisplayName = info.DisplayName;
            StatusText = info.StatusText;
            _elevationText = info.ElevationText;
        }

        public long LineId { get; }
        public string DisplayName { get; }
        public string StatusText { get; }

        private string _elevationText;

        /// <summary>
        /// The height as text, in the document's units, parsed on the Revit side by
        /// <c>UnitFormatUtils.TryParse</c>. Empty clears the height — 0 would be a real elevation.
        /// When several rows are selected, committing this applies to all of them.
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

        /// <summary>
        /// Bound two-way to the row's ListBoxItem, so it is both "the user highlighted this row"
        /// and "Revit has this line selected" — the two are the same fact.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                SetProperty(ref _isSelected, value);
                _selectionChanged();
            }
        }
    }
}
