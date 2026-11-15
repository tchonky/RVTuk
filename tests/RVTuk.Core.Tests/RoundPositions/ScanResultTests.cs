using System.Linq;
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class ScanResultTests
{
    private static DirtyElement Wall(long id, string level, double? elevation) =>
        new DirtyElement(id, FixTier.Walls, "Walls", level, elevation, $"Wall {id}");

    [Fact]
    public void CountsComeStraightFromTheBuilder()
    {
        var builder = new ScanResultBuilder();
        builder.CountExamined();
        builder.RecordAlreadyClean();
        builder.CountExamined();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.CountExamined();
        builder.RecordSkipped(3, "Walls", SkipReason.NotOrthogonal);

        var result = builder.Build();

        Assert.Equal(3, result.Examined);
        Assert.Equal(1, result.AlreadyClean);
        Assert.Single(result.Dirty);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void CategoriesSortByTierNotAlphabetically()
    {
        // The order is the point: fixing walls before levels means the level fix moves those
        // walls again. Alphabetically "Levels" would follow "Grids" and both would follow "Walls".
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.RecordDirty(new DirtyElement(2, FixTier.Datums, "Grids", "", null, "Grid A"));
        builder.RecordDirty(new DirtyElement(3, FixTier.Levels, "Levels", "", null, "Level 1"));

        var categories = builder.Build().GroupedByCategoryAndLevel();

        Assert.Equal(new[] { FixTier.Levels, FixTier.Datums, FixTier.Walls },
            categories.Select(c => c.Tier).ToArray());
    }

    [Fact]
    public void LevelsSortBottomToTopByElevation()
    {
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 3", 700));
        builder.RecordDirty(Wall(2, "Level 1", 0));
        builder.RecordDirty(Wall(3, "Level 2", 350));

        var levels = builder.Build().GroupedByCategoryAndLevel().Single().Levels;

        Assert.Equal(new[] { "Level 1", "Level 2", "Level 3" },
            levels.Select(l => l.Level).ToArray());
    }

    [Fact]
    public void ElementsWithNoLevelSortFirstUnderTheirOwnLabel()
    {
        // Grids and base points belong to no level. They must still appear under their category
        // rather than being dropped from the tree.
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.RecordDirty(Wall(2, "", null));

        var levels = builder.Build().GroupedByCategoryAndLevel().Single().Levels;

        Assert.Equal(ScanResult.NoLevelLabel, levels[0].Level);
        Assert.Equal("Level 1", levels[1].Level);
    }

    [Fact]
    public void CountsRollUpFromElementsToLevelToCategory()
    {
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.RecordDirty(Wall(2, "Level 1", 0));
        builder.RecordDirty(Wall(3, "Level 2", 350));

        var category = builder.Build().GroupedByCategoryAndLevel().Single();

        Assert.Equal(3, category.Count);
        Assert.Equal(2, category.Levels[0].Count);
        Assert.Equal(1, category.Levels[1].Count);
    }

    [Fact]
    public void TwoCategoriesInTheSameTierSortByName()
    {
        var builder = new ScanResultBuilder();
        builder.RecordDirty(new DirtyElement(1, FixTier.Datums, "Reference Planes", "", null, "RP 1"));
        builder.RecordDirty(new DirtyElement(2, FixTier.Datums, "Grids", "", null, "Grid A"));

        var categories = builder.Build().GroupedByCategoryAndLevel();

        Assert.Equal(new[] { "Grids", "Reference Planes" },
            categories.Select(c => c.Category).ToArray());
    }

    [Fact]
    public void AnEmptyScanGroupsToNothingRatherThanThrowing()
    {
        var result = new ScanResultBuilder().Build();

        Assert.Empty(result.GroupedByCategoryAndLevel());
        Assert.Equal(0, result.Examined);
    }

    [Fact]
    public void SkipsGroupByReasonLargestFirst()
    {
        var builder = new ScanResultBuilder();
        builder.RecordSkipped(1, "Furniture", SkipReason.InGroup);
        builder.RecordSkipped(2, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(3, "Walls", SkipReason.NotOrthogonal);

        var groups = builder.Build().GroupedSkips();

        Assert.Equal(SkipReason.NotOrthogonal, groups[0].Reason);
        Assert.Equal(2, groups[0].Elements.Count);
    }
}
