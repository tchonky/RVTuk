using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    /// <summary>Base for a category's serializable standards payload. Concrete types are
    /// serialized/deserialized individually (one JSON payload per category), so no
    /// polymorphic/KnownType handling is required.</summary>
    [DataContract]
    public abstract class CategorySnapshot
    {
        [DataMember] public string CategoryId { get; set; } = string.Empty;
    }
}
