using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Identity + provenance for one captured snapshot (or the editable Standard).</summary>
    [DataContract]
    public class SnapshotMeta
    {
        /// <summary>"Project" | "Template" | "Standard".</summary>
        [DataMember] public string SourceKind { get; set; } = "Project";
        [DataMember] public string SourceName { get; set; } = string.Empty;
        [DataMember] public string? SourcePath { get; set; }
        [DataMember] public int RevitYear { get; set; }
        /// <summary>UTC ISO-8601 (DateTime.ToString("o")). Provenance only — never a "newer" claim.</summary>
        [DataMember] public string CapturedUtc { get; set; } = string.Empty;
        [DataMember] public int SchemaVersion { get; set; } = 1;
        /// <summary>True only for the Standard, which is curated/edited in-tool.</summary>
        [DataMember] public bool IsMutable { get; set; }
        [DataMember] public int Revision { get; set; }
    }
}
