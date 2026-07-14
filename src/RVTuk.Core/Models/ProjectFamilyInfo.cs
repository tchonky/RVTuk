namespace RVTuk.Core.Models
{
    /// <summary>
    /// A family loaded in the active Revit project, as reported to the Family Browser:
    /// its name plus the value of the "_Version" shared parameter (null when the loaded
    /// copy doesn't carry that parameter).
    /// </summary>
    public class ProjectFamilyInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? Version { get; set; }
        // Revit category name, so model-only rows can show one without a library index entry.
        public string? Category { get; set; }
    }
}
