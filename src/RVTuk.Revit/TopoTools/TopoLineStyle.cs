using Autodesk.Revit.DB;

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

        public static bool IsTopoLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
