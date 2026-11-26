using Kms.Core;

namespace Kms.Core.Tests;

public sealed class BuildInfoTests
{
    [Fact]
    public void Version_ComesFromDirectoryBuildProps()
    {
        Assert.StartsWith("4.", BuildInfo.Version, StringComparison.Ordinal);
        Assert.DoesNotContain('+', BuildInfo.Version);
    }

    [Fact]
    public void FileVersion_HasFourParts()
    {
        Assert.Equal(4, BuildInfo.FileVersion.Split('.').Length);
    }

    [Fact]
    public void Product_IsConfigured()
    {
        Assert.Equal("键鼠统计", BuildInfo.Product);
    }
}
