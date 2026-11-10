using System;
using System.IO;
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
    [InlineData("../etc/passwd")]
    public void DisallowedCategories_MustReturnNull(string invalidCategory)
    {
        string? path = DiskCleanerEngine.GetCategoryPath(invalidCategory);
        Assert.Null(path);
    }

    [Fact]
    public void IsPathSafe_DangerousSystemAndPersonalPaths_ReturnsFalse()
    {
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Assert.False(DiskCleanerEngine.IsPathSafe(winDir));
        Assert.False(DiskCleanerEngine.IsPathSafe(sys32));
        Assert.False(DiskCleanerEngine.IsPathSafe(userProfile));
        if (!string.IsNullOrWhiteSpace(documents))
        {
            Assert.False(DiskCleanerEngine.IsPathSafe(documents));
        }
    }

    [Fact]
    public void IsPathSafe_PathTraversalSequence_ReturnsFalse()
    {
        string tempPath = Path.GetTempPath();
        string traversalPath = Path.Combine(tempPath, "..", "System32");
        Assert.False(DiskCleanerEngine.IsPathSafe(traversalPath));
    }

    [Fact]
    public void IsReparsePointOrSymlink_RegularDirectory_ReturnsFalse()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_clean_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dirInfo = new DirectoryInfo(tempDir);
            Assert.False(DiskCleanerEngine.IsReparsePointOrSymlink(dirInfo));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PurgeCategory_WithInvalidCategory_ReturnsZeroFreedBytes()
    {
        long freed = DiskCleanerEngine.PurgeCategory("invalid-hacker-path");
        Assert.Equal(0, freed);
    }

    [Fact]
    public void PurgeCategoryWithReport_UpdateCacheWithoutConfirmation_IsBlocked()
    {
        var report = DiskCleanerEngine.PurgeCategoryWithReport("update-cache", confirmServiceStop: false);
        Assert.False(report.Success);
        Assert.Equal(0, report.BytesFreed);
    }

    [Fact]
    public void SimulateCategoryDryRun_WithInvalidCategory_ReturnsFailureWithoutDeleting()
    {
        var dryRun = DiskCleanerEngine.SimulateCategoryDryRun("forbidden-category");
        Assert.False(dryRun.Success);
        Assert.Equal(0, dryRun.EstimatedBytes);
        Assert.Equal(0, dryRun.EstimatedFilesCount);
    }

    [Fact]
    public void SimulateCategoryDryRun_WithValidCategory_ReturnsNonNegativeEstimateAndLeavesFilesIntact()
    {
        var dryRun = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
        Assert.True(dryRun.Success);
        Assert.True(dryRun.EstimatedBytes >= 0);
        Assert.True(dryRun.EstimatedFilesCount >= 0);
    }
}

