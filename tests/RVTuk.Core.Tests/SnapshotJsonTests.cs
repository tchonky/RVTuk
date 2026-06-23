using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Serialization;
using Xunit;

namespace RVTuk.Core.Tests;

public class SnapshotJsonTests
{
    [Fact]
    public void ViewTemplatesSnapshot_RoundTrips()
    {
        var snap = new ViewTemplatesSnapshot();
        var a = new ViewTemplateDto { Name = "Floor Plan - Working", ViewType = "FloorPlan", CategoryOverridesHash = "h1" };
        a.Included.Add(new ControlledParam("DETAIL_LEVEL", "Detail Level", true));
        a.Included.Add(new ControlledParam("VIEW_SCALE", "Scale", false));
        a.Settings.Add(new KvPair("DETAIL_LEVEL", "Fine"));
        a.FilterNames.Add("Structural");
        var b = new ViewTemplateDto { Name = "Section - Interior", ViewType = "Section" };
        snap.Templates.Add(a);
        snap.Templates.Add(b);

        var json = SnapshotJson.Serialize(snap);
        var back = SnapshotJson.Deserialize<ViewTemplatesSnapshot>(json);

        Assert.Equal("ViewTemplates", back.CategoryId);
        Assert.Equal(2, back.Templates.Count);
        var ra = back.Templates[0];
        Assert.Equal("Floor Plan - Working", ra.Name);
        Assert.Equal("FloorPlan", ra.ViewType);
        Assert.Equal("h1", ra.CategoryOverridesHash);
        Assert.Equal(2, ra.Included.Count);
        Assert.True(ra.Included[0].Controlled);
        Assert.False(ra.Included[1].Controlled);
        Assert.Equal("Fine", ra.Settings[0].Value);
        Assert.Contains("Structural", ra.FilterNames);
        Assert.Equal("Section - Interior", back.Templates[1].Name);
    }

    [Fact]
    public void EmptySnapshot_RoundTrips()
    {
        var json = SnapshotJson.Serialize(new ViewTemplatesSnapshot());
        var back = SnapshotJson.Deserialize<ViewTemplatesSnapshot>(json);
        Assert.Empty(back.Templates);
    }
}
