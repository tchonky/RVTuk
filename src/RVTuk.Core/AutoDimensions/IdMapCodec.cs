using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// An id-to-id map as one "key:value;key:value" string — same rationale as
    /// <see cref="IdListCodec"/>. Used for the tracker's target-view-id → dimension-id map.
    /// </summary>
    public static class IdMapCodec
    {
        public static string Encode(IReadOnlyDictionary<long, long> map) =>
            string.Join(";", map.Select(pair =>
                pair.Key.ToString(CultureInfo.InvariantCulture) + ":" +
                pair.Value.ToString(CultureInfo.InvariantCulture)));

        public static IReadOnlyDictionary<long, long> Decode(string? encoded)
        {
            var map = new Dictionary<long, long>();
            if (string.IsNullOrWhiteSpace(encoded)) return map;

            foreach (var entry in encoded!.Split(';'))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)) continue;
                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;
                map[key] = value;
            }
            return map;
        }
    }
}
