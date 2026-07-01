using Autodesk.Revit.DB;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Revit.Extraction
{
    /// <summary>Produces a category's standards snapshot from a Revit document. The only place
    /// the Revit API is touched for comparison. Runs on Revit's main thread (inside ExternalEvent).</summary>
    public interface ICategoryExtractor
    {
        string CategoryId { get; }
        CategorySnapshot Extract(Document doc);
    }
}
