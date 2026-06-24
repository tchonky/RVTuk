using System;
using System.Collections.ObjectModel;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.UI.ViewModels
{
    public class ViewTemplatesCategoryViewModel : CategoryViewModelBase
    {
        public override string CategoryId => ViewTemplatesSnapshot.Category;
        public override string DisplayName => "View Templates";
        public override bool IsAvailable => true;

        public ObservableCollection<ItemDiffViewModel> Items { get; } = new ObservableCollection<ItemDiffViewModel>();

        private ItemDiffViewModel? _selectedItem;
        public ItemDiffViewModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public void Load(CategoryDiffResult result, Action<ItemDiffViewModel> accept)
        {
            Items.Clear();
            foreach (var item in result.Items)
                Items.Add(new ItemDiffViewModel(item, accept));

            DiffCount = result.Summary.Added + result.Summary.Removed + result.Summary.Changed;
            SelectedItem = Items.Count > 0 ? Items[0] : null;
        }
    }
}
