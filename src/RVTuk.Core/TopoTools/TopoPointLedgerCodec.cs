using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// The per-toposolid ledger as one string: "lineId=x,y,z|x,y,z;lineId=x,y,z". Extensible
    /// Storage documents neither a 64-bit integer field nor a map key type, and Revit 2024+ element
    /// ids are 64-bit — the same corner <c>IdMapCodec</c> turned for Auto Dimensions.
    ///
    /// Coordinates use round-trip ("R") formatting: a ledger position is matched back to a live
    /// vertex within a tenth of a millimetre, so a lossy format would quietly orphan points.
    /// Decoding never throws — a ledger written by a future version, or damaged, must degrade to
    /// "this toposolid has no tracked points" rather than break the run.
    ///
    /// On net48 (Revit 2024) "R" carries the documented quirk of occasionally emitting digits that
    /// do not round-trip exactly; net8 emits the shortest exact form. Deliberately not swapped for
    /// "G17": the discrepancy is at the last bit of a double, some ten orders of magnitude below the
    /// 0.1 mm matching tolerance, while "G17" would lengthen every ledger by half again. The tests
    /// run on net8 only and so cannot see this difference — hence this note instead of a test.
    /// </summary>
    public static class TopoPointLedgerCodec
    {
        public static string Encode(IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger)
        {
            if (ledger == null) return "";

            return string.Join(";", ledger
                .Where(entry => entry.Value != null && entry.Value.Count > 0)
                .Select(entry =>
                    entry.Key.ToString(CultureInfo.InvariantCulture) + "=" +
                    string.Join("|", entry.Value.Select(Format))));
        }

        public static IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> Decode(string? encoded)
        {
            var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
            if (string.IsNullOrWhiteSpace(encoded)) return ledger;

            foreach (var entry in encoded!.Split(';'))
            {
                var halves = entry.Split('=');
                if (halves.Length != 2) continue;
                if (!long.TryParse(halves[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineId))
                    continue;

                var points = new List<XyzPoint>();
                foreach (var text in halves[1].Split('|'))
                {
                    var parts = text.Split(',');
                    if (parts.Length != 3) continue;
                    if (!TryParse(parts[0], out var x)) continue;
                    if (!TryParse(parts[1], out var y)) continue;
                    if (!TryParse(parts[2], out var z)) continue;
                    points.Add(new XyzPoint(x, y, z));
                }

                if (points.Count > 0) ledger[lineId] = points;
            }
            return ledger;
        }

        private static string Format(XyzPoint point) =>
            point.X.ToString("R", CultureInfo.InvariantCulture) + "," +
            point.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
            point.Z.ToString("R", CultureInfo.InvariantCulture);

        private static bool TryParse(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
