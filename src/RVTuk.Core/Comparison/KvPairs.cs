using System.Collections.Generic;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    internal static class KvPairs
    {
        public static Dictionary<string, string> ToDictionary(this List<KvPair> pairs)
        {
            var d = new Dictionary<string, string>();
            foreach (var p in pairs)
                d[p.Key] = p.Value;
            return d;
        }
    }
}
