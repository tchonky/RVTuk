using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>What one toposolid's share of a run did.</summary>
    public sealed record ApplyOutcome(int Added, int Removed, int Missing);

    /// <summary>
    /// Writes one toposolid's points, and the only place that copes with a point having no
    /// identity: <c>SlabShapeVertex</c> is a position, not an element, so the ledger stores
    /// coordinates and a re-run matches them back within a tenth of a millimetre.
    ///
    /// The positions recorded are the ones <c>AddPoints</c> hands back, not the ones asked for, so
    /// snapping or rounding inside Revit cannot drift the ledger away from the model. A recorded
    /// point with no vertex within tolerance was moved or deleted by hand since the last run: it is
    /// left alone and counted, never guessed at.
    /// </summary>
    public static class TopoPointApplier
    {
        /// <summary>0.1 mm, in Revit's internal feet.</summary>
        public const double MatchToleranceFeet = 0.1 / 304.8;

        /// <summary>Caller must already be inside a transaction.</summary>
        public static ApplyOutcome Apply(
            ToposolidTarget target,
            IReadOnlyCollection<long> lineIdsSeen,
            IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> newPointsByLine,
            Func<long, bool> lineStillExists)
        {
            var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
            foreach (var entry in TopoLedgerStore.Read(target.Solid)) ledger[entry.Key] = entry.Value;

            var editor = target.Solid.GetSlabShapeEditor();
            if (editor == null) return new ApplyOutcome(0, 0, 0);
            if (!editor.IsEnabled) editor.Enable();

            // Every line this run saw, plus every line since deleted. Not only the lines producing
            // points: a line whose elevation was cleared must lose last run's points too.
            var staleLineIds = ledger.Keys
                .Where(lineId => lineIdsSeen.Contains(lineId) || !lineStillExists(lineId))
                .ToList();

            int removed = 0;
            int missing = 0;

            if (staleLineIds.Count > 0)
            {
                var live = new List<SlabShapeVertex>();
                foreach (SlabShapeVertex vertex in editor.SlabShapeVertices) live.Add(vertex);

                foreach (var lineId in staleLineIds)
                {
                    foreach (var recorded in ledger[lineId])
                    {
                        var match = FindVertex(live, recorded);
                        if (match == null)
                        {
                            missing++;
                            continue;
                        }

                        live.Remove(match);
                        if (match.IsValidObject && editor.DeletePoint(match)) removed++;
                        else missing++;
                    }
                    ledger.Remove(lineId);
                }
            }

            int added = 0;
            foreach (var entry in newPointsByLine)
            {
                if (entry.Value.Count == 0) continue;

                IList<XYZ> points = entry.Value
                    .Select(point => new XYZ(point.X, point.Y, point.Z))
                    .ToList();

                IList<SlabShapeVertex> created = editor.AddPoints(points);

                var recorded = new List<XyzPoint>();
                foreach (var vertex in created)
                {
                    var position = vertex.Position;
                    recorded.Add(new XyzPoint(position.X, position.Y, position.Z));
                }

                if (recorded.Count > 0) ledger[entry.Key] = recorded;
                added += recorded.Count;
            }

            TopoLedgerStore.Write(target.Solid, ledger);
            return new ApplyOutcome(added, removed, missing);
        }

        private static SlabShapeVertex? FindVertex(List<SlabShapeVertex> live, XyzPoint recorded)
        {
            foreach (var vertex in live)
            {
                var position = vertex.Position;
                if (Math.Abs(position.X - recorded.X) <= MatchToleranceFeet &&
                    Math.Abs(position.Y - recorded.Y) <= MatchToleranceFeet &&
                    Math.Abs(position.Z - recorded.Z) <= MatchToleranceFeet)
                {
                    return vertex;
                }
            }
            return null;
        }
    }
}
