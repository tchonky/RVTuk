using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Matches file names against the user-configured regular expressions in
    /// <c>AppConfig.IgnoredFilePatterns</c> (e.g. the Revit backup pattern <c>.*\.\d{4}\.rfa</c>).
    /// Patterns are matched case-insensitively against the file NAME only, never the path —
    /// path-based exclusion is <see cref="Shared.Util.PathUtil.IsUnderIgnoredFolder"/>'s job. The patterns
    /// are user-typed, so invalid regexes are skipped (never fail a scan) and matching is
    /// capped by a timeout so a catastrophic-backtracking pattern can't hang a scan over
    /// thousands of files. Construct once per scan/filter pass — compiling per file is wasteful.
    /// </summary>
    public sealed class IgnoredFileMatcher
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

        private readonly List<Regex> _patterns = new List<Regex>();

        public IgnoredFileMatcher(IEnumerable<string>? patterns)
        {
            if (patterns == null) return;
            foreach (var raw in patterns)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    _patterns.Add(new Regex(raw.Trim(),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout));
                }
                catch (ArgumentException) { /* invalid user regex — skip the entry */ }
            }
        }

        public bool HasPatterns => _patterns.Count > 0;

        /// <summary>True when <paramref name="fileName"/> matches any configured pattern.</summary>
        public bool IsIgnored(string fileName)
        {
            foreach (var regex in _patterns)
            {
                try
                {
                    if (regex.IsMatch(fileName)) return true;
                }
                catch (RegexMatchTimeoutException) { /* pathological pattern — treat as no match */ }
            }
            return false;
        }
    }
}
