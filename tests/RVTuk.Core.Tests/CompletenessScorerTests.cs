using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;
using Xunit;

namespace RVTuk.Core.Tests;

public class CompletenessScorerTests
{
    [Fact]
    public void EmptyTemplate_ScoresZero()
    {
        Assert.Equal(0.0, CompletenessScorer.Score(new ViewTemplateDto()), 3);
    }

    [Fact]
    public void FullyPopulated_ScoresOne()
    {
        var t = new ViewTemplateDto { CategoryOverridesHash = "h" };
        t.FilterNames.Add("Structural");
        t.Settings.Add(new KvPair(ViewTemplateFields.Phase, "New Construction"));
        t.Settings.Add(new KvPair(ViewTemplateFields.DetailLevel, "Fine"));
        t.Settings.Add(new KvPair(ViewTemplateFields.Scale, "100"));
        t.Settings.Add(new KvPair(ViewTemplateFields.Discipline, "Architectural"));

        Assert.Equal(1.0, CompletenessScorer.Score(t), 3);
    }

    [Fact]
    public void DefaultDetailLevel_DoesNotCount()
    {
        var t = new ViewTemplateDto();
        t.Settings.Add(new KvPair(ViewTemplateFields.DetailLevel, "Coarse"));
        Assert.Equal(0.0, CompletenessScorer.Score(t), 3);
    }

    [Fact]
    public void PartialTemplate_SumsWeights()
    {
        // overrides (0.30) + filter (0.25) = 0.55
        var t = new ViewTemplateDto { CategoryOverridesHash = "h" };
        t.FilterNames.Add("F");
        Assert.Equal(0.55, CompletenessScorer.Score(t), 3);
    }
}
