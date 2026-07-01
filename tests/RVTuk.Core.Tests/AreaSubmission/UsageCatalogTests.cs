using RVTuk.Core.AreaSubmission;
using Xunit;

namespace RVTuk.Core.Tests.AreaSubmission;

public class UsageCatalogTests
{
    [Fact]
    public void ByCode_ReturnsPrimary_For1()
    {
        var e = UsageCatalog.ByCode(1);
        Assert.NotNull(e);
        Assert.Equal(UsageKind.Primary, e!.Kind);
    }

    [Fact]
    public void Service101_IsService() => Assert.Equal(UsageKind.Service, UsageCatalog.ByCode(101)!.Kind);

    [Fact]
    public void UnknownCode_IsInvalid() => Assert.False(UsageCatalog.IsValidCode(9999));
}
