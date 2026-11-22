using System.Globalization;
using System.Linq;
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

public class AreaListDisplayTests
{
    private static AreaRowViewModel Row(int? code, int? old, double m2) =>
        new AreaRowViewModel(1, new AreaRecord { UsageCode = code, UsageCodePrev = old, AreaValue = m2 });

    [Fact]
    public void LevelGroup_Parts_OmitZeroGroups_ShowNoCode_AndSigmaTotal()
    {
        var g = new AreaLevelGroupViewModel("Ground קרקע");
        g.Rows.Add(Row(1, null, 22));
        g.Rows.Add(Row(301, 1, 12.5));
        g.Rows.Add(Row(null, null, 84.3));
        Assert.Equal(new[] { (UsageGroup.Primary, "22.00"), (UsageGroup.Process, "12.50"), (UsageGroup.NoCode, "84.30") },
            g.Parts.Select(p => (p.Group, p.Text)));
        Assert.Equal("Σ 22.00", g.TotalText);
    }

    [Fact]
    public void LevelGroup_UncodedZeroArea_StillShowsNoCode()
    {
        var g = new AreaLevelGroupViewModel("Ground");
        g.Rows.Add(Row(null, null, 0));
        Assert.Equal(new[] { (UsageGroup.NoCode, "0.00") }, g.Parts.Select(p => (p.Group, p.Text)));
    }

    [Fact]
    public void Row_HasNoCode_OnlyWhenBothEmpty()
    {
        Assert.True(Row(null, null, 0).HasNoCode);
        Assert.False(Row(null, 1, 0).HasNoCode);
    }

    [Fact]
    public void Row_NumberLabel_KeepsNumberApartFromName()
    {
        var named = new AreaRowViewModel(1, new AreaRecord { Number = "002", Name = "דירה 1 - Apt" });
        Assert.Equal("002 —", named.NumberLabel);
        var noName = new AreaRowViewModel(1, new AreaRecord { Number = "002" });
        Assert.Equal("002", noName.NumberLabel);
        var noNumber = new AreaRowViewModel(1, new AreaRecord { Name = "Lobby מבואה" });
        Assert.Equal("(no #) —", noNumber.NumberLabel);
    }

    [Fact]
    public void UsageValueDisplay_Texts()
    {
        var code = UsageValueDisplay.From(new UsageValue(UsageValueKind.Code, 110));
        Assert.Equal("110 - מרפסת", code.Text);
        Assert.Equal(UsageGroup.Service, code.Group);
        Assert.False(code.IsMixed);

        var none = UsageValueDisplay.From(new UsageValue(UsageValueKind.None, null));
        Assert.Equal("— none —", none.Text);
        Assert.Null(none.Group);

        var mixed = UsageValueDisplay.From(new UsageValue(UsageValueKind.Mixed, null));
        Assert.Equal("(mixed)", mixed.Text);
        Assert.True(mixed.IsMixed);
        Assert.Null(mixed.Group);
    }
}
