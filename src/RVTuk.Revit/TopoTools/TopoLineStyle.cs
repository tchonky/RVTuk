using Autodesk.Revit.DB;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The dedicated line subcategory ("_DP-Topo Line") that marks a detail line as a topo
    /// contour — the same arrangement Auto Dimensions uses for its own styles. Created on the
    /// pane's first refresh, because you cannot draw the line before the style exists.
    /// </summary>
    public static class TopoLineStyle
    {
        public const string LineStyleName = "_DP-Topo Line";

        public static bool Exists(Document doc) => LineStyleCreator.Exists(doc, LineStyleName);

        /// <summary>Must be called inside an open transaction.</summary>
        public static void EnsureExists(Document doc) =>
            LineStyleCreator.EnsureExists(doc, LineStyleName);

        public static bool IsTopoLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
