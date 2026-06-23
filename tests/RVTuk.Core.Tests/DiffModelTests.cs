using RVTuk.Core.Models.Comparison;
using Xunit;

namespace RVTuk.Core.Tests;

public class DiffModelTests
{
    [Fact]
    public void FieldDiff_DifferentValues_NotEqual()
    {
        var d = new FieldDiff("Scale", "Scale", "1:100", "1:50");
        Assert.False(d.IsEqual);
    }

    [Fact]
    public void FieldDiff_SameValues_Equal()
    {
        var d = new FieldDiff("Scale", "Scale", "1:100", "1:100");
        Assert.True(d.IsEqual);
    }

    [Fact]
    public void FieldDiff_BothNull_Equal()
    {
        var d = new FieldDiff("X", "X", null, null);
        Assert.True(d.IsEqual);
    }

    [Fact]
    public void FieldDiff_OneNull_NotEqual()
    {
        var d = new FieldDiff("X", "X", "v", null);
        Assert.False(d.IsEqual);
    }

    [Fact]
    public void ItemDiff_Defaults_NoFutureApplyTokenInV1()
    {
        var item = new ItemDiff { Key = "k", DisplayName = "n", Kind = DiffKind.Changed };
        Assert.Null(item.FutureApplyToken);
        Assert.Empty(item.Fields);
    }
}
