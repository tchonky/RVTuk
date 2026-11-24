using RVTuk.Core.FamilyBrowser.Util;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    /// <summary>A chip in the search box: a ticked category (OR) or a chosen tag (AND).</summary>
    public sealed class FilterChip
    {
        public FilterChip(bool isCategory, string value, string? iconCategory = null)
        {
            IsCategory = isCategory;
            Value = value;
            IconCategory = iconCategory ?? string.Empty;
        }

        public bool IsCategory { get; }
        public bool IsTag => !IsCategory;
        public string Value { get; }
        public string IconCategory { get; }
        public bool IsBuiltIn => !IsCategory && BuiltInTags.IsBuiltIn(Value);

        /// <summary>Icon name: the category's, a lock for built-in tags, a tag for the others.</summary>
        public string IconName => IsCategory ? TablerIconName(IconCategory) : IsBuiltIn ? "lock" : "tag";

        public string ToolTip => IsCategory
            ? "Category: families in any ticked category"
            : "Tag: families must have every tag";

        public string RemoveLabel => "Remove " + Value;

        private static string TablerIconName(string category) => Views.TablerIcons.IconNameForCategory(category);
    }

    /// <summary>A tag offered in the search dropdown.</summary>
    public sealed class TagSuggestion
    {
        public TagSuggestion(string name, int count, bool isFirst)
        {
            Name = name;
            Count = count;
            IsFirst = isFirst;
        }

        public string Name { get; }
        public int Count { get; }

        /// <summary>The one Enter adds.</summary>
        public bool IsFirst { get; }

        public bool IsBuiltIn => BuiltInTags.IsBuiltIn(Name);
        public bool IsEnabled => Count > 0;

        public string ToolTip =>
            Count == 0 && Is(BuiltInTags.Project) ? "Press ⟳ to check the open project first"
            : Count == 0 ? "No family has this tag right now"
            : Is(BuiltInTags.Project) ? "Added automatically to every family loaded in the open project"
            : Is(BuiltInTags.Library) ? "Added automatically to every family in the library"
            : Is(BuiltInTags.FixRequested) ? "Added automatically to every family with an open fix request"
            : "Show only families with this tag";

        private bool Is(string tag) => string.Equals(Name, tag, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A tag shown on the selected family. Built-in ones can't be removed.</summary>
    public sealed class TagChip
    {
        public TagChip(string name) => Name = name;
        public string Name { get; }
        public bool IsBuiltIn => BuiltInTags.IsBuiltIn(Name);
        public string ToolTip => IsBuiltIn
            ? "Added automatically, can't be removed. Click to filter the list by it."
            : "Filter the list by this tag";
    }
}
