using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    /// <summary>A category's pure-Core comparison logic: load a stored payload, then match
    /// and diff two snapshots. Knows nothing about Revit. Extraction lives in RVTuk.Revit.</summary>
    public interface ICategoryComparer
    {
        string CategoryId { get; }
        string DisplayName { get; }

        /// <summary>Deserialize a stored JSON payload back into a typed snapshot.</summary>
        CategorySnapshot LoadSnapshot(string payloadJson);

        /// <summary>Match + diff two snapshots of this category.</summary>
        CategoryDiffResult Compare(CategorySnapshot a, CategorySnapshot b);
    }
}
