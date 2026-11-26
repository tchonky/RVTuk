using RVTuk.Core.WallFinishes;
using Xunit;

namespace RVTuk.Core.Tests.WallFinishes;

public class FinishStyleNameTests
{
    [Fact] public void PrefixesTheMaterialName() => Assert.Equal("_DP-Mat Plaster", FinishStyleName.For("Plaster"));

    [Fact] public void ForbiddenCharactersAreReplaced() => Assert.Equal("_DP-Mat Paint - RAL 9010 -A-", FinishStyleName.For("Paint : RAL 9010 [A]"));

    [Fact] public void HebrewIsKept() => Assert.Equal("_DP-Mat טיח", FinishStyleName.For("טיח"));

    [Fact]
    public void RecognisesItsOwnStyles()
    {
        Assert.True(FinishStyleName.IsFinishStyle("_DP-Mat Plaster"));
        Assert.False(FinishStyleName.IsFinishStyle("_DP-Dim Outer"));
    }

    [Fact] public void MaterialOfReturnsTheNameAfterThePrefix() => Assert.Equal("Plaster", FinishStyleName.MaterialOf("_DP-Mat Plaster"));
}
