using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Serializable key/value pair. Used instead of KeyValuePair&lt;,&gt; because
    /// DataContractJsonSerializer (net48 path) does not round-trip the BCL struct cleanly.</summary>
    [DataContract]
    public class KvPair
    {
        public KvPair() { }
        public KvPair(string key, string value) { Key = key; Value = value; }

        [DataMember] public string Key { get; set; } = string.Empty;
        [DataMember] public string Value { get; set; } = string.Empty;
    }
}
