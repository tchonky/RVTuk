namespace RVTuk.UI.ViewModels
{
    /// <summary>A not-yet-implemented category, shown greyed with a [SOON] badge.</summary>
    public class PlaceholderCategoryViewModel : CategoryViewModelBase
    {
        public PlaceholderCategoryViewModel(string categoryId, string displayName)
        {
            CategoryId = categoryId;
            DisplayName = displayName;
        }

        public override string CategoryId { get; }
        public override string DisplayName { get; }
        public override bool IsAvailable => false;
    }
}
