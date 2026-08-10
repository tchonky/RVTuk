using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The dedicated line subcategories Auto Dimensions drives off. Two of them are dimension
    /// strings — an outer ring and an inner one, which differ only in who owns a contested
    /// opening (see CandidateMatcher) — and the third points at a reference the automatic pass
    /// structurally cannot find (see WallEndResolver). All three auto-create on first use.
    /// </summary>
    public static class DimensionLineStyle
    {
        public const string OuterLineStyleName = "_DP-Dim Outer";
        public const string InnerLineStyleName = "_DP-Dim Inner";
        public const string RefLineStyleName = "_DP-Dim Ref";

        public static readonly string[] AllStyleNames =
        {
            OuterLineStyleName, InnerLineStyleName, RefLineStyleName,
        };

        /// <summary>Must be called inside an open transaction.</summary>
        public static void EnsureExists(Document doc)
        {
            foreach (var name in AllStyleNames) LineStyleCreator.EnsureExists(doc, name);
        }

        /// <summary>
        /// Whether this is a dimension string, and which ring it belongs to. False for a ref
        /// line, which is not a string and never receives a dimension of its own.
        /// </summary>
        public static bool TryGetRing(CurveElement curveElement, out DimensionRing ring)
        {
            ring = DimensionRing.Outer;

            var name = StyleName(curveElement);
            if (name == OuterLineStyleName) return true;
            if (name == InnerLineStyleName)
            {
                ring = DimensionRing.Inner;
                return true;
            }
            return false;
        }

        public static bool IsRefLine(CurveElement curveElement) =>
            StyleName(curveElement) == RefLineStyleName;

        private static string? StyleName(CurveElement curveElement) =>
            curveElement.LineStyle is GraphicsStyle style
                ? style.GraphicsStyleCategory?.Name
                : null;
    }
}
