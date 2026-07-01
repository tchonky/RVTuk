using System.Linq;
using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;
using Xunit;

namespace RVTuk.Core.Tests;

public class ViewTemplateComparerTests
{
    private static ViewTemplateDto Tpl(string name, string viewType = "FloorPlan")
        => new ViewTemplateDto { Name = name, ViewType = viewType };

    private static ViewTemplatesSnapshot Snap(params ViewTemplateDto[] tpls)
    {
        var s = new ViewTemplatesSnapshot();
        s.Templates.AddRange(tpls);
        return s;
    }

    private readonly ViewTemplateComparer _cmp = new ViewTemplateComparer();

    [Fact]
    public void MatchedPair_OneControlledFieldDiffers_IsChanged()
    {
        var a = Tpl("FP");
        a.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", true));
        a.Settings.Add(new KvPair("DETAIL_LEVEL", "Medium"));
        var b = Tpl("FP");
        b.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", true));
        b.Settings.Add(new KvPair("DETAIL_LEVEL", "Fine"));

        var r = _cmp.Compare(Snap(a), Snap(b));

        var item = Assert.Single(r.Items);
        Assert.Equal(DiffKind.Changed, item.Kind);
        var field = Assert.Single(item.Fields);
        Assert.Equal("DETAIL_LEVEL", field.FieldId);
        Assert.False(field.IsEqual);
    }

    [Fact]
    public void FieldControlledInAOnly_IsClassifiedAsControlDifference()
    {
        var a = Tpl("FP");
        a.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", true));
        a.Settings.Add(new KvPair("DETAIL_LEVEL", "Fine"));
        var b = Tpl("FP");
        b.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", false));

        var r = _cmp.Compare(Snap(a), Snap(b));

        var item = Assert.Single(r.Items);
        var field = Assert.Single(item.Fields);
        Assert.Equal("Fine", field.ValueA);
        Assert.Null(field.ValueB);
        Assert.Equal(DiffKind.Changed, item.Kind);
    }

    [Fact]
    public void FieldUncontrolledOnBothSides_EmitsNoFieldDiff()
    {
        var a = Tpl("FP");
        a.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", false));
        var b = Tpl("FP");
        b.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", false));

        var r = _cmp.Compare(Snap(a), Snap(b));

        var item = Assert.Single(r.Items);
        Assert.Empty(item.Fields);
        Assert.Equal(DiffKind.Unchanged, item.Kind);
    }

    [Fact]
    public void TemplateOnlyInA_IsAdded_OnlyInB_IsRemoved()
    {
        var r = _cmp.Compare(Snap(Tpl("OnlyA")), Snap(Tpl("OnlyB")));

        Assert.Equal(2, r.Items.Count);
        Assert.Equal(DiffKind.Added, r.Items.Single(i => i.DisplayName == "OnlyA").Kind);
        Assert.Equal(DiffKind.Removed, r.Items.Single(i => i.DisplayName == "OnlyB").Kind);
        Assert.Equal(1, r.Summary.Added);
        Assert.Equal(1, r.Summary.Removed);
    }

    [Fact]
    public void SameName_DifferentViewType_NotMatched()
    {
        var r = _cmp.Compare(Snap(Tpl("P", "FloorPlan")), Snap(Tpl("P", "Ceiling")));

        Assert.Equal(2, r.Items.Count);
        Assert.Contains(r.Items, i => i.Kind == DiffKind.Added);
        Assert.Contains(r.Items, i => i.Kind == DiffKind.Removed);
    }

    [Fact]
    public void DifferingFilters_AreSurfaced()
    {
        var a = Tpl("FP");
        a.FilterNames.Add("Structural");
        var b = Tpl("FP");
        b.FilterNames.Add("Structural");
        b.FilterNames.Add("Grid");

        var r = _cmp.Compare(Snap(a), Snap(b));

        var item = Assert.Single(r.Items);
        Assert.Equal(DiffKind.Changed, item.Kind);
        Assert.Contains(item.Fields, f => f.FieldId == ViewTemplateFields.Filters && !f.IsEqual);
    }

    [Fact]
    public void DifferingOverrideHash_IsSurfaced()
    {
        var a = Tpl("FP"); a.CategoryOverridesHash = "h1";
        var b = Tpl("FP"); b.CategoryOverridesHash = "h2";

        var r = _cmp.Compare(Snap(a), Snap(b));

        var item = Assert.Single(r.Items);
        Assert.Contains(item.Fields, f => f.FieldId == ViewTemplateFields.VgOverrides && !f.IsEqual);
    }
}
