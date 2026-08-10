using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Lengths as plain numbers in the document's display unit — "42750", never "42750.0". Survey
    /// elevations come in round, and a column of them is easier to scan without a decimal point
    /// that never carries anything.
    ///
    /// This first went through Revit's own <c>FormatOptions.SuppressTrailingZeros</c>, which did
    /// nothing in practice. Two candidate reasons, indistinguishable without a Revit session:
    /// <c>forEditing: true</c> asks for a round-trippable string and may ignore format options
    /// altogether, and <c>CanSuppressTrailingZeros()</c> can simply return false for a given unit
    /// setup, in which case the flag was never set. Converting and formatting the number here
    /// depends on neither.
    ///
    /// The trade: no digit grouping and no unit symbol. For a box that must parse back that is what
    /// you want anyway, and one format for both display and editing means the two can never drift.
    ///
    /// Rounded before formatting because feet are the internal unit: a height typed as 42750 mm
    /// comes back as 42750.000000000004, and twelve decimal places of float residue is not a
    /// number anyone asked to see.
    /// </summary>
    public static class TopoLengthFormatter
    {
        /// <summary>Well below any surveyed precision, well above float residue.</summary>
        private const int Decimals = 6;

        public static string Format(Document doc, double feet)
        {
            double value = UnitUtils.ConvertFromInternalUnits(feet, DisplayUnit(doc));

            // CurrentCulture, not invariant: the number goes into a box the user reads and edits,
            // and comes back through UnitFormatUtils.TryParse, which is locale-aware too.
            return Math.Round(value, Decimals).ToString("0.######", CultureInfo.CurrentCulture);
        }

        private static ForgeTypeId DisplayUnit(Document doc) =>
            doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
    }
}
