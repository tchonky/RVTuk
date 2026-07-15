using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    public class CategoryFilterOption : ViewModelBase
    {
        private bool _isSelected = true;

        public string Name { get; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public CategoryFilterOption(string name) => Name = name;
    }
}
