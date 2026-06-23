using RVTuk.Core.Models.Comparison;
using Xunit;

namespace RVTuk.Core.Tests;

public class SnapshotDtoTests
{
    [Fact]
    public void ViewTemplatesSnapshot_HasCategoryId()
    {
        var snap = new ViewTemplatesSnapshot();
        Assert.Equal("ViewTemplates", snap.CategoryId);
        Assert.Empty(snap.Templates);
    }

    [Fact]
    public void ViewTemplateDto_HoldsContent()
    {
        var t = new ViewTemplateDto
        {
            Name = "Floor Plan - Working",
            ViewType = "FloorPlan",
            CategoryOverridesHash = "abc",
        };
        t.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", true));
        t.Settings.Add(new KvPair("DETAIL_LEVEL", "Fine"));
        t.FilterNames.Add("Structural");

        Assert.Equal("Floor Plan - Working", t.Name);
        Assert.True(t.Included[0].Controlled);
        Assert.Equal("Fine", t.Settings[0].Value);
        Assert.Contains("Structural", t.FilterNames);
    }

    [Fact]
    public void StandardSnapshot_IsMutableByDefault()
    {
        var std = new StandardSnapshot();
        Assert.True(std.Meta.IsMutable);
        Assert.Equal("Standard", std.Meta.SourceKind);
    }
}
