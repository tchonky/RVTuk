namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// One row of the pane's list: what this line is about to do, before anything is pressed — and,
    /// since the height lives in storage rather than in Properties, the only place to set one.
    ///
    /// <see cref="ElevationText"/> is round-trippable text produced by the Revit layer through
    /// <c>UnitFormatUtils</c> (empty when no height is set), because only that layer knows the
    /// document's units; the same layer parses whatever comes back. Core never guesses at
    /// millimetres in either direction.
    /// </summary>
    public sealed record TopoLineInfo(
        long LineId,
        string ElevationText,
        string LengthText,
        int PointCount,
        TopoLineStatus Status)
    {
        public string DisplayName => $"Line {LineId}";

        public string StatusText => Status switch
        {
            TopoLineStatus.Ready => $"{LengthText} · {PointCount} point(s)",
            TopoLineStatus.NoElevation => $"{LengthText} · no height set",
            TopoLineStatus.OutsideToposolid => $"{LengthText} · outside every toposolid",
            _ => LengthText,
        };

        public bool IsReady => Status == TopoLineStatus.Ready;
    }
}
