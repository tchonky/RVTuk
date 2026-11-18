using System;
using System.Collections.Generic;

namespace RVTuk.Core.FamilyBrowser.Models
{
    /// <summary>The active project as Sync saw it: which document, and its loaded families.</summary>
    public sealed class ProjectSnapshot
    {
        /// <summary>Identity of the document (see the Revit side's ProjectKey); empty when none.</summary>
        public string DocumentKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public IReadOnlyList<ProjectFamilyInfo> Families { get; set; } = Array.Empty<ProjectFamilyInfo>();
    }
}
