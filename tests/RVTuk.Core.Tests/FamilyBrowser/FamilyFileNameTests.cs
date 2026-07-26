using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class FamilyFileNameTests
{
    [Theory]
    // Ordinary library file names.
    [InlineData("Door Single.rfa", "Door Single")]
    [InlineData("X.RFA", "X")] // extension match is case-insensitive
    [InlineData(".rfa", "")]
    // Dots inside the name belong to the name — only the trailing ".rfa" comes off.
    [InlineData("M_Door 1.2.rfa", "M_Door 1.2")]
    [InlineData("Family.v2", "Family.v2")]
    // Synthetic "model only" rows are built from in-project family names, which Revit
    // allows to contain characters that are illegal in Windows paths. A name with an
    // inch mark (") made net48's Path.GetFileNameWithoutExtension throw ArgumentException
    // and crash Revit (CER 2026-07-16); a name with '/' made every framework treat the
    // name as a directory path and truncate it. Plain string handling must survive both.
    [InlineData("Bolt 1/2\".rfa", "Bolt 1/2\"")]
    [InlineData("W12<26.rfa", "W12<26")]
    [InlineData("דלת כניסה.rfa", "דלת כניסה")]
    // Defensive edges.
    [InlineData("NoExtension", "NoExtension")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void WithoutRfaExtension(string? fileName, string expected)
    {
        Assert.Equal(expected, FamilyFileName.WithoutRfaExtension(fileName));
    }

    [Theory]
    [InlineData("Plain Door")]
    [InlineData("Door 90x210 (fire rated)")]
    [InlineData("Дверь-Δ £")] // non-ASCII is fine
    public void IsSafeFileName_AcceptsOrdinaryNames(string name)
        => Assert.True(FamilyFileName.IsSafeFileName(name));

    [Theory]
    [InlineData("24\" Door")] // inch mark — the CER 2026-07-16 crash character
    [InlineData("A/B Door")]
    [InlineData("A\\B Door")]
    [InlineData("Door: wide")]
    [InlineData("Door?")]
    [InlineData("Door*")]
    [InlineData("Door<1>")]
    [InlineData("Door|x")]
    [InlineData("Door\t")] // control char
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsSafeFileName_RejectsIllegalOrBlankNames(string? name)
        => Assert.False(FamilyFileName.IsSafeFileName(name));
}
