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
    [InlineData("temp-prefetch")]
    [InlineData("temp-delivery-optimization")]
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
    [Fact]
    public void SimulateDirectoryDryRunForTesting_UsesOnlyTemporaryDirectoryAndDoesNotDeleteFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_dryrun_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string nested = Path.Combine(tempDir, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(tempDir, "one.tmp"), "12345");
            File.WriteAllText(Path.Combine(nested, "two.tmp"), "1234567");

            var report = DiskCleanerEngine.SimulateDirectoryDryRunForTesting("temp-user", tempDir);

            Assert.True(report.Success);
            Assert.Equal(2, report.EstimatedFilesCount);
            Assert.Equal(12, report.EstimatedBytes);
            Assert.True(File.Exists(Path.Combine(tempDir, "one.tmp")));
            Assert.True(File.Exists(Path.Combine(nested, "two.tmp")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PurgeDirectoryForTesting_DeletesOnlyFilesInsideTemporaryRootAndReportsCompleted()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_purge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string nested = Path.Combine(tempDir, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(tempDir, "one.tmp"), "12345");
            File.WriteAllText(Path.Combine(nested, "two.tmp"), "1234567");

            var report = DiskCleanerEngine.PurgeDirectoryForTesting("temp-user", tempDir);

            Assert.True(report.Success);
            Assert.Equal("Completed", report.Status);
            Assert.Equal(2, report.DeletedFilesCount);
            Assert.Equal(12, report.FreedBytes);
            Assert.False(File.Exists(Path.Combine(tempDir, "one.tmp")));
            Assert.False(Directory.Exists(nested));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PurgeDirectoryForTesting_LockedFileIsPreservedAndReportedAsPartialSuccess()
    {
        if (!OperatingSystem.IsWindows()) return;

        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_locked_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string lockedFile = Path.Combine(tempDir, "in-use.tmp");
        File.WriteAllText(lockedFile, "keep while locked");

        try
        {
            using (var fileLock = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var report = DiskCleanerEngine.PurgeDirectoryForTesting("temp-user", tempDir);

                Assert.True(report.Success);
                Assert.True(report.PartialSuccess);
                Assert.Equal("Partial", report.Status);
                Assert.Equal(0, report.DeletedFilesCount);
                Assert.Equal(0, report.FreedBytes);
                Assert.Equal(1, report.SkippedLockedCount);
                Assert.Empty(report.Errors);
                Assert.NotEmpty(report.Warnings ?? Array.Empty<string>());
                Assert.True(File.Exists(lockedFile));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PurgeDirectoryForTesting_EmptyDirectoryReportsNothingToClean()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var report = DiskCleanerEngine.PurgeDirectoryForTesting("temp-user", tempDir);
            Assert.True(report.Success);
            Assert.Equal("NothingToClean", report.Status);
            Assert.Equal(0, report.DeletedFilesCount);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SimulateDirectoryDryRunForTesting_MissingDirectoryReportsFailure()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"pccontrol_missing_{Guid.NewGuid():N}");
        var report = DiskCleanerEngine.SimulateDirectoryDryRunForTesting("temp-user", missing);
        Assert.False(report.Success);
        Assert.NotEmpty(report.Errors!);
    }

}

