namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// One row of the pane's list: what this line is about to do, before anything is pressed.
    /// The two texts are pre-formatted by the Revit layer through <c>UnitFormatUtils</c>, because
    /// only it knows the document's display units — Core never guesses at millimetres.
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
            TopoLineStatus.Ready => $"{ElevationText} · {LengthText} · {PointCount} point(s)",
            TopoLineStatus.NoElevation => $"{LengthText} · no elevation set",
            TopoLineStatus.OutsideToposolid => $"{ElevationText} · outside every toposolid",
            _ => ElevationText,
        };

        public bool IsReady => Status == TopoLineStatus.Ready;
    }
}
