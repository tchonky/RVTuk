using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>Builds DWG filenames from resolved naming-rule parts.</summary>
    public static class FileNameComposer
    {
        public static string Compose(IReadOnlyList<NamingRulePart> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                sb.Append(parts[i].Prefix).Append(parts[i].Value).Append(parts[i].Suffix);
                if (i < parts.Count - 1) sb.Append(parts[i].Separator);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Makes a rule-produced name safe as a Windows filename: every invalid character
        /// becomes '-', trailing dots/spaces are trimmed, and a name that ends up empty
        /// falls back to "Sheet" so the export can still proceed.
        /// </summary>
        public static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '-' : c);
            var clean = sb.ToString().Trim().TrimEnd('.', ' ').Trim();
            return clean.Length == 0 ? "Sheet" : clean;
        }

        /// <summary>Names occurring more than once (case-insensitive), each reported once.</summary>
        public static IReadOnlyList<string> FindDuplicates(IEnumerable<string> names)
        {
            return names
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
        }
    }
}
