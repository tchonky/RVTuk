using RVTuk.Core.DwgExporter;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.DwgExporter.ViewModels
{
    /// <summary>One row of the Models list: an open model and whether the run includes it.
    /// The active model is always included and its checkbox is locked on.</summary>
    public class ModelSelectionItem : ViewModelBase
    {
        public ModelSelectionItem(ModelSetupInventory model, bool isActive)
        {
            Key = model.Key;
            Title = model.Title;
            IsReadOnly = model.IsReadOnly;
            IsActive = isActive;
            _isSelected = isActive;
        }

        public string Key { get; }
        public string Title { get; }
        public bool IsReadOnly { get; }
        public bool IsActive { get; }

        /// <summary>The active model can't be unticked — it is the model the dialog read its
        /// sets and setups from.</summary>
        public bool CanUnselect => !IsActive;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, IsActive || value);
        }

        public string Display => IsActive
            ? Title + "  (active)"
            : IsReadOnly ? Title + "  (read-only)" : Title;
    }
}
