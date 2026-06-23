namespace ReviTchucky.UI.ViewModels
{
    public class VersionFilterOption : ViewModelBase
    {
        private bool _isSelected;

        public int Year { get; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public VersionFilterOption(int year) => Year = year;
    }
}
