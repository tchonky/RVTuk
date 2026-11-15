using System;

namespace RVTuk.Core.RoundPositions
{
    /// <summary>Which world axis a line runs along, in plan.</summary>
    public enum LineAxis
    {
        /// <summary>Runs along X, so its Y is the coordinate that gets snapped.</summary>
        AlongX,

        /// <summary>Runs along Y, so its X is the coordinate that gets snapped.</summary>
        AlongY,

        /// <summary>Diagonal, near-orthogonal or degenerate. v1 skips all three.</summary>
        NotOrthogonal,
    }

    public static class AxisClassifier
    {
        /// <summary>
        /// A displacement, not an angle. An angular tolerance scales with length — 0.01° is float
        /// dust on a 40 cm wall and 0.7 cm of real drift on a 40 m one — so the same tolerance
        /// would mean two different things depending on what it was applied to.
        /// </summary>
        public const double ToleranceCentimetres = 0.01;

        /// <summary>
        /// <paramref name="deltaX"/> and <paramref name="deltaY"/> are in centimetres like the
        /// tolerance; Revit callers convert from feet with <see cref="GridSnapper.FromFeet"/>.
        /// </summary>
        public static LineAxis Classify(double deltaX, double deltaY, double toleranceCentimetres)
        {
            var spansX = Math.Abs(deltaX) > toleranceCentimetres;
            var spansY = Math.Abs(deltaY) > toleranceCentimetres;

            if (spansX && !spansY) return LineAxis.AlongX;
            if (spansY && !spansX) return LineAxis.AlongY;
            return LineAxis.NotOrthogonal;
        }
    }
}
