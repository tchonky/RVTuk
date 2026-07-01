using System;
using System.Collections.Generic;

namespace RVTuk.Core.Comparison
{
    /// <summary>Generic outer-join of two item collections by a string key.</summary>
    public static class Matcher
    {
        public class JoinResult<T>
        {
            public List<(T A, T B)> Matched { get; } = new List<(T, T)>();
            public List<T> OnlyA { get; } = new List<T>();
            public List<T> OnlyB { get; } = new List<T>();
        }

        public static JoinResult<T> OuterJoin<T>(
            IEnumerable<T> a, IEnumerable<T> b, Func<T, string> key)
        {
            var result = new JoinResult<T>();
            var bByKey = new Dictionary<string, T>();
            foreach (var item in b)
                bByKey[key(item)] = item;

            var matchedKeys = new HashSet<string>();
            foreach (var item in a)
            {
                var k = key(item);
                if (bByKey.TryGetValue(k, out var bItem))
                {
                    result.Matched.Add((item, bItem));
                    matchedKeys.Add(k);
                }
                else
                {
                    result.OnlyA.Add(item);
                }
            }

            foreach (var item in b)
            {
                if (!matchedKeys.Contains(key(item)))
                    result.OnlyB.Add(item);
            }

            return result;
        }
    }
}
