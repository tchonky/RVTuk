using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.UI.ViewModels
{
    public class ItemDiffViewModel : ViewModelBase
    {
        private readonly ItemDiff _item;
        private DecisionOption _decision = DecisionOption.Pending;

        public ItemDiffViewModel(ItemDiff item, Action<ItemDiffViewModel> accept)
        {
            _item = item;
            Fields = new ObservableCollection<FieldDiffViewModel>();
            foreach (var f in item.Fields)
                Fields.Add(new FieldDiffViewModel(f));
            AcceptCommand = new RelayCommand(() => accept(this));
        }

        public ItemDiff Model => _item;
        public string Key => _item.Key;
        public DiffKind Kind => _item.Kind;
        public ObservableCollection<FieldDiffViewModel> Fields { get; }

        public string StatusGlyph => Kind switch
        {
            DiffKind.Added => "+",
            DiffKind.Removed => "−",
            DiffKind.Changed => "△",
            _ => "≈"
        };

        public string NameA => Kind == DiffKind.Removed ? "—" : _item.DisplayName;
        public string NameB => Kind == DiffKind.Added ? "—" : _item.DisplayName;
        public string DisplayName => _item.DisplayName;

        public int ScoreA => (int)Math.Round(_item.CompletenessA * 100);
        public int ScoreB => (int)Math.Round(_item.CompletenessB * 100);

        public string Recommendation => Kind switch
        {
            DiffKind.Added => "Only in A — consider adopting",
            DiffKind.Removed => "Only in B — consider adopting",
            DiffKind.Unchanged => "Identical",
            _ => _item.CompletenessB > _item.CompletenessA ? "B is more complete"
                : _item.CompletenessA > _item.CompletenessB ? "A is more complete"
                : "Differs — review"
        };

        public DecisionOption Decision
        {
            get => _decision;
            set { SetProperty(ref _decision, value); OnPropertyChanged(nameof(IsDecided)); }
        }

        public bool IsDecided => _decision != DecisionOption.Pending;

        public ICommand AcceptCommand { get; }
    }
}
