using System;
using System.Collections.Generic;

namespace RVTuk.Core.Models
{
    public class FamilyModel
    {
        public long Id { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime ModifiedDate { get; set; }
        public long FileSize { get; set; }
        public string? Category { get; set; }
        public DateTime IndexedDate { get; set; }
        // Value of the "_Version" shared parameter, captured by the deep scan (null when the
        // family doesn't carry it or hasn't been deep-scanned since it last changed on disk).
        public string? Version { get; set; }
    }
}
