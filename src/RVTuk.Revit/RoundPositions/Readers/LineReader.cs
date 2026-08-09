using System;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>
    /// Straight-line elements — walls, framing, model and detail lines. Only exactly-orthogonal
    /// ones move, and they slide parallel to themselves so no length or angle can change.
    /// </summary>
    public static class LineReader
    {
        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            if (!(element.Location is LocationCurve locationCurve) ||
                !(locationCurve.Curve is Line line))
            {
                return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);
            }

            var start = ToGridPoint(line.GetEndPoint(0));
            var end = ToGridPoint(line.GetEndPoint(1));
            var offset = LineOffset.For(start, end, step, AxisClassifier.ToleranceCentimetres);

            switch (offset.Outcome)
            {
                case LineOffsetOutcome.AlreadyOnGrid:
                    return OffsetVerdict.Clean();

                case LineOffsetOutcome.NotOrthogonal:
                    return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);

                case LineOffsetOutcome.Offset:
                    return OffsetVerdict.Move(new XYZ(
                        GridSnapper.ToFeet(offset.DeltaX), GridSnapper.ToFeet(offset.DeltaY), 0));

                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(LineOffsetOutcome)}: {offset.Outcome}");
            }
        }

        private static GridPoint ToGridPoint(XYZ point) => new GridPoint(
            GridSnapper.FromFeet(point.X), GridSnapper.FromFeet(point.Y), GridSnapper.FromFeet(point.Z));
    }
}
