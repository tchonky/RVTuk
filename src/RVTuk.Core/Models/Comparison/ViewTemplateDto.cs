using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>A single view template's captured standards content.</summary>
    [DataContract]
    public class ViewTemplateDto
    {
        [DataMember] public string Name { get; set; } = string.Empty;
        /// <summary>Per-document UniqueId. In-session tiebreaker only — NOT a cross-model identity.</summary>
        [DataMember] public string UniqueId { get; set; } = string.Empty;
        /// <summary>ViewType enum as string. Matching happens within a view-type bucket.</summary>
        [DataMember] public string ViewType { get; set; } = string.Empty;
        /// <summary>Which parameters this template controls vs leaves uncontrolled.</summary>
        [DataMember] public List<ControlledParam> Included { get; set; } = new List<ControlledParam>();
        /// <summary>Values of controlled fields (scale, detail level, discipline, phase, ...).</summary>
        [DataMember] public List<KvPair> Settings { get; set; } = new List<KvPair>();
        /// <summary>Stable digest of the per-category V/G override tree (cheap "changed?" detection).</summary>
        [DataMember] public string CategoryOverridesHash { get; set; } = string.Empty;
        /// <summary>Names of applied view filters (ordered).</summary>
        [DataMember] public List<string> FilterNames { get; set; } = new List<string>();
    }
}
