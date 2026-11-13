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

    [Fact]
    public void PurgeDirectoryForTesting_SkipsFileSymlinkAndPreservesExternalTarget()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_symlink_{Guid.NewGuid():N}");
        string outsideDir = Path.Combine(Path.GetTempPath(), $"pccontrol_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outsideDir);
        string externalFile = Path.Combine(outsideDir, "keep.txt");
        string linkPath = Path.Combine(tempDir, "external-link.txt");
        File.WriteAllText(externalFile, "must remain untouched");
        File.WriteAllText(Path.Combine(tempDir, "inside.txt"), "safe to remove");

        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, externalFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException ||
                                       ex is PlatformNotSupportedException || ex is NotSupportedException)
            {
                // Symlink creation can be unavailable on Windows without Developer Mode/admin rights.
                return;
            }

            var report = DiskCleanerEngine.PurgeDirectoryForTesting("temp-user", tempDir);

            Assert.True(report.Success);
            Assert.True(report.PartialSuccess);
            Assert.Equal(1, report.DeletedFilesCount);
            Assert.Equal(1, report.SkippedSymlinksCount);
            Assert.False(File.Exists(Path.Combine(tempDir, "inside.txt")));
            Assert.True(File.Exists(externalFile));
            Assert.True(File.Exists(linkPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, recursive: true);
        }
    }

    [Fact]
    public void SimulateDirectoryDryRunForTesting_SkipsDirectorySymlinkAndDoesNotCountExternalFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"pccontrol_dry_symlink_{Guid.NewGuid():N}");
        string outsideDir = Path.Combine(Path.GetTempPath(), $"pccontrol_dry_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outsideDir);
        string externalFile = Path.Combine(outsideDir, "outside.txt");
        string linkPath = Path.Combine(tempDir, "external-dir");
        File.WriteAllText(externalFile, "outside");
        File.WriteAllText(Path.Combine(tempDir, "inside.txt"), "inside");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, outsideDir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException ||
                                       ex is PlatformNotSupportedException || ex is NotSupportedException)
            {
                return;
            }

            var report = DiskCleanerEngine.SimulateDirectoryDryRunForTesting("temp-user", tempDir);

            Assert.True(report.Success);
            Assert.Equal(1, report.EstimatedFilesCount);
            Assert.Equal(6, report.EstimatedBytes);
            Assert.Equal(1, report.SkippedSymlinksCount);
            Assert.True(File.Exists(externalFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, recursive: true);
        }
    }

}

