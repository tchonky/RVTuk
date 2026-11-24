using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.FamilyBrowser.Models;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Tags every family carries without anyone typing them. They are worked out each time from
    /// the family's state and never stored, so Edit Info drops them from the typed tags.
    /// </summary>
    public static class BuiltInTags
    {
        /// <summary>Loaded in the open project: every status except <see cref="VersionStatus.None"/>.</summary>
        public const string Project = "Project";

        /// <summary>In the library: every status except <see cref="VersionStatus.ModelOnly"/>.</summary>
        public const string Library = "Library";

        /// <summary>Someone asked the maintainer to fix this family (an open request).</summary>
        public const string FixRequested = "Fix requested";

        public static IReadOnlyList<string> All { get; } = new[] { Project, Library, FixRequested };

        public static bool IsBuiltIn(string? tag) =>
            tag != null && All.Any(b => string.Equals(b, tag.Trim(), StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<string> For(VersionStatus status, bool fixRequested)
        {
            if (status != VersionStatus.None) yield return Project;
            if (status != VersionStatus.ModelOnly) yield return Library;
            if (fixRequested) yield return FixRequested;
        }
    }

    /// <summary>
    /// The browser's search. Categories combine with OR (any ticked one; none ticked = all),
    /// tags with AND (a family needs every chosen tag), and so do the typed words (each must
    /// appear in the name or in one of the tags) and the favourites toggle.
    /// </summary>
    public static class FamilyFilter
    {
        /// <summary>The typed tags of a family: the stored comma-separated text, trimmed, without
        /// built-in names (which are never stored) and without repeats.</summary>
        public static IReadOnlyList<string> OwnTags(string? storedTags)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(storedTags)) return result;
            foreach (var raw in storedTags!.Split(','))
            {
                var t = raw.Trim();
                if (t.Length == 0 || BuiltInTags.IsBuiltIn(t)) continue;
                if (result.Any(r => string.Equals(r, t, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(t);
            }
            return result;
        }

        /// <summary>Own tags followed by the built-in ones that apply.</summary>
        public static IReadOnlyList<string> TagsOf(string? storedTags, VersionStatus status, bool fixRequested)
            => OwnTags(storedTags).Concat(BuiltInTags.For(status, fixRequested)).ToList();

        /// <summary>What Edit Info stores: the own tags joined with ", ", or null when none.</summary>
        public static string? CleanStoredTags(string? typed)
        {
            var own = OwnTags(typed);
            return own.Count == 0 ? null : string.Join(", ", own);
        }

        public static string[] Tokens(string? text) =>
            (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        public static bool HasAllTags(IReadOnlyList<string> tags, IEnumerable<string> required) =>
            required.All(r => tags.Any(t => string.Equals(t, r, StringComparison.OrdinalIgnoreCase)));

        public static bool MatchesText(string name, IReadOnlyList<string> tags, IReadOnlyList<string> tokens) =>
            tokens.All(tok => name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0
                           || tags.Any(t => t.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0));

        public static bool MatchesCategory(string categoryKey, ICollection<string> selected) =>
            selected.Count == 0 || selected.Contains(categoryKey);

        /// <summary>The whole rule for one family; the browser applies the same parts separately
        /// so it can count categories and tags before the words and categories narrow the list.</summary>
        public static bool Matches(string name, string categoryKey, IReadOnlyList<string> tags, bool isFavorite,
            ICollection<string> selectedCategories, IEnumerable<string> requiredTags,
            IReadOnlyList<string> tokens, bool favoritesOnly)
            => (!favoritesOnly || isFavorite)
            && HasAllTags(tags, requiredTags)
            && MatchesText(name, tags, tokens)
            && MatchesCategory(categoryKey, selectedCategories);
    }
}
