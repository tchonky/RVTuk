using System;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Compares the office's "_Version" shared-parameter value between a library .rfa
    /// (captured into the index by the deep scan) and the copy loaded in the active project
    /// (read off the family's symbols, or off a placed instance when the parameter is
    /// instance-level). The check is deliberately skipped — never "update available" — when
    /// either side lacks the parameter or has an empty value.
    /// </summary>
    public static class FamilyVersionCheck
    {
        /// <summary>Name of the shared parameter that carries a family's version.</summary>
        public const string ParameterName = "_Version";

        public static bool IsUpdateAvailable(string? libraryVersion, string? projectVersion)
        {
            var lib = Normalize(libraryVersion);
            var proj = Normalize(projectVersion);
            if (lib == null || proj == null) return false;
            if (string.Equals(lib, proj, StringComparison.OrdinalIgnoreCase)) return false;

            // Numeric versions compare per dotted part ("10" > "9", "1.10" > "1.9"), and only a
            // library value AHEAD of the project counts as an update — a project holding a newer
            // copy than the library must not be prompted to "update" backwards.
            if (Version.TryParse(Dotted(lib), out var libVer) && Version.TryParse(Dotted(proj), out var projVer))
                return libVer > projVer;

            // Non-numeric schemes have no reliable ordering: any difference means the library
            // copy is not what the project holds.
            return true;
        }

        // Version.TryParse needs at least two parts.
        private static string Dotted(string v) => v.IndexOf('.') < 0 ? v + ".0" : v;

        /// <summary>Trims and maps empty to null, so "no value" and "missing parameter" behave alike.</summary>
        public static string? Normalize(string? version)
        {
            var v = version?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }
}
