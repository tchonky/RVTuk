using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Everything that must travel with an item for it to be portable into the Standard
    /// (or, later, into a real Revit model). Filters -> their parameters (GUIDs) -> patterns/subcats.</summary>
    [DataContract]
    public class DependencyClosure
    {
        [DataMember] public List<string> Filters { get; set; } = new List<string>();
        [DataMember] public List<string> ParameterGuids { get; set; } = new List<string>();
        [DataMember] public List<string> Patterns { get; set; } = new List<string>();
        [DataMember] public List<string> Subcategories { get; set; } = new List<string>();
    }
}
