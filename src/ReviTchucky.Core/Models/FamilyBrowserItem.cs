using System;

namespace ReviTchucky.Core.Models
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
    }
}
