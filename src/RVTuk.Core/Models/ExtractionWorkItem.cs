namespace RVTuk.Core.Models
{
    public class ExtractionWorkItem
    {
        public long FamilyId { get; set; }
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public byte[]? ThumbnailPng { get; set; }
        /// <summary>Revit build year read from BasicFileInfo (e.g. 2024). 0 = unknown.</summary>
        public int FileRevitYear { get; set; }
    }
}
