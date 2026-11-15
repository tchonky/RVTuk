using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.RoundPositions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    /// <summary>Anything in the tree that can be selected or fixed. One shape for all three tiers
    /// keeps the commands single — Fix and Select never need to know which tier they were given.</summary>
    public abstract class TreeRow : ViewModelBase
    {
        private bool _isExpanded;

        protected TreeRow(string label, IReadOnlyList<long> elementIds)
        {
            Label = label;
            ElementIds = elementIds;
        }

        public string Label { get; }

        public IReadOnlyList<long> ElementIds { get; }

        public int Count => ElementIds.Count;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }

    public sealed class ElementRow : TreeRow
    {
        public ElementRow(DirtyElement element)
            : base(element.Name, new[] { element.ElementId })
        {
        }
    }

    public sealed class LevelRow : TreeRow
    {
        public LevelRow(LevelNode node)
            : base(node.Level, node.Elements.Select(e => e.ElementId).ToList())
        {
            Elements = node.Elements.Select(e => new ElementRow(e)).ToList();
        }

        public IReadOnlyList<ElementRow> Elements { get; }
    }

    public sealed class CategoryRow : TreeRow
    {
        public CategoryRow(CategoryNode node)
            : base(node.Category, node.Levels.SelectMany(l => l.Elements).Select(e => e.ElementId).ToList())
        {
            Levels = node.Levels.Select(l => new LevelRow(l)).ToList();
        }

        public IReadOnlyList<LevelRow> Levels { get; }
    }
}
