using System;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class DiskCleanerTests
{
    [Theory]
    [InlineData("cache-dx-shaders")]
    [InlineData("temp-windows")]
    [InlineData("temp-user")]
    [InlineData("update-cache")]
    public void AllowedCategories_ShouldResolveToNonEmptyPath(string categoryKey)
    {
        string? path = DiskCleanerEngine.GetCategoryPath(categoryKey);
        Assert.False(string.IsNullOrWhiteSpace(path));
    }

    [Theory]
    [InlineData("arbitrary-system-delete")]
    [InlineData("windows-system32")]
    [InlineData("user-documents")]
    public void DisallowedCategories_MustReturnNull(string invalidCategory)
    {
        string? path = DiskCleanerEngine.GetCategoryPath(invalidCategory);
        Assert.Null(path);
    }

    [Fact]
    public void IsPathSafe_DangerousSystemPaths_ReturnsFalse()
    {
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.False(DiskCleanerEngine.IsPathSafe(winDir));
        Assert.False(DiskCleanerEngine.IsPathSafe(sys32));
        Assert.False(DiskCleanerEngine.IsPathSafe(userProfile));
    }

    [Fact]
    public void PurgeCategory_WithInvalidCategory_ReturnsZeroFreedBytes()
    {
        long freed = DiskCleanerEngine.PurgeCategory("invalid-hacker-path");
        Assert.Equal(0, freed);
    }
}
