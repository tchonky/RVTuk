using System.Globalization;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.RishuiZamin;

public class AreaRowViewModelTests
{
    private static AreaRowViewModel Row(int? code, int? old, double m2 = 0) =>
        new AreaRowViewModel(1, new AreaRecord { Number = "003", Name = "הריסה Demo", UsageCode = code, UsageCodePrev = old, AreaValue = m2 });

    [Fact]
    public void AreaText_HasTwoDecimals_InAnyCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("84.30", Row(1, null, 84.3).AreaText);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void CodeHalves_AndTheirGroups()
    {
        var r = Row(301, 1);
        Assert.Equal("301", r.NewCodeText);
        Assert.Equal("←1", r.OldCodeText);
        Assert.Equal(UsageGroup.Process, r.NewGroup);
        Assert.Equal(UsageGroup.Primary, r.OldGroup);
        Assert.Equal(UsageGroup.Process, r.Group);
    }

    [Fact]
    public void NoCodes_HalvesNull_GroupNoCode()
    {
        var r = Row(null, null);
        Assert.Null(r.NewCodeText);
        Assert.Null(r.OldCodeText);
        Assert.Equal(UsageGroup.NoCode, r.Group);
    }

    [Fact]
    public void IsSelected_Notifies()
    {
        var r = Row(1, null);
        string? changed = null;
        r.PropertyChanged += (_, e) => changed = e.PropertyName;
        r.IsSelected = true;
        Assert.Equal(nameof(AreaRowViewModel.IsSelected), changed);
    }

    [Fact]
    public void LevelGroup_TotalsItsRows()
    {
        var g = new AreaLevelGroupViewModel("קומת קרקע Ground");
        g.Rows.Add(Row(1, null, 10));
        g.Rows.Add(Row(105, null, 2.5));
        g.Rows.Add(Row(null, null, 1));
        Assert.Equal(12.5, g.Totals.Total, 9);
        Assert.Equal(1, g.Totals.NoCode, 9);
    }
}
