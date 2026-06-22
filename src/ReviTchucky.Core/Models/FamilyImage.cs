namespace ReviTchucky.Core.Models
{
    public class FamilyImage
    {
        public long Id { get; set; }
        public long FamilyId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
    }
}
