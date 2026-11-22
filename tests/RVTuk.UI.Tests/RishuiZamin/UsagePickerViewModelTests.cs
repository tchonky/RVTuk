using System;
using System.Linq;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.RishuiZamin;

public class UsagePickerViewModelTests
{
    private static UsagePickerViewModel Picker(UsageKeySlot slot = UsageKeySlot.New, int? current = null, params string?[] names) =>
        new UsagePickerViewModel(slot, names, current);

    [Fact]
    public void EmptyQuery_StartsWithClear_ThenSuggestions_ThenGroups()
    {
        var items = Picker(names: "מרפסת - Balcony").Items;
        Assert.Equal(PickerItemKind.Clear, items[0].Kind);
        Assert.Equal("— none — (clear)", items[0].Text);
        Assert.Equal(PickerItemKind.Heading, items[1].Kind);
        Assert.Equal("Suggested from the name", items[1].Text);
        Assert.Equal(new[] { 30, 110, 250 }, items.Skip(2).Take(3).Select(i => i.Entry!.Code));
        Assert.All(items.Skip(2).Take(3), i => Assert.Equal(PickerItemKind.Suggestion, i.Kind));
        Assert.Equal(PickerItemKind.Heading, items[5].Kind);
        Assert.Equal("PRIMARY  שטחים עיקריים", items[5].Text);
        Assert.Equal("1  מגורים", items[6].Text);
        Assert.Equal(UsageGroup.Primary, items[6].Group);
    }

    [Fact]
    public void NoSuggestions_NoSuggestionHeading()
    {
        var items = Picker(names: "Room 12 חדר").Items;
        Assert.DoesNotContain(items, i => i.Text == "Suggested from the name");
        Assert.DoesNotContain(items, i => i.Kind == PickerItemKind.Suggestion);
        Assert.Equal(PickerItemKind.Heading, items[1].Kind);
    }

    [Fact]
    public void Old_HasNoProcessHeading()
    {
        var items = Picker(UsageKeySlot.Old).Items;
        Assert.DoesNotContain(items, i => i.Kind == PickerItemKind.Heading && i.Text.StartsWith("PROCESS", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Entry?.Kind == UsageKind.Process);
        Assert.Contains(items, i => i.Text == "SERVICE  שטחי שירות");
    }

    [Fact]
    public void Query_ShowsOnlyMatchingGroups()
    {
        var p = Picker();
        p.Query = "11";
        Assert.DoesNotContain(p.Items, i => i.Kind == PickerItemKind.Clear);
        Assert.Equal(new[] { "PRIMARY  שטחים עיקריים", "SERVICE  שטחי שירות" },
            p.Items.Where(i => i.Kind == PickerItemKind.Heading).Select(i => i.Text));
        Assert.Equal(11, p.Items.Count(i => i.Kind == PickerItemKind.Entry));
    }

    [Fact]
    public void Move_SkipsHeadings_AndClamps()
    {
        var p = Picker(names: "Lobby");   // Clear, heading, suggestion 105, heading, entries…
        Assert.Equal(0, p.HighlightedIndex);
        p.Move(-1);
        Assert.Equal(0, p.HighlightedIndex);
        p.Move(1);
        Assert.Equal(2, p.HighlightedIndex);
        p.Move(1);
        Assert.Equal(4, p.HighlightedIndex);
        for (var i = 0; i < 200; i++) p.Move(1);
        Assert.Equal(p.Items.Count - 1, p.HighlightedIndex);

        p.Query = "11";
        Assert.Equal(1, p.HighlightedIndex);   // first non-heading
    }

    [Fact]
    public void PickClear_RaisesNull()
    {
        var p = Picker();
        int? picked = -1;
        p.Picked += c => picked = c;
        p.PickHighlighted();
        Assert.Null(picked);
    }

    [Fact]
    public void PickHighlighted_RaisesCode()
    {
        var p = Picker();
        int? picked = -1;
        p.Picked += c => picked = c;
        p.Query = "105";
        p.PickHighlighted();
        Assert.Equal(105, picked);
    }

    [Fact]
    public void PickHeading_DoesNothing()
    {
        var p = Picker();
        var raised = false;
        p.Picked += _ => raised = true;
        p.Pick(p.Items.First(i => i.Kind == PickerItemKind.Heading));
        Assert.False(raised);
    }

    [Fact]
    public void CurrentCode_IsMarked()
    {
        var current = Picker(current: 105).Items.Where(i => i.IsCurrent).ToList();
        Assert.Single(current);
        Assert.Equal(105, current[0].Entry!.Code);
    }
}
