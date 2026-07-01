using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RVTuk.Core.Models.Comparison
{
    [DataContract]
    public class ViewTemplatesSnapshot : CategorySnapshot
    {
        public const string Category = "ViewTemplates";

        public ViewTemplatesSnapshot()
        {
            CategoryId = Category;
        }

        [DataMember] public List<ViewTemplateDto> Templates { get; set; } = new List<ViewTemplateDto>();
    }
}
