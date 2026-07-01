namespace RVTuk.Core.Models.Comparison
{
    /// <summary>How an item differs between snapshot A and snapshot B.</summary>
    public enum DiffKind
    {
        Added,      // only in A
        Removed,    // only in B
        Changed,    // in both, some fields differ
        Unchanged   // in both, identical
    }
}
