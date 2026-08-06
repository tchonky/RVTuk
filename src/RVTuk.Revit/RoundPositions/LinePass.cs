using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Straight-line elements — walls, framing, model and detail lines. Only exactly-orthogonal
    /// ones move, and they slide parallel to themselves.
    /// </summary>
    public static class LinePass
    {
        public static void Run(
            Document doc, IReadOnlyList<Element> elements, double step, RoundingReportBuilder report)
        {
            foreach (var element in elements)
            {
                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    report.RecordSkipped(element.Id.Value, ElementEligibility.CategoryName(element), skip.Value);
                    continue;
                }

                if (!(element.Location is LocationCurve locationCurve) ||
                    !(locationCurve.Curve is Line line))
                {
                    report.RecordSkipped(
                        element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.NotOrthogonal);
                    continue;
                }

                var start = ToGridPoint(line.GetEndPoint(0));
                var end = ToGridPoint(line.GetEndPoint(1));
                var offset = LineOffset.For(start, end, step, AxisClassifier.ToleranceCentimetres);

                switch (offset.Outcome)
                {
                    case LineOffsetOutcome.AlreadyOnGrid:
                        report.RecordAlreadyClean();
                        break;

                    case LineOffsetOutcome.NotOrthogonal:
                        report.RecordSkipped(
                            element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.NotOrthogonal);
                        break;

                    case LineOffsetOutcome.Offset:
                        var translation = new XYZ(
                            GridSnapper.ToFeet(offset.DeltaX), GridSnapper.ToFeet(offset.DeltaY), 0);

                        if (MoveAttempt.TryMove(doc, element, translation, report))
                        {
                            report.RecordMoved();
                        }
                        else
                        {
                            report.RecordSkipped(
                                element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.RevitRejected);
                        }
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unhandled {nameof(LineOffsetOutcome)}: {offset.Outcome}");
                }
            }
        }

        private static GridPoint ToGridPoint(XYZ point) => new GridPoint(
            GridSnapper.FromFeet(point.X), GridSnapper.FromFeet(point.Y), GridSnapper.FromFeet(point.Z));
    }
}
