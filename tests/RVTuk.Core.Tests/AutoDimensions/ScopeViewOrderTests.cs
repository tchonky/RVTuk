using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class ScopeViewOrderTests
{
    [Fact]
    public void SortsByNameNotByDiscoveryOrder()
    {
        var views = new List<ScopeViewInfo>
        {
            new(30, "Working"),
            new(10, "As Built"),
            new(20, "Furniture"),
        };

        var sorted = ScopeViewOrder.Sort(views);

        Assert.Equal(new[] { "As Built", "Furniture", "Working" }, Names(sorted));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var views = new List<ScopeViewInfo>
        {
            new(1, "beta"),
            new(2, "Alpha"),
            new(3, "GAMMA"),
        };

        var sorted = ScopeViewOrder.Sort(views);

        Assert.Equal(new[] { "Alpha", "beta", "GAMMA" }, Names(sorted));
    }

    [Fact]
    public void SortsNumberedViewsInNumericTextOrder()
    {
        // Plain text ordering: "Level 10" precedes "Level 2". Documented, not accidental —
        // matching Revit's own project-browser behaviour is not worth a natural-sort dependency.
        var views = new List<ScopeViewInfo>
        {
            new(1, "Level 2"),
            new(2, "Level 10"),
        };

        var sorted = ScopeViewOrder.Sort(views);

        Assert.Equal(new[] { "Level 10", "Level 2" }, Names(sorted));
    }

    [Fact]
    public void PreservesEveryViewAndItsId()
    {
        var views = new List<ScopeViewInfo>
        {
            new(30, "Zulu"),
            new(10, "Alpha"),
        };

        var sorted = ScopeViewOrder.Sort(views);

        Assert.Equal(2, sorted.Count);
        Assert.Equal(10, sorted[0].ViewId);
        Assert.Equal(30, sorted[1].ViewId);
    }

    [Fact]
    public void EmptyInputStaysEmpty()
    {
        Assert.Empty(ScopeViewOrder.Sort(new List<ScopeViewInfo>()));
    }

    private static string[] Names(IReadOnlyList<ScopeViewInfo> views)
    {
        var names = new string[views.Count];
        for (int i = 0; i < views.Count; i++) names[i] = views[i].ViewName;
        return names;
    }
}
