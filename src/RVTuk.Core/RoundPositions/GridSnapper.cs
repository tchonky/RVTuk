using System;

namespace RVTuk.Core.RoundPositions
{
    /// <summary>
    /// The whole-centimetre grid every position lands on, anchored at Revit's internal origin.
    /// The step is the user's choice, so it travels as a parameter rather than sitting in a
    /// constant.
    /// </summary>
    public static class GridSnapper
    {
        public const double CentimetresPerFoot = 30.48;

        public const double HalfCentimetreStep = 0.5;
        public const double WholeCentimetreStep = 1.0;

        /// <summary>Values equal to within this are the same value — float dust, not drift.</summary>
        public const double EpsilonCentimetres = 1e-9;

        public static double StepFor(bool allowHalfCentimetres) =>
            allowHalfCentimetres ? HalfCentimetreStep : WholeCentimetreStep;

        public static double FromFeet(double feet) => feet * CentimetresPerFoot;

        public static double ToFeet(double centimetres) => centimetres / CentimetresPerFoot;

        /// <summary>
        /// AwayFromZero rather than .NET's banker's rounding: an exact midpoint must resolve the
        /// same distance from the origin on both sides, so on the 1 cm grid 0.5 goes to 1.0 and
        /// −0.5 to −1.0.
        /// </summary>
        public static double Snap(double centimetres, double step) =>
            Math.Round(centimetres / step, MidpointRounding.AwayFromZero) * step;

        public static GridPoint Snap(GridPoint point, double step) =>
            new GridPoint(Snap(point.X, step), Snap(point.Y, step), Snap(point.Z, step));

        public static bool IsOnGrid(double centimetres, double step) =>
            Math.Abs(centimetres - Snap(centimetres, step)) <= EpsilonCentimetres;
    }
}
