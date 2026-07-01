namespace RVTuk.UI.ViewModels
{
    /// <summary>The per-item decision in the roster. In v1 these record into the report/Standard
    /// only — they never modify a Revit model.</summary>
    public enum DecisionOption
    {
        Pending,
        AcceptA,
        AcceptB,
        Merge,
        Ignore
    }

    public enum ComparatorMode
    {
        BuildTemplate,
        AuditProject
    }
}
