using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Lengths written the way the document would write them, minus the trailing zeros — "42750"
    /// rather than "42750.0". Survey elevations come in round, and a column of them is easier to
    /// scan without a decimal point that never carries anything.
    ///
    /// Done through Revit's own <c>FormatOptions.SuppressTrailingZeros</c> rather than by trimming
    /// the string: the separator, digit grouping and unit symbol are all locale-dependent, and
    /// chopping characters off the end of "1 234,50" is how that goes wrong.
    ///
    /// <c>UseDefault</c> must be cleared on the copy — while it is set, Revit ignores every other
    /// setting on the object and formats with the document's defaults instead.
    /// </summary>
    public static class TopoLengthFormatter
    {
        /// <param name="forEditing">
        /// True for text destined for an editable box, which must parse back exactly; false for
        /// display.
        /// </param>
        public static string Format(Document doc, double feet, bool forEditing)
        {
            var units = doc.GetUnits();

            try
            {
                var format = new FormatOptions(units.GetFormatOptions(SpecTypeId.Length))
                {
                    UseDefault = false,
                };
                if (format.CanSuppressTrailingZeros()) format.SuppressTrailingZeros = true;

                var options = new FormatValueOptions();
                options.SetFormatOptions(format);

                return UnitFormatUtils.Format(units, SpecTypeId.Length, feet, forEditing, options);
            }
            catch
            {
                // A unit setup that will not take the override still deserves a number.
                return UnitFormatUtils.Format(units, SpecTypeId.Length, feet, forEditing);
            }
        }
    }
}
