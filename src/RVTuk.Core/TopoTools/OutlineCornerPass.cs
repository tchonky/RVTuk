using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What the outline corner pass of one Update needs (spec 2026-11-19): the rule prepared from
    /// the view's lines, null when the view has no line with a readable height (the pass then only
    /// puts back what earlier Updates set), and the shared elevation of the internal origin, which
    /// turns the rule's shared metres into internal Z as the session runner does. Also the
    /// conversions from Revit's internal feet into the rule's metres.
    /// </summary>
    public sealed record CornerPass(OutlineCornerHeights? Heights, double SharedElevationOfInternalZero)
    {
        /// <summary>The internal Z the rule gives a vertex (internal feet), or null when it is left alone.</summary>
        public double? TargetZ(XyzPoint vertex)
        {
            if (Heights == null) return null;
            double? metres = Heights.HeightAt(new XyPoint(
                vertex.X * SurveyHeightText.MetresPerFoot, vertex.Y * SurveyHeightText.MetresPerFoot));
            return metres == null ? null : metres.Value / SurveyHeightText.MetresPerFoot - SharedElevationOfInternalZero;
        }

        /// <summary>One topo line for the rule: its shared height and its plan polyline, both from
        /// internal feet into metres.</summary>
        public static CornerLine Line(double sharedFeet, IReadOnlyList<XyPoint> polylineFeet) =>
            new CornerLine(
                sharedFeet * SurveyHeightText.MetresPerFoot,
                polylineFeet
                    .Select(xy => new XyPoint(xy.X * SurveyHeightText.MetresPerFoot, xy.Y * SurveyHeightText.MetresPerFoot))
                    .ToList());

        /// <summary>The rule's chord count (<see cref="OutlineCornerHeights.ArcChordCount"/>) for an
        /// arc given in internal feet: its sweep is length / radius, its radius goes into metres.</summary>
        public static int ArcChordCount(double lengthFeet, double radiusFeet) =>
            radiusFeet > 0
                ? OutlineCornerHeights.ArcChordCount(lengthFeet / radiusFeet, radiusFeet * SurveyHeightText.MetresPerFoot)
                : 1;
    }

    /// <summary>What an Update does before it touches the element.</summary>
    public enum CornerPassStart
    {
        /// <summary>Nothing to do: leave the element exactly as it is.</summary>
        Skip,

        /// <summary>Nothing to place, and a plain element: delete its stale corner record, if any, and stop.</summary>
        DropRecords,

        /// <summary>Go on: switch shape editing on if it is off, place the lines' points, run the pass.</summary>
        Run,
    }

    /// <summary>
    /// When the applier runs, switches a plain element to shape editing, and makes a floor plain
    /// again. A floor none of whose corners takes a height from the lines must stay a plain floor
    /// (README row 24), so for a plain element with nothing to place the pass is first worked out
    /// dry, without switching anything on (ruling R2).
    /// </summary>
    public static class CornerPassGate
    {
        /// <param name="lineWork">There are line points to place or earlier points to take out.</param>
        /// <param name="pass">The corner pass, or null for none (Cancel).</param>
        /// <param name="editorEnabled">The element's shape editing is on.</param>
        /// <param name="recordCount">How many corner records the element has (read only when it is on).</param>
        /// <param name="plainNeedsEditing">The dry plan for a plain element; asked only when it is
        /// plain, has nothing to place, and the view has a rule.</param>
        public static CornerPassStart Start(
            bool lineWork, CornerPass? pass, bool editorEnabled, int recordCount, Func<bool> plainNeedsEditing)
        {
            if (lineWork) return CornerPassStart.Run;
            if (pass == null) return CornerPassStart.Skip;
            if (!editorEnabled)
                return pass.Heights != null && plainNeedsEditing() ? CornerPassStart.Run : CornerPassStart.DropRecords;
            return pass.Heights == null && recordCount == 0 ? CornerPassStart.Skip : CornerPassStart.Run;
        }

        /// <summary>
        /// A floor switched to shape editing for the corner pass alone (plain before, no line
        /// work), whose pass then changed no corner, is made plain again, as SlabShape.Replay does
        /// with a floor it enabled for nothing. A toposolid is left as it is.
        /// </summary>
        /// <param name="changed">Corners the pass actually moved (<see cref="CornerOutcome.Changed"/>).</param>
        public static bool ResetToPlain(bool wasEnabled, bool lineWork, bool isFloor, int changed) =>
            !wasEnabled && !lineWork && isFloor && changed == 0;
    }
}
