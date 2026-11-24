using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class HeightStepsTests
{
    private static HeightSteps Steps(string typed)
    {
        Assert.True(HeightSteps.TryCreate(typed, out var steps));
        return steps;
    }

    [Theory]
    [InlineData("43")]
    [InlineData("=-3.2")]
    [InlineData("abc")]
    [InlineData("")]
    public void OnlyAMoveMakesSteps(string typed) => Assert.False(HeightSteps.TryCreate(typed, out _));

    [Fact]
    public void BeforeAStartThereIsNoNextLabel() => Assert.Null(Steps("+0.1").NextLabel);

    [Fact]
    public void EachStepGoesFromTheLineBefore()
    {
        var steps = Steps("+0.1");
        Assert.True(steps.Start("50"));
        Assert.Equal("50.00", steps.Last);
        Assert.Equal("50.10", steps.NextLabel);
        steps.Accept("50.10");
        Assert.Equal("50.20", steps.NextLabel);
        steps.Accept("50.20");
        Assert.Equal("50.30", steps.NextLabel);
    }

    [Fact]
    public void ANextLabelNotAcceptedIsOfferedAgain()
    {
        var steps = Steps("+0.1");
        steps.Start("50");
        Assert.Equal("50.10", steps.NextLabel);
        Assert.Equal("50.10", steps.NextLabel);   // a click Revit rolled back does not advance the run
    }

    [Fact]
    public void AMinusStepGoesDown()
    {
        var steps = Steps("-0.25");
        steps.Start("50,5 m");
        Assert.Equal("50.25", steps.NextLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void AnUnreadableLabelCannotStartTheRun(string? label)
    {
        var steps = Steps("+0.1");
        Assert.False(steps.Start(label));
        Assert.Null(steps.NextLabel);
    }

    [Fact]
    public void AStepPastTheHighestHeightHasNoNextLabel()
    {
        var steps = Steps("+1");
        steps.Start("2999.5");
        Assert.Null(steps.NextLabel);
    }
}
