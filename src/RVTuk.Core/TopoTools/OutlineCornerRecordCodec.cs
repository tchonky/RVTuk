using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// One outline corner of a toposolid or floor that Topo Tools has raised: where it is in plan
    /// (X, Y), the height it had of its own (OwnZ), the height Topo Tools set (SetZ) — all internal
    /// feet — and whether the user has since moved it by hand (<c>ByHand</c>).
    /// </summary>
    public sealed record CornerRecord(double X, double Y, double OwnZ, double SetZ, bool ByHand);

    /// <summary>
    /// The corner records of one element as one string: entries <c>x,y,ownZ,setZ,flag</c> joined by
    /// <c>;</c>, numbers round-trip ("R") in the invariant culture, flag <c>0</c> or <c>hand</c>.
    /// Decoding never throws; a malformed entry is skipped and the rest survive. Same note on net48
    /// "R" as <see cref="TopoPointLedgerCodec"/>: last-bit differences only.
    /// </summary>
    public static class OutlineCornerRecordCodec
    {
        public static string Encode(IReadOnlyList<CornerRecord> records)
        {
            if (records == null || records.Count == 0) return "";

            var text = new StringBuilder();
            foreach (var record in records)
            {
                if (text.Length > 0) text.Append(';');
                text.Append(Format(record.X)).Append(',')
                    .Append(Format(record.Y)).Append(',')
                    .Append(Format(record.OwnZ)).Append(',')
                    .Append(Format(record.SetZ)).Append(',')
                    .Append(record.ByHand ? "hand" : "0");
            }
            return text.ToString();
        }

        public static IReadOnlyList<CornerRecord> Decode(string? encoded)
        {
            var records = new List<CornerRecord>();
            if (string.IsNullOrWhiteSpace(encoded)) return records;

            foreach (var entry in encoded!.Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length != 5) continue;
                if (!TryParse(parts[0], out var x)) continue;
                if (!TryParse(parts[1], out var y)) continue;
                if (!TryParse(parts[2], out var ownZ)) continue;
                if (!TryParse(parts[3], out var setZ)) continue;

                bool byHand;
                if (parts[4] == "0") byHand = false;
                else if (parts[4] == "hand") byHand = true;
                else continue;

                records.Add(new CornerRecord(x, y, ownZ, setZ, byHand));
            }
            return records;
        }

        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static bool TryParse(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
