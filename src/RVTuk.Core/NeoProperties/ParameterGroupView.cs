using System.Collections.Generic;

namespace RVTuk.Core.NeoProperties
{
    /// <summary>One rendered group header plus its parameters, in display order.</summary>
    public record ParameterGroupView(string GroupName, IReadOnlyList<ParameterEntry> Parameters);
}
