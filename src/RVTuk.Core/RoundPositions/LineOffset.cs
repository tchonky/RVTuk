using System;

namespace RVTuk.Core.RoundPositions
{
    public enum LineOffsetOutcome
    {
        Offset,
        AlreadyOnGrid,
        NotOrthogonal,
    }

    /// <summary>The translation that brings one straight line onto the grid, in centimetres. Only
    /// ever one of the two deltas is non-zero.</summary>
    public readonly record struct LineOffsetResult(
        LineOffsetOutcome Outcome, double DeltaX, double DeltaY);

    /// <summary>
    /// v1 moves a line parallel to itself: the coordinate it does not run along is snapped, and
    /// the along-axis coordinates are left exactly where they were. One delta carries both
    /// endpoints, so a line's own length and angle cannot change — snapping each endpoint
    /// separately is what would deform it. This says nothing about a corner two walls share:
    /// the horizontal one snaps its Y and the vertical one its X, so the corner does land in
    /// two places, and closing it is Revit's wall-join maintenance to do.
    /// </summary>
    public static class LineOffset
    {
        public static LineOffsetResult For(
            GridPoint start, GridPoint end, double step, double toleranceCentimetres)
        {
            var axis = AxisClassifier.Classify(end.X - start.X, end.Y - start.Y, toleranceCentimetres);

            var deltaX = 0.0;
            var deltaY = 0.0;
            switch (axis)
            {
                case LineAxis.AlongX:
                    deltaY = GridSnapper.Snap(start.Y, step) - start.Y;
                    break;
                case LineAxis.AlongY:
                    deltaX = GridSnapper.Snap(start.X, step) - start.X;
                    break;
                default:
                    return new LineOffsetResult(LineOffsetOutcome.NotOrthogonal, 0, 0);
            }

            var moves = Math.Abs(deltaX) > GridSnapper.EpsilonCentimetres
                     || Math.Abs(deltaY) > GridSnapper.EpsilonCentimetres;

            return moves
                ? new LineOffsetResult(LineOffsetOutcome.Offset, deltaX, deltaY)
                : new LineOffsetResult(LineOffsetOutcome.AlreadyOnGrid, 0, 0);
        }
    }
}
