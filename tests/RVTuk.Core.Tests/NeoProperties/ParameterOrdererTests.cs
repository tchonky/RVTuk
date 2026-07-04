using System.Linq;
using RVTuk.Core.NeoProperties;
using Xunit;

namespace RVTuk.Core.Tests.NeoProperties;

public class ParameterOrdererTests
{
    [Fact]
    public void PinnedParametersAppearFirstInPinnedOrder()
    {
        var input = new[]
        {
            new ParameterEntry("Identity Data", "Comments", "hello"),
            new ParameterEntry("Constraints", "Mark", "A-101"),
            new ParameterEntry("Identity Data", "Family and Type", "Wall: Basic"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal("Pinned", result[0].GroupName);
        Assert.Equal(new[] { "Mark", "Comments", "Family and Type" },
            result[0].Parameters.Select(p => p.Name));
    }

    [Fact]
    public void MissingPinnedParametersAreSkippedNotBlank()
    {
        var input = new[]
        {
            new ParameterEntry("Identity Data", "Comments", "hello"),
            new ParameterEntry("Other", "Length", "10 ft"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal(new[] { "Comments" }, result[0].Parameters.Select(p => p.Name));
    }

    [Fact]
    public void NoPinnedParametersPresentSkipsPinnedGroupEntirely()
    {
        var input = new[]
        {
            new ParameterEntry("Other", "Length", "10 ft"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.DoesNotContain(result, g => g.GroupName == "Pinned");
    }

    [Fact]
    public void RemainingGroupsFollowFixedGroupOrder()
    {
        var input = new[]
        {
            new ParameterEntry("Dimensions", "Length", "10 ft"),
            new ParameterEntry("Constraints", "Level", "Level 1"),
            new ParameterEntry("Identity Data", "Description", "desc"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal(new[] { "Identity Data", "Constraints", "Dimensions" },
            result.Select(g => g.GroupName));
    }

    [Fact]
    public void GroupsOutsideFixedOrderAppendInFirstSeenOrder()
    {
        var input = new[]
        {
            new ParameterEntry("Structural", "Rebar Cover", "25 mm"),
            new ParameterEntry("Identity Data", "Description", "desc"),
            new ParameterEntry("Electrical", "Voltage", "230V"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal(new[] { "Identity Data", "Structural", "Electrical" },
            result.Select(g => g.GroupName));
    }

    [Fact]
    public void IntraGroupOrderIsPreserved()
    {
        var input = new[]
        {
            new ParameterEntry("Identity Data", "Description", "desc"),
            new ParameterEntry("Identity Data", "Assembly Code", "A1010"),
        };

        var result = ParameterOrderer.Order(input);

        var idGroup = result.Single(g => g.GroupName == "Identity Data");
        Assert.Equal(new[] { "Description", "Assembly Code" },
            idGroup.Parameters.Select(p => p.Name));
    }
}
