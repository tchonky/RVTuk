using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>One view-template parameter and whether the template actually controls it
    /// (the "include" checkbox). Inclusion-awareness is the core correctness rule.</summary>
    [DataContract]
    public class ControlledParam
    {
        public ControlledParam() { }
        public ControlledParam(string id, string label, bool controlled)
        {
            Id = id; Label = label; Controlled = controlled;
        }

        [DataMember] public string Id { get; set; } = string.Empty;
        [DataMember] public string Label { get; set; } = string.Empty;
        [DataMember] public bool Controlled { get; set; }
    }
}
