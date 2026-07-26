using System;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Derives a family's display name from its file name with plain string operations.
    /// <c>System.IO.Path</c> must not be used here: the browser's synthetic "model only"
    /// rows build their FileName from the in-project family name (plus ".rfa"), and Revit
    /// allows characters in family names that are illegal in Windows paths — a name with
    /// an inch mark (") makes .NET Framework's <c>Path.GetFileNameWithoutExtension</c>
    /// throw <see cref="ArgumentException"/>, and a '/' makes every framework treat the
    /// name as a directory path and truncate it. Only a trailing ".rfa" is stripped, so
    /// dots inside a family name are never eaten.
    /// </summary>
    public static class FamilyFileName
    {
        public static string WithoutRfaExtension(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            return fileName!.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 4)
                : fileName;
        }

        // The invalid set is hard-coded (not Path.GetInvalidFileNameChars) so net48 and net8
        // agree: net48's list is a superset and both contain all of these.
        private const string InvalidFileNameChars = "\"<>|:*?\\/";

        /// <summary>
        /// True iff <paramref name="name"/> can be used as a Windows file name as-is:
        /// non-blank and free of characters Windows forbids. Family names that fail this
        /// (Revit allows " and / in them) cannot be saved to or edited via an .rfa file.
        /// </summary>
        public static bool IsSafeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var c in name!)
                if (c < ' ' || InvalidFileNameChars.IndexOf(c) >= 0) return false;
            return true;
        }
    }
}
