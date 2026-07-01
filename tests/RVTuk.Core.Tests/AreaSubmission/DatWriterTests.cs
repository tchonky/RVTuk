using RVTuk.Core.AreaSubmission;
using Xunit;

namespace RVTuk.Core.Tests.AreaSubmission;

public class DatWriterTests
{
    [Fact]
    public void Build_MatchesSampleBytes()
    {
        var bytes = DatWriter.Build(10);
        Assert.Equal(new byte[] { 0x44, 0x57, 0x46, 0x58, 0x5F, 0x53, 0x43, 0x41, 0x4C, 0x45, 0x09, 0x31, 0x30, 0x0A }, bytes);
    }
}
