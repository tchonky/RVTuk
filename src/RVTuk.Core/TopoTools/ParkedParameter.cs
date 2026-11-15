using System.Globalization;

namespace RVTuk.Core.TopoTools
{
    /// <summary>How a parameter is found again on another element.</summary>
    public enum ParkedKeyKind
    {
        /// <summary>Key is the <c>BuiltInParameter</c> as an invariant integer.</summary>
        BuiltIn,
        /// <summary>Key is the shared parameter's GUID.</summary>
        Shared,
        /// <summary>Key is the name of a project parameter that is neither built-in nor shared.</summary>
        Project,
    }

    /// <summary>Mirrors Revit's <c>StorageType</c>, minus <c>None</c>.</summary>
    public enum ParkedStorage { Integer, Double, String, ElementId }

    /// <summary>
    /// One instance parameter value, detached from its element. The converter snapshots a source's
    /// values this way before deleting it. Any the new element cannot take (a floor's Structural
    /// flag, a shared parameter bound to Floors only) are parked on it for the return trip.
    /// <see cref="Value"/> is always invariant text, so the record compares and encodes the same
    /// on net48 and net8.
    /// </summary>
    public sealed record ParkedParameter(ParkedKeyKind KeyKind, string Key, ParkedStorage Storage, string Value)
    {
        public static ParkedParameter OfInteger(ParkedKeyKind kind, string key, int value) =>
            new(kind, key, ParkedStorage.Integer, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>"R" formatting, as <see cref="TopoPointLedgerCodec"/> uses for the same reason.</summary>
        public static ParkedParameter OfDouble(ParkedKeyKind kind, string key, double value) =>
            new(kind, key, ParkedStorage.Double, value.ToString("R", CultureInfo.InvariantCulture));

        public static ParkedParameter OfString(ParkedKeyKind kind, string key, string value) =>
            new(kind, key, ParkedStorage.String, value ?? "");

        public static ParkedParameter OfElementId(ParkedKeyKind kind, string key, long value) =>
            new(kind, key, ParkedStorage.ElementId, value.ToString(CultureInfo.InvariantCulture));

        public bool TryGetInteger(out int value) =>
            int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        public bool TryGetDouble(out double value) =>
            double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        public bool TryGetElementId(out long value) =>
            long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
