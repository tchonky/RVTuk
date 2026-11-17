using System;
using System.Globalization;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class SurveyHeightTextTests
{
    private static double Feet(double metres) => metres / SurveyHeightText.MetresPerFoot;

    [Theory]
    [InlineData(42.7, "42.70")]
    [InlineData(42.755, "42.755")]
    [InlineData(0.0, "0.00")]
    [InlineData(-3.2, "-3.20")]
    [InlineData(-0.0000001, "0.00")]   // −0 is folded
    [InlineData(42.7500004, "42.75")]  // float residue goes at 6 decimals
    [InlineData(1234.5, "1234.50")]    // no grouping
    [InlineData(42.123456, "42.123456")]  // up to six decimals
    [InlineData(42.12345649, "42.123456")]        // rounded once, at six decimals: not 42.123457
    [InlineData(2.0000005, "2.000001")]           // a midpoint rounds away from zero: not 2.00
    [InlineData(42.12345649999997, "42.123456")]  // just below a midpoint: rounded once, not twice
    public void FormatWritesMetresWithAtLeastTwoDecimals(double metres, string expected)
        => Assert.Equal(expected, SurveyHeightText.Format(Feet(metres)));

    [Theory]
    [InlineData("42.75", 42.75)]
    [InlineData("42,75", 42.75)]
    [InlineData("+42.75", 42.75)]
    [InlineData("-3.2", -3.2)]
    [InlineData("\u22123.2", -3.2)]           // minus sign U+2212
    [InlineData("42.75 m", 42.75)]
    [InlineData("42.75M", 42.75)]
    [InlineData("\u00A042.75\u00A0", 42.75)]  // non-breaking spaces
    [InlineData("\u200F42.75", 42.75)]        // RLM
    [InlineData("\u200E42.75\u200E", 42.75)]  // LRM wrap
    [InlineData("\u206642.75\u2069", 42.75)]  // LRI … PDI
    [InlineData("\u061C42.75", 42.75)]        // ALM
    [InlineData("\u200B42.75\uFEFF", 42.75)]  // zero-width space, BOM
    [InlineData("42.75 m\u200F", 42.75)]      // a mark after the "m": marks go before the "m" rule
    [InlineData("\u200E-3.20", -3.2)]         // LRM before a negative height
    [InlineData("-\u200E3.20", -3.2)]         // a mark inside the text
    [InlineData("\u202B42.75\u202C", 42.75)]  // RLE … PDF
    [InlineData("\u202A42.75\u202C", 42.75)]  // LRE … PDF
    [InlineData("\u202D42.75\u202C", 42.75)]  // LRO … PDF
    [InlineData("\u202E42.75\u202C", 42.75)]  // RLO … PDF
    [InlineData("\u206742.75\u2069", 42.75)]  // RLI … PDI
    [InlineData("\u206842.75\u2069", 42.75)]  // FSI … PDI
    [InlineData("42.75\r", 42.75)]            // Revit ends a text note's text with a carriage return
    [InlineData("1,050", 1.05)]               // a single comma is the decimal point
    [InlineData("3000", 3000.0)]
    [InlineData("-500", -500.0)]
    public void TryParseReadsMetres(string text, double metres)
    {
        Assert.True(SurveyHeightText.TryParse(text, out double feet));
        Assert.Equal(metres, feet * SurveyHeightText.MetresPerFoot, 9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("±0.00")]     // the building's own zero, typed out of habit
    [InlineData("4275 cm")]
    [InlineData("42750 mm")]
    [InlineData("42.75 mm")]  // only the last 'm' is dropped: not 42.75 m
    [InlineData("42.75mm")]
    [InlineData("2750 mm")]   // millimetres inside the range: not 2750 m
    [InlineData("=42+1")]
    [InlineData("4 275")]
    [InlineData("1 234")]            // a space inside the range: not 1234 m
    [InlineData("1\u00A0234")]   // nor a non-breaking space
    [InlineData("1,234.5")]
    [InlineData("1.2.3")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("42ץ75")]     // typed with the Hebrew keyboard on
    [InlineData("42.7x")]
    [InlineData("3000.01")]
    [InlineData("-500.01")]
    [InlineData("4275")]      // a forgotten point on a site above 30 m
    [InlineData("+-3")]
    [InlineData("3.20-")]            // a trailing minus, the way a right-to-left line shows -3.20
    [InlineData("(3.20)")]           // brackets are not a minus
    [InlineData("\u00AD3.20")]   // a soft hyphen is not a minus
    [InlineData("\u200D42.75")]  // a zero-width joiner is not on the drop list
    [InlineData("m")]
    public void TryParseRefuses(string? text) => Assert.False(SurveyHeightText.TryParse(text, out _));

    [Fact]
    public void EveryWrittenHeightReadsBackToItself()
    {
        for (double m = -500; m <= 3000; m += 0.137)
        {
            Assert.True(SurveyHeightText.TryParse(SurveyHeightText.Format(Feet(m)), out double feet));
            Assert.Equal(Math.Round(m, 6), feet * SurveyHeightText.MetresPerFoot, 6);
        }
    }

    [Fact]
    public void FeetAreInternationalFeet()
    {
        Assert.Equal("30.48", SurveyHeightText.Format(100.0));  // 100 ft is exactly 30.48 m
        Assert.True(SurveyHeightText.TryParse("30.48", out double feet));
        Assert.Equal(100.0, feet, 9);
    }

    [Fact]
    public void ARefusedHeightLeavesZero()
    {
        Assert.False(SurveyHeightText.TryParse("3000.01", out double feet));
        Assert.Equal(0.0, feet);
    }

    [Fact]
    public void HeightsIgnoreTheMachineCulture()
    {
        // Two machine cultures' traps at once: de-DE's comma for the point and point for grouping,
        // and the LRM before the minus sign that he-IL gives under ICU on net8 and net10 (Revit 2025
        // to 2027). Cloned from the invariant culture, because named cultures throw where .NET runs
        // in invariant-globalization mode.
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat.NumberDecimalSeparator = ",";
        hostile.NumberFormat.NumberGroupSeparator = ".";
        hostile.NumberFormat.NegativeSign = "\u200E-";

        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = hostile;

            Assert.Equal("-3.20", SurveyHeightText.Format(Feet(-3.2)));
            Assert.Equal("42.75", SurveyHeightText.Format(Feet(42.75)));
            Assert.True(SurveyHeightText.TryParse("-3.20", out double feet));
            Assert.Equal(-3.2, feet * SurveyHeightText.MetresPerFoot, 9);
            Assert.True(SurveyHeightText.TryParse("42.75", out feet));
            Assert.Equal(42.75, feet * SurveyHeightText.MetresPerFoot, 9);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
