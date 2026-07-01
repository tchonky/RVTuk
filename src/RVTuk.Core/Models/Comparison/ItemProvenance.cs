using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Where an item (and, when merged, each field) in the Standard came from.</summary>
    [DataContract]
    public class ItemProvenance
    {
        [DataMember] public string ItemKey { get; set; } = string.Empty;
        [DataMember] public string SourceName { get; set; } = string.Empty;
        [DataMember] public string CapturedUtc { get; set; } = string.Empty;
        /// <summary>field id -> source name, for field-level merges.</summary>
        [DataMember] public List<KvPair> FieldSources { get; set; } = new List<KvPair>();
    }
}
