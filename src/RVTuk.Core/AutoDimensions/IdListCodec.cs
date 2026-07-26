using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Element ids as one comma-separated string, so they can live in a single Extensible
    /// Storage string field (ES doesn't document a 64-bit integer field type, and the ids are
    /// 64-bit from Revit 2024 on). Unparseable entries are dropped rather than throwing — a
    /// truncated entity should cost the selection, not the run.
    /// </summary>
    public static class IdListCodec
    {
        public static string Encode(IEnumerable<long> ids) =>
            string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

        public static IReadOnlyList<long> Decode(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return Array.Empty<long>();

            var ids = new List<long>();
            foreach (var part in encoded!.Split(','))
            {
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    ids.Add(id);
            }
            return ids;
        }
    }
}
