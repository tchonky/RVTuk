namespace ReviTchucky.Core.Models
{
    public class ParameterModel
    {
        public long Id { get; set; }
        public long FamilyId { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsInstance { get; set; }

        // "group parameter under" label, e.g. "Dimensions", "Identity Data", "Other".
        public string? ParamGroup { get; set; }
        // "System" | "Shared" | "Family"
        public string? Kind { get; set; }
        // Shared-parameter GUID (null for non-shared).
        public string? Guid { get; set; }
        public string? Formula { get; set; }
    }
}
