using RVTuk.Core.AutoDimensions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>One row of the pane's category checklist.</summary>
    public class CategoryOptionViewModel : ViewModelBase
    {
        public CategoryOptionViewModel(
            string name, DimensionCategories category, bool isEnabled, string? toolTip)
        {
            Name = name;
            Category = category;
            IsEnabled = isEnabled;
            ToolTip = toolTip;
        }

        public string Name { get; }
        public DimensionCategories Category { get; }

        /// <summary>False for Ceilings/Floors: shown for layout continuity, inert until v2.</summary>
        public bool IsEnabled { get; }
        public string? ToolTip { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }
    }
}
