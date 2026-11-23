using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    public enum PickerItemKind
    {
        Clear,
        Heading,
        Suggestion,
        Entry
    }

    /// <summary>One line of the usage picker's list. A class, not a record: RVTuk.UI has no
    /// IsExternalInit polyfill for net48.</summary>
    public sealed class PickerItem
    {
        public PickerItem(PickerItemKind kind, string text, UsageEntry? entry, UsageGroup group, bool isCurrent)
        {
            Kind = kind;
            Text = text;
            Entry = entry;
            Group = group;
            IsCurrent = isCurrent;
        }

        public PickerItemKind Kind { get; }
        public string Text { get; }
        public UsageEntry? Entry { get; }
        public UsageGroup Group { get; }
        public bool IsCurrent { get; }
        public bool IsPickable => Kind != PickerItemKind.Heading;
    }

    /// <summary>
    /// The New/Old usage picker: a search box over the usage table, "— none —", codes suggested
    /// from the selected areas' names, and the four groups in table order. Raises
    /// <see cref="Picked"/> with the chosen code, or null for none.
    /// </summary>
    public class UsagePickerViewModel : ViewModelBase
    {
        private static readonly IReadOnlyDictionary<UsageKind, string> Headings = new Dictionary<UsageKind, string>
        {
            [UsageKind.Primary] = "PRIMARY  שטחים עיקריים",
            [UsageKind.Service] = "SERVICE  שטחי שירות",
            [UsageKind.Other] = "OTHER  אחר",
            [UsageKind.Process] = "PROCESS  סימוני תהליך",
        };

        private readonly UsageKeySlot _slot;
        private readonly int? _currentCode;
        private readonly bool _isMixed;
        private readonly IReadOnlyList<int> _suggestions;
        private string _query = "";
        private int _highlightedIndex;

        /// <param name="currentCode">The selected areas' shared code, or null.</param>
        /// <param name="isMixed">The selected areas differ: nothing is marked current, not even
        /// "— none —".</param>
        public UsagePickerViewModel(UsageKeySlot slot, IReadOnlyList<string?> selectedNames, int? currentCode,
            bool isMixed = false)
        {
            _slot = slot;
            _currentCode = isMixed ? null : currentCode;
            _isMixed = isMixed;
            _suggestions = UsageSuggester.Suggest(selectedNames, slot);
            Rebuild();
        }

        public UsageKeySlot Slot => _slot;

        public ObservableCollection<PickerItem> Items { get; } = new();

        public event Action<int?>? Picked;

        public string Query
        {
            get => _query;
            set
            {
                SetProperty(ref _query, value ?? "");
                Rebuild();
            }
        }

        public int HighlightedIndex
        {
            get => _highlightedIndex;
            private set => SetProperty(ref _highlightedIndex, value);
        }

        public PickerItem? Highlighted =>
            HighlightedIndex >= 0 && HighlightedIndex < Items.Count ? Items[HighlightedIndex] : null;

        /// <summary>Moves the highlight by <paramref name="delta"/> pickable items, stopping at the
        /// ends.</summary>
        public void Move(int delta)
        {
            var step = Math.Sign(delta);
            var index = HighlightedIndex;
            for (var n = Math.Abs(delta); n > 0; n--)
            {
                var next = index + step;
                while (next >= 0 && next < Items.Count && !Items[next].IsPickable) next += step;
                if (next < 0 || next >= Items.Count) break;
                index = next;
            }

            HighlightedIndex = index;
            OnPropertyChanged(nameof(Highlighted));
        }

        public void PickHighlighted()
        {
            if (Highlighted is { } item) Pick(item);
        }

        public void Pick(PickerItem item)
        {
            if (!item.IsPickable) return;
            Picked?.Invoke(item.Kind == PickerItemKind.Clear ? null : item.Entry!.Code);
        }

        private void Rebuild()
        {
            Items.Clear();
            var empty = string.IsNullOrWhiteSpace(_query);
            if (empty)
            {
                Items.Add(new PickerItem(PickerItemKind.Clear, "— none — (clear)", null, UsageGroup.NoCode, _currentCode == null && !_isMixed));
                if (_suggestions.Count > 0)
                {
                    Items.Add(new PickerItem(PickerItemKind.Heading, "Suggested from the name", null, UsageGroup.NoCode, false));
                    foreach (var code in _suggestions)
                        Items.Add(ItemFor(PickerItemKind.Suggestion, UsageCatalog.ByCode(code)!, markCurrent: false));
                }
            }

            foreach (var group in UsageSearch.Filter(_query, _slot).GroupBy(e => e.Kind))
            {
                Items.Add(new PickerItem(PickerItemKind.Heading, Headings[group.Key], null, (UsageGroup)(int)group.Key, false));
                foreach (var entry in group)
                    Items.Add(ItemFor(PickerItemKind.Entry, entry, markCurrent: true));
            }

            var first = Items.ToList().FindIndex(i => i.IsPickable);
            HighlightedIndex = first < 0 ? -1 : first;
            OnPropertyChanged(nameof(Highlighted));
        }

        private PickerItem ItemFor(PickerItemKind kind, UsageEntry entry, bool markCurrent) =>
            new PickerItem(kind, entry.Code + "  " + entry.HebrewName, entry, (UsageGroup)(int)entry.Kind,
                markCurrent && entry.Code == _currentCode);
    }
}
