using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    /// <summary>Accepts an item from a source snapshot into the editable Standard.
    /// Pure-Core data operation — never writes to a Revit model.</summary>
    public interface ICategoryMerger
    {
        string CategoryId { get; }

        /// <param name="replace">If the item already exists in the Standard, replace it instead of conflicting.</param>
        MergeResult AcceptIntoStandard(
            StandardSnapshot std, CategorySnapshot source, string itemKey,
            DependencyClosure deps, bool replace);
    }
}
