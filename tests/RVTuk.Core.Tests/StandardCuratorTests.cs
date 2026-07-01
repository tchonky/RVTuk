using System.Linq;
using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;
using Xunit;

namespace RVTuk.Core.Tests;

public class StandardCuratorTests
{
    private static (StandardCurator curator, ViewTemplatesSnapshot source, SnapshotMeta meta) Setup()
    {
        var curator = new StandardCurator(new[] { new ViewTemplateMerger() });
        var source = new ViewTemplatesSnapshot();
        source.Templates.Add(new ViewTemplateDto { Name = "FP", ViewType = "FloorPlan" });
        var meta = new SnapshotMeta { SourceName = "Project Gamma", CapturedUtc = "2026-06-23T00:00:00.0000000Z" };
        return (curator, source, meta);
    }

    private const string Key = "FloorPlan|FP";

    [Fact]
    public void Accept_IntoEmptyStandard_AddsTemplateWithProvenanceAndRevision()
    {
        var (curator, source, meta) = Setup();
        var std = new StandardSnapshot();

        var result = curator.Accept(std, meta, source, Key, new DependencyClosure());

        Assert.True(result.Applied);
        var cat = Assert.Single(std.Categories.OfType<ViewTemplatesSnapshot>());
        Assert.Single(cat.Templates);
        Assert.Equal(1, std.Meta.Revision);
        var prov = Assert.Single(std.Provenance);
        Assert.Equal("Project Gamma", prov.SourceName);
        Assert.Equal(Key, prov.ItemKey);
    }

    [Fact]
    public void Accept_DuplicateWithoutReplace_Conflicts()
    {
        var (curator, source, meta) = Setup();
        var std = new StandardSnapshot();
        curator.Accept(std, meta, source, Key, new DependencyClosure());

        var second = curator.Accept(std, meta, source, Key, new DependencyClosure());

        Assert.False(second.Applied);
        Assert.Equal("exists", second.Conflict);
        Assert.Equal(1, std.Meta.Revision); // unchanged
        Assert.Single(std.Categories.OfType<ViewTemplatesSnapshot>().Single().Templates);
    }

    [Fact]
    public void Accept_DuplicateWithReplace_ReplacesSingleInstance()
    {
        var (curator, source, meta) = Setup();
        var std = new StandardSnapshot();
        curator.Accept(std, meta, source, Key, new DependencyClosure());

        var second = curator.Accept(std, meta, source, Key, new DependencyClosure(), replace: true);

        Assert.True(second.Applied);
        Assert.Equal(2, std.Meta.Revision);
        Assert.Single(std.Categories.OfType<ViewTemplatesSnapshot>().Single().Templates);
        Assert.Single(std.Provenance); // provenance de-duped by key
    }

    [Fact]
    public void Accept_UnknownItem_NotFound()
    {
        var (curator, source, meta) = Setup();
        var std = new StandardSnapshot();
        var result = curator.Accept(std, meta, source, "FloorPlan|Missing", new DependencyClosure());
        Assert.False(result.Applied);
        Assert.Equal("not found", result.Conflict);
    }

    [Fact]
    public void AcceptedCopy_IsIndependentOfSource()
    {
        var (curator, source, meta) = Setup();
        var std = new StandardSnapshot();
        curator.Accept(std, meta, source, Key, new DependencyClosure());

        // mutate source after accept
        source.Templates[0].FilterNames.Add("AddedLater");

        var copied = std.Categories.OfType<ViewTemplatesSnapshot>().Single().Templates[0];
        Assert.Empty(copied.FilterNames);
    }
}
