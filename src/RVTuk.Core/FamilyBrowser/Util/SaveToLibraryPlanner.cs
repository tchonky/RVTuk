using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Picks the library-relative target path for saving a model-only family into the
    /// library: the folder where most same-category families already live, else a folder
    /// named after the (sanitised) category, else the library root. Pure string logic —
    /// no disk access, and no System.IO.Path on family names (see FamilyFileName).
    /// </summary>
    public static class SaveToLibraryPlanner
    {
        public static (string? RelativePath, string? Error) Plan(
            string familyName,
            string? category,
            IEnumerable<(string? Category, string RelativePath)> libraryItems)
        {
            if (!FamilyFileName.IsSafeFileName(familyName))
                return (null, $"The family name '{familyName}' contains characters that are not " +
                              "allowed in Windows file names. Rename the family in the project first.");

            var fileName = familyName + ".rfa";
            var dir = MostCommonCategoryFolder(category, libraryItems) ?? SanitizeFolderName(category);
            return (dir.Length == 0 ? fileName : dir + "\\" + fileName, null);
        }

        // The folder holding the most same-category families (ties break on ordinal name so
        // the result is deterministic). Null when the category has no indexed families.
        private static string? MostCommonCategoryFolder(
            string? category, IEnumerable<(string? Category, string RelativePath)> libraryItems)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            return libraryItems
                .Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase))
                .Select(i => FolderOf(i.RelativePath))
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .FirstOrDefault()?.Key;
        }

        // Directory part of a library-relative path, tolerating both separators.
        private static string FolderOf(string relativePath)
        {
            int cut = Math.Max(relativePath.LastIndexOf('\\'), relativePath.LastIndexOf('/'));
            return cut < 0 ? string.Empty : relativePath.Substring(0, cut);
        }

        // A category name becomes a folder name by dropping forbidden characters entirely
        // (folder names are not identity-critical, unlike the family file name).
        private static string SanitizeFolderName(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return string.Empty;
            var sb = new StringBuilder(category!.Length);
            foreach (var c in category)
                if (c >= ' ' && "\"<>|:*?\\/".IndexOf(c) < 0) sb.Append(c);
            return sb.ToString().Trim();
        }
    }
}
