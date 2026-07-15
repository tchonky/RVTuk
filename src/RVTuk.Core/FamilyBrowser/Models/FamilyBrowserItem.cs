using System;

namespace RVTuk.Core.FamilyBrowser.Models
{
    public class FamilyBrowserItem
    {
        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? Category { get; set; }
        public DateTime ModifiedDate { get; set; }
        public byte[]? ThumbnailPng { get; set; }  // resolved: CustomThumbnail ?? OLE thumbnail
        public bool HasCustomThumbnail { get; set; }
        public bool OleSynced { get; set; } = true;
        public VersionStatus VersionStatus { get; set; } = VersionStatus.None;
        // Value of the "_Version" shared parameter, captured by the deep scan (null when the
        // family doesn't carry it or hasn't been deep-scanned since it last changed on disk).
        public string? Version { get; set; }
        public int RevitYear { get; set; }
        public string? Tags { get; set; }
        public bool IsFavorite { get; set; }
    }
}
