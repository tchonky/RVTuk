using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.FamilyBrowser.Models;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>A library row as the project check sees it.</summary>
    public sealed class LibraryEntry
    {
        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? Version { get; set; }
        /// <summary>Under an ignored folder or matching an ignored pattern: not part of the library.</summary>
        public bool IsIgnored { get; set; }
    }

    public sealed class SkippedDuplicate
    {
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<string> RelativePaths { get; set; } = Array.Empty<string>();
    }

    public sealed class ProjectComparison
    {
        /// <summary>Library row id → the project's copy, for rows with exactly one non-ignored copy.</summary>
        public IReadOnlyDictionary<long, ProjectFamilyInfo> Matches { get; set; } = new Dictionary<long, ProjectFamilyInfo>();
        /// <summary>Library row id → every non-ignored copy's path, for duplicated names in the project.</summary>
        public IReadOnlyDictionary<long, IReadOnlyList<string>> Duplicates { get; set; } = new Dictionary<long, IReadOnlyList<string>>();
        /// <summary>Project families with no non-ignored library row.</summary>
        public IReadOnlyList<ProjectFamilyInfo> ModelOnly { get; set; } = Array.Empty<ProjectFamilyInfo>();
        /// <summary>Duplicated names Update All would otherwise have updated (a copy is newer).</summary>
        public IReadOnlyList<SkippedDuplicate> SkippedDuplicates { get; set; } = Array.Empty<SkippedDuplicate>();
    }

    /// <summary>
    /// Pairs the library with the active project by family name. Ignored rows are not part of
    /// the library; a name with several non-ignored copies is a duplicate and gets no verdict.
    /// </summary>
    public static class ProjectComparer
    {
        public static ProjectComparison Compare(IReadOnlyList<LibraryEntry> library, IReadOnlyList<ProjectFamilyInfo> project)
        {
            var projectByName = new Dictionary<string, ProjectFamilyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var pf in project)
                if (!projectByName.ContainsKey(pf.Name)) projectByName[pf.Name] = pf; // first occurrence wins

            var matches = new Dictionary<long, ProjectFamilyInfo>();
            var duplicates = new Dictionary<long, IReadOnlyList<string>>();
            var skipped = new List<SkippedDuplicate>();
            var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in library.Where(e => !e.IsIgnored)
                         .GroupBy(e => FamilyFileName.WithoutRfaExtension(e.FileName), StringComparer.OrdinalIgnoreCase))
            {
                libraryNames.Add(group.Key);
                if (!projectByName.TryGetValue(group.Key, out var pf)) continue;
                var copies = group.ToList();
                if (copies.Count == 1) { matches[copies[0].Id] = pf; continue; }

                var paths = copies.Select(c => c.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToList();
                foreach (var c in copies) duplicates[c.Id] = paths;
                if (copies.Any(c => FamilyVersionCheck.IsUpdateAvailable(c.Version, pf.Version)))
                    skipped.Add(new SkippedDuplicate { Name = pf.Name, RelativePaths = paths });
            }

            return new ProjectComparison
            {
                Matches = matches,
                Duplicates = duplicates,
                ModelOnly = projectByName.Values.Where(pf => !libraryNames.Contains(pf.Name)).ToList(),
                SkippedDuplicates = skipped,
            };
        }
    }
}
