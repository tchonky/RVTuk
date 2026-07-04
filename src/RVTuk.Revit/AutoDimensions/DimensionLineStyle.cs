using Autodesk.Revit.DB;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The dedicated line subcategory ("Dimensions_Line") that marks a detail line as a
    /// reference for Auto Dimensions. Auto-creates on first use — no separate setup step.
    /// </summary>
    public static class DimensionLineStyle
    {
        public const string LineStyleName = "Dimensions_Line";

        public static void EnsureExists(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory.SubCategories.Contains(LineStyleName)) return;
            doc.Settings.Categories.NewSubcategory(linesCategory, LineStyleName);
        }

        public static bool IsDimensionsLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
