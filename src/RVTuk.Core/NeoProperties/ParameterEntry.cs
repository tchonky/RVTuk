namespace RVTuk.Core.NeoProperties
{
    /// <summary>One parameter's display data, as read from the Revit element (or a test double).</summary>
    public record ParameterEntry(string Group, string Name, string Value);
}
