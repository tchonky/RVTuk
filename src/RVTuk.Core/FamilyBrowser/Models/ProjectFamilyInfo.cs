namespace RVTuk.Core.FamilyBrowser.Models
{
    /// <summary>
    /// A family loaded in the active Revit project, as reported to the Family Browser:
    /// its name plus the value of the "_Version" shared parameter (null when the loaded
    /// copy doesn't carry that parameter, or carries it only as an instance parameter of
    /// a family with no placed instances).
    /// </summary>
    public class ProjectFamilyInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? Version { get; set; }
        // Revit category name, so model-only rows can show one without a library index entry.
        public string? Category { get; set; }
        // True when the loaded copy carries _Version as an *instance* parameter (the value had
        // to be read off a placed instance instead of the symbols). The office standard wants
        // a type parameter — the browser paints the version red to flag the family.
        public bool VersionIsInstance { get; set; }
    }
}
