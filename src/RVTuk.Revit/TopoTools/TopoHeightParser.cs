using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Reads a height text as a shared (survey) elevation, in the project's length units, through
    /// Revit's own parser — so "42750", "42.75 m" and the locale's decimal separator all work as
    /// they do in any Revit length box. The result is in feet and still shared: the caller
    /// converts it to an internal Z.
    /// </summary>
    public static class TopoHeightParser
    {
        public static bool TryParse(Document doc, string? text, out double sharedFeet)
        {
            sharedFeet = 0;
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return false;

            return UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, trimmed, out sharedFeet);
        }
    }
}
