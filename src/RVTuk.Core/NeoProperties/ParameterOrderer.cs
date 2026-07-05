using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.NeoProperties
{
    /// <summary>
    /// Reorders a flat parameter list into pinned-first, then a fixed group order — a
    /// placeholder ordering used to validate the Neo Properties pane mechanism. Swap
    /// <see cref="PinnedParameterNames"/> / <see cref="GroupOrder"/> for real values later.
    /// </summary>
    public static class ParameterOrderer
    {
        public static readonly IReadOnlyList<string> PinnedParameterNames =
            new[] { "Mark", "Comments", "Family and Type" };

        public static readonly IReadOnlyList<string> GroupOrder =
            new[] { "Identity Data", "Constraints", "Dimensions" };

        public static IReadOnlyList<ParameterGroupView> Order(IReadOnlyList<ParameterEntry> parameters)
        {
            var remaining = new List<ParameterEntry>(parameters);
            var result = new List<ParameterGroupView>();

            var pinned = new List<ParameterEntry>();
            foreach (var pinnedName in PinnedParameterNames)
            {
                var index = remaining.FindIndex(p => p.Name == pinnedName);
                if (index < 0) continue;
                pinned.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
            if (pinned.Count > 0)
                result.Add(new ParameterGroupView("Pinned", pinned));

            var groupsInFirstSeenOrder = remaining
                .Select(p => p.Group)
                .Distinct()
                .ToList();

            var orderedGroupNames = GroupOrder
                .Where(g => groupsInFirstSeenOrder.Contains(g))
                .Concat(groupsInFirstSeenOrder.Where(g => !GroupOrder.Contains(g)))
                .ToList();

            foreach (var groupName in orderedGroupNames)
            {
                var entries = remaining.Where(p => p.Group == groupName).ToList();
                result.Add(new ParameterGroupView(groupName, entries));
            }

            return result;
        }
    }
}
