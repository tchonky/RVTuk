namespace ReviTchucky.Core.Models
{
    public class ParameterModel
    {
        public long Id { get; set; }
        public long FamilyId { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsInstance { get; set; }
    }
}
