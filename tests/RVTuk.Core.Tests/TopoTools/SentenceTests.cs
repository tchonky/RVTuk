using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class SentenceTests
{
    [Theory]
    [InlineData("it is a subdivision", "it is a subdivision")]
    [InlineData("Revit refused it.", "Revit refused it")]
    [InlineData("Revit refused it.\r\n", "Revit refused it")]
    [InlineData("Revit refused it..", "Revit refused it")]
    [InlineData("", "")]
    public void WithoutFullStopDropsTrailingStopsAndSpace(string message, string expected)
    {
        Assert.Equal(expected, Sentence.WithoutFullStop(message));
    }
}
