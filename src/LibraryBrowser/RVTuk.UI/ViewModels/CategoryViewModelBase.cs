namespace RVTuk.UI.ViewModels
{
    /// <summary>One row in the category rail. Concrete categories (e.g. View Templates) are
    /// available; others are placeholders shown as [SOON].</summary>
    public abstract class CategoryViewModelBase : ViewModelBase
    {
        public abstract string CategoryId { get; }
        public abstract string DisplayName { get; }
        public abstract bool IsAvailable { get; }

        private int _diffCount;
        public int DiffCount
        {
            get => _diffCount;
            set { SetProperty(ref _diffCount, value); OnPropertyChanged(nameof(HasDiffs)); }
        }

        public bool HasDiffs => _diffCount > 0;
        public string Badge => IsAvailable ? (HasDiffs ? _diffCount.ToString() : "") : "SOON";
    }
}
