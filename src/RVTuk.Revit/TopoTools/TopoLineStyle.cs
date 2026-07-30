using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The dedicated line subcategory ("Topo_Line") that marks a detail line as a topo contour —
    /// the same arrangement Auto Dimensions uses for "Dimensions_Line". Unlike that one this is not
    /// auto-created on first use: you cannot draw the line before the style exists, so creating it
    /// is the pane's explicit setup action.
    /// </summary>
    public static class TopoLineStyle
    {
        public const string LineStyleName = "Topo_Line";

        public static bool Exists(Document doc) =>
            doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)
                .SubCategories.Contains(LineStyleName);

        public static void EnsureExists(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory.SubCategories.Contains(LineStyleName)) return;
            doc.Settings.Categories.NewSubcategory(linesCategory, LineStyleName);
        }

        public static bool IsTopoLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
