using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// One category's half of both operations. Scan and Apply share the same collector and the
    /// same reader, so the tree can never promise something the fix would decide differently.
    /// </summary>
    public sealed class CategoryPass
    {
        private readonly Func<Document, IReadOnlyList<Element>> _collect;
        private readonly Func<Document, Element, double, OffsetVerdict> _read;
        private readonly Func<Document, Element, XYZ, RoundingReportBuilder, bool>? _applyOne;

        /// <param name="applyOne">
        /// Set only for categories that cannot be moved by a transform — a Level moves by having
        /// its Elevation written. When null, moves go through the batch mover.
        /// </param>
        public CategoryPass(
            FixTier tier,
            string category,
            Func<Document, IReadOnlyList<Element>> collect,
            Func<Document, Element, double, OffsetVerdict> read,
            Func<Document, Element, XYZ, RoundingReportBuilder, bool>? applyOne = null)
        {
            Tier = tier;
            Category = category;
            _collect = collect;
            _read = read;
            _applyOne = applyOne;
        }

        public FixTier Tier { get; }

        public string Category { get; }

        public void Scan(
            Document doc, double step, ScanResultBuilder builder,
            CancellationToken cancellation = default)
        {
            foreach (var element in _collect(doc))
            {
                if (cancellation.IsCancellationRequested) break;

                builder.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    builder.RecordSkipped(element.Id.Value, Category, skip.Value);
                    continue;
                }

                var verdict = _read(doc, element, step);
                switch (verdict.Outcome)
                {
                    case OffsetOutcome.AlreadyOnGrid:
                        builder.RecordAlreadyClean();
                        break;

                    case OffsetOutcome.CannotFix:
                        builder.RecordSkipped(element.Id.Value, Category, verdict.Reason);
                        break;

                    case OffsetOutcome.NeedsMove:
                        builder.RecordDirty(new DirtyElement(
                            element.Id.Value, Tier, Category,
                            LevelNaming.NameOf(doc, element),
                            LevelNaming.ElevationCentimetresOf(doc, element),
                            ElementNaming.NameOf(element)));
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unhandled {nameof(OffsetOutcome)}: {verdict.Outcome}");
                }
            }
        }

        public void Apply(
            Document doc, double step, ISet<long> ids, int batchSize, RoundingReportBuilder report,
            CancellationToken cancellation = default)
        {
            var moves = new List<(Element, XYZ)>();

            foreach (var element in _collect(doc))
            {
                if (cancellation.IsCancellationRequested) break;
                if (!ids.Contains(element.Id.Value)) continue;

                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    report.RecordSkipped(element.Id.Value, Category, skip.Value);
                    continue;
                }

                // Re-read rather than trusting the scan: the model can have changed since, and an
                // element that is already clean now is passed over rather than treated as an error.
                var verdict = _read(doc, element, step);
                switch (verdict.Outcome)
                {
                    case OffsetOutcome.AlreadyOnGrid:
                        report.RecordAlreadyClean();
                        break;

                    case OffsetOutcome.CannotFix:
                        report.RecordSkipped(element.Id.Value, Category, verdict.Reason);
                        break;

                    case OffsetOutcome.NeedsMove:
                        if (_applyOne == null)
                        {
                            moves.Add((element, verdict.Translation!));
                        }
                        else if (_applyOne(doc, element, verdict.Translation!, report))
                        {
                            report.RecordMoved();
                        }
                        else
                        {
                            report.RecordSkipped(element.Id.Value, Category, SkipReason.RevitRejected);
                        }
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unhandled {nameof(OffsetOutcome)}: {verdict.Outcome}");
                }
            }

            if (moves.Count == 0) return;

            var moved = BatchMover.Move(doc, moves, batchSize, report, cancellation);
            for (var i = 0; i < moved; i++) report.RecordMoved();
        }
    }

    /// <summary>The level a dirty element is filed under in the tree.</summary>
    internal static class LevelNaming
    {
        public static string NameOf(Document doc, Element element) =>
            (doc.GetElement(element.LevelId) as Level)?.Name ?? string.Empty;

        public static double? ElevationCentimetresOf(Document doc, Element element)
        {
            var level = doc.GetElement(element.LevelId) as Level;
            return level == null ? (double?)null : GridSnapper.FromFeet(level.Elevation);
        }
    }

    internal static class ElementNaming
    {
        /// <summary>
        /// The id is always shown because a name alone is rarely unique — a plan can hold two
        /// hundred elements all called "Basic Wall".
        /// </summary>
        public static string NameOf(Element element)
        {
            var name = element.Name;
            return string.IsNullOrWhiteSpace(name)
                ? element.Id.Value.ToString()
                : $"{name} · {element.Id.Value}";
        }
    }
}
