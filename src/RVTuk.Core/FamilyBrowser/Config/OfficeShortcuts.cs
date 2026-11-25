using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RVTuk.Core.FamilyBrowser.Config
{
    /// <summary>
    /// The office's Revit keyboard shortcuts, kept as <c>KeyboardShortcuts.xml</c> in the
    /// library's <c>.DB</c> folder. Revit has no API for shortcuts: it reads each user's own
    /// <c>%APPDATA%\Autodesk\Revit\Autodesk Revit &lt;year&gt;\KeyboardShortcuts.xml</c> when
    /// it starts, so installing means copying the office file over that one (the office keys
    /// win), keeping the user's old file as a dated backup. The file is a full export from
    /// Revit's Keyboard Shortcuts dialog, so it stands on its own.
    /// </summary>
    public static class OfficeShortcuts
    {
        public const string FileName = "KeyboardShortcuts.xml";
        private static readonly Regex YearFolder = new(@"^Autodesk Revit (20\d\d)$", RegexOptions.IgnoreCase);

        /// <summary>Where the office file lives: <c>&lt;library&gt;\.DB\KeyboardShortcuts.xml</c>.</summary>
        public static string OfficeFile(string libraryRoot) =>
            Path.Combine(libraryRoot, Shared.Config.AppConfig.DbFolderName, FileName);

        /// <summary><c>%APPDATA%\Autodesk\Revit</c>, the parent of each year's user folder.</summary>
        public static string UserRevitRoot() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit");

        /// <summary>Each Revit year this user has started (its folder exists), oldest first.</summary>
        public static IReadOnlyList<(int Year, string Folder)> UserYears(string userRevitRoot)
        {
            if (!Directory.Exists(userRevitRoot)) return Array.Empty<(int, string)>();
            return Directory.GetDirectories(userRevitRoot)
                .Select(d => (Match: YearFolder.Match(Path.GetFileName(d)), Folder: d))
                .Where(x => x.Match.Success)
                .Select(x => (int.Parse(x.Match.Groups[1].Value), x.Folder))
                .OrderBy(x => x.Item1)
                .ToList();
        }

        /// <summary>Null when the file is a Revit shortcuts export, otherwise why it isn't.</summary>
        public static string? Validate(string path)
        {
            if (!File.Exists(path)) return "There is no file at " + path + ".";
            try
            {
                var root = XDocument.Load(path).Root;
                if (root == null || root.Name.LocalName != "Shortcuts")
                    return path + " is not a Revit keyboard shortcuts file (it has no <Shortcuts> list).";
                if (!root.Elements().Any(e => e.Name.LocalName == "ShortcutItem"))
                    return path + " lists no shortcuts.";
                return null;
            }
            catch (Exception ex) { return path + " could not be read: " + ex.Message; }
        }

        /// <summary>
        /// Copies the office file over each year's user file. A year's existing file is first
        /// renamed to <c>KeyboardShortcuts.before-RVTuk-&lt;time&gt;.xml</c>. Returns the years
        /// done. Throws (with nothing changed) when the office file is not a shortcuts file.
        /// </summary>
        public static IReadOnlyList<int> Install(string officeFile, string userRevitRoot, DateTime now)
        {
            var error = Validate(officeFile);
            if (error != null) throw new InvalidDataException(error);
            var done = new List<int>();
            foreach (var (year, folder) in UserYears(userRevitRoot))
            {
                var target = Path.Combine(folder, FileName);
                if (File.Exists(target))
                    File.Copy(target, Path.Combine(folder, "KeyboardShortcuts.before-RVTuk-" + now.ToString("yyyyMMdd-HHmmss") + ".xml"), true);
                File.Copy(officeFile, target, true);
                done.Add(year);
            }
            return done;
        }

        /// <summary>The newest user shortcuts file (the year the maintainer last changed), or null.</summary>
        public static (int Year, string Path)? NewestUserFile(string userRevitRoot) =>
            UserYears(userRevitRoot)
                .Select(y => (y.Year, Path: Path.Combine(y.Folder, FileName)))
                .Where(y => File.Exists(y.Path))
                .OrderByDescending(y => File.GetLastWriteTimeUtc(y.Path))
                .ThenByDescending(y => y.Year)
                .Select(y => ((int, string)?)y)
                .FirstOrDefault();

        /// <summary>The maintainer's own shortcuts become the office's. Throws when the file is not a
        /// shortcuts file.</summary>
        public static void Publish(string userFile, string officeFile)
        {
            var error = Validate(userFile);
            if (error != null) throw new InvalidDataException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(officeFile)!);
            File.Copy(userFile, officeFile, true);
        }
    }
}
