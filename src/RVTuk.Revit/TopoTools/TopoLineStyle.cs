using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The dedicated line subcategory ("_DP-Topo Line") that marks a detail line as a topo
    /// contour — the same arrangement Auto Dimensions uses for its own styles. Created when a
    /// session starts, because the session's updater gives new lines this style, and created
    /// orange (<see cref="Colour"/>); a style that already exists keeps the colour it has.
    ///
    /// The style is found by this name and nothing else. Renaming it in the project disconnects
    /// every topo line already drawn: none counts as a topo line any more, so Update ignores them
    /// (their points stay on the element, unreported) and the next session creates a new style.
    /// </summary>
    public static class TopoLineStyle
    {
        /// <summary>The style's name, and the only way it is found: renaming the style in the
        /// project disconnects every topo line already drawn.</summary>
        public const string LineStyleName = "_DP-Topo Line";

        /// <summary>
        /// Topo Tools' orange, RGB 255, 140, 0, given to this style and to <see cref="HeightTextType"/>
        /// only when Topo Tools creates them. A new instance on every read, so no Revit API object
        /// is made when this type loads.
        /// </summary>
        public static Color Colour => new Color(255, 140, 0);

        public static bool IsTopoLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
