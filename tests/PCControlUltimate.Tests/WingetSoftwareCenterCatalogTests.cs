using System.Linq;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public sealed class WingetSoftwareCenterCatalogTests
{
    [Fact]
    public void OfficialCatalog_ContainsUniquePackageIds()
    {
        var packageIds = WingetSoftwareCenterService.OfficialCatalog
            .Select(package => package.PackageId)
            .ToArray();

        Assert.Equal(packageIds.Length, packageIds.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void XboxStorePackage_UsesMicrosoftStoreSource()
    {
        var package = WingetSoftwareCenterService.OfficialCatalog
            .SingleOrDefault(item => item.PackageId == "9MV0B5HZVK9Z");

        Assert.NotNull(package);
        Assert.Equal("msstore", package!.OfficialSource);
    }

    [Fact]
    public void OfficialCatalog_UsesOnlySupportedOfficialSources()
    {
        Assert.All(
            WingetSoftwareCenterService.OfficialCatalog,
            package => Assert.True(
                WingetSoftwareCenterService.IsTrustedSourceOrigin(package.OfficialSource),
                $"Package {package.PackageId} has an unsupported source: {package.OfficialSource}"));
    }
}
