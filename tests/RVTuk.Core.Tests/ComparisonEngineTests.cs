using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;
using Xunit;

namespace RVTuk.Core.Tests;

public class MatcherTests
{
    [Fact]
    public void OuterJoin_PartitionsItems()
    {
        var a = new[] { "a", "b", "c" };
        var b = new[] { "b", "c", "d" };

        var r = Matcher.OuterJoin(a, b, x => x);

        Assert.Equal(new[] { "b", "c" }, r.Matched.Select(m => m.A));
        Assert.Equal(new[] { "a" }, r.OnlyA);
        Assert.Equal(new[] { "d" }, r.OnlyB);
    }
}

public class ComparisonEngineTests
{
    private sealed class FakeComparer : ICategoryComparer
    {
        public string CategoryId => "Fake";
        public string DisplayName => "Fake";
        public CategorySnapshot LoadSnapshot(string payloadJson) => new FakeSnapshot();
        public CategoryDiffResult Compare(CategorySnapshot a, CategorySnapshot b) =>
            new CategoryDiffResult { CategoryId = "Fake", DisplayName = "Fake",
                Items = { new ItemDiff { Key = "x", Kind = DiffKind.Changed } } };
    }

    private sealed class FakeSnapshot : CategorySnapshot
    {
        public FakeSnapshot() { CategoryId = "Fake"; }
    }

    [Fact]
    public void Compare_DispatchesToRegisteredComparer()
    {
        var reg = new CategoryRegistry();
        reg.Register(new FakeComparer());
        var engine = new ComparisonEngine(reg);

        var result = engine.Compare(
            new SnapshotMeta { SourceName = "A" },
            new SnapshotMeta { SourceName = "B" },
            new List<CategorySnapshot> { new FakeSnapshot() },
            new List<CategorySnapshot> { new FakeSnapshot() });

        Assert.Single(result.Categories);
        Assert.Equal("Fake", result.Categories[0].CategoryId);
        Assert.Single(result.Categories[0].Items);
    }

    [Fact]
    public void Compare_UnregisteredCategory_Skipped()
    {
        var engine = new ComparisonEngine(new CategoryRegistry());
        var result = engine.Compare(
            new SnapshotMeta(), new SnapshotMeta(),
            new List<CategorySnapshot> { new FakeSnapshot() },
            new List<CategorySnapshot> { new FakeSnapshot() });
        Assert.Empty(result.Categories);
    }

    [Fact]
    public void Compare_OneSidedCategory_Skipped()
    {
        var reg = new CategoryRegistry();
        reg.Register(new FakeComparer());
        var engine = new ComparisonEngine(reg);

        var result = engine.Compare(
            new SnapshotMeta(), new SnapshotMeta(),
            new List<CategorySnapshot> { new FakeSnapshot() },
            new List<CategorySnapshot>());
        Assert.Empty(result.Categories);
    }
}
