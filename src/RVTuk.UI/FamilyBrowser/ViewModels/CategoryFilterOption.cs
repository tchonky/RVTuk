using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    /// <summary>A category in the search dropdown. Unticked by default: no ticked category
    /// means every category is shown.</summary>
    public class CategoryFilterOption : ViewModelBase
    {
        private bool _isSelected;
        private int _count;

        public string Name { get; }

        /// <summary>The name the category icon is chosen from (empty for "(No category)").</summary>
        public string IconCategory { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>Families in this category under the current tags and ⭐.</summary>
        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }

        public CategoryFilterOption(string name, string iconCategory)
        {
            Name = name;
            IconCategory = iconCategory;
        }
    }
}
