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

        /// <summary>
        /// The file's real last-write time (UTC). Written to the DB only AFTER metadata
        /// extraction succeeds, so a family whose extraction is cancelled stays stale and is
        /// re-scanned next time. Until then the row carries a sentinel date.
        /// </summary>
        public System.DateTime ModifiedDate { get; set; }

        /// <summary>
        /// The file's real size in bytes. Written to the DB only AFTER metadata extraction
        /// succeeds (see <see cref="ModifiedDate"/>).
        /// </summary>
        public long FileSize { get; set; }
    }
}
