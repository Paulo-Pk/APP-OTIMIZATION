using System;
using System.Collections.Generic;
using System.IO;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class DiskCleanerTests
{
    private static string CreateIsolatedTempFolder(string suffix)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"pccontrol_clean_test_{suffix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

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
        string tempDir = CreateIsolatedTempFolder("regular");
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
        Assert.Equal(DiskPurgeStatus.Failed, report.Status);
        Assert.Equal(0, report.BytesFreed);
    }

    [Fact]
    public void SimulateCategoryDryRun_WithInvalidCategory_ReturnsFailureWithoutDeleting()
    {
        var dryRun = DiskCleanerEngine.SimulateCategoryDryRun("forbidden-category");
        Assert.False(dryRun.Success);
        Assert.Equal(DiskPurgeStatus.Failed, dryRun.Status);
        Assert.Equal(0, dryRun.EstimatedBytes);
        Assert.Equal(0, dryRun.EstimatedFilesCount);
    }

    [Fact]
    public void SimulateCategoryDryRun_WithValidCategory_ReturnsAccurateEstimateAndLeavesFilesIntact()
    {
        string tempDir = CreateIsolatedTempFolder("dryrun_valid");
        try
        {
            string fileA = Path.Combine(tempDir, "shader_a.bin");
            byte[] payload = new byte[2048];
            File.WriteAllBytes(fileA, payload);

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir }
            );

            var dryRun = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
            Assert.True(dryRun.Success);
            Assert.Equal(DiskPurgeStatus.Completed, dryRun.Status);
            Assert.Equal(2048, dryRun.EstimatedBytes);
            Assert.Equal(1, dryRun.EstimatedFilesCount);
            Assert.True(File.Exists(fileA));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void NonExistentDirectory_DryRunAndPurge_ReturnNothingToCleanAndSuccessFalse()
    {
        string missingDir = Path.Combine(Path.GetTempPath(), $"pccontrol_nonexistent_{Guid.NewGuid():N}");
        if (Directory.Exists(missingDir)) Directory.Delete(missingDir, true);

        using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
            new Dictionary<string, string> { ["cache-dx-shaders"] = missingDir }
        );

        var dryRun = DiskCleanerEngine.SimulateCategoryDryRun("cache-dx-shaders");
        Assert.Equal(DiskPurgeStatus.NothingToClean, dryRun.Status);
        Assert.False(dryRun.Success);
        Assert.False(dryRun.DirectoryExisted);
        Assert.Equal(0, dryRun.CandidateFilesCount);
        Assert.Equal(0, dryRun.EstimatedBytes);
        Assert.NotEmpty(dryRun.Warnings);

        var purge = DiskCleanerEngine.PurgeCategoryWithReport("cache-dx-shaders");
        Assert.Equal(DiskPurgeStatus.NothingToClean, purge.Status);
        Assert.False(purge.Success);
        Assert.False(purge.DirectoryExisted);
        Assert.False(purge.PartialSuccess);
        Assert.Equal(0, purge.DeletedFilesCount);
        Assert.Equal(0, purge.FreedBytes);
        Assert.NotEmpty(purge.Warnings);
        Assert.Empty(purge.Errors);
    }

    [Fact]
    public void EmptyDirectory_DryRunAndPurge_ReturnNothingToCleanAndSuccessFalse()
    {
        string emptyDir = CreateIsolatedTempFolder("empty");
        try
        {
            Directory.CreateDirectory(Path.Combine(emptyDir, "EmptySubfolder"));

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = emptyDir }
            );

            var dryRun = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
            Assert.Equal(DiskPurgeStatus.NothingToClean, dryRun.Status);
            Assert.False(dryRun.Success);
            Assert.True(dryRun.DirectoryExisted);
            Assert.Equal(0, dryRun.CandidateFilesCount);
            Assert.Equal(0, dryRun.EstimatedBytes);

            var purge = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");
            Assert.Equal(DiskPurgeStatus.NothingToClean, purge.Status);
            Assert.False(purge.Success);
            Assert.True(purge.DirectoryExisted);
            Assert.False(purge.PartialSuccess);
            Assert.Equal(0, purge.DeletedFilesCount);
            Assert.Equal(0, purge.FreedBytes);
        }
        finally
        {
            if (Directory.Exists(emptyDir)) Directory.Delete(emptyDir, true);
        }
    }

    [Fact]
    public void CompletedCleaning_RemovesAllEligibleFiles_AndCalculatesExactCountAndFreedBytes()
    {
        string tempDir = CreateIsolatedTempFolder("completed");
        try
        {
            string subDir = Path.Combine(tempDir, "Nested");
            Directory.CreateDirectory(subDir);

            string file1 = Path.Combine(tempDir, "cache1.tmp");
            string file2 = Path.Combine(subDir, "cache2.tmp");
            string file3 = Path.Combine(subDir, "cache3.tmp");

            File.WriteAllBytes(file1, new byte[1500]);
            File.WriteAllBytes(file2, new byte[2500]);
            File.WriteAllBytes(file3, new byte[4096]);

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir }
            );

            var report = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");

            Assert.Equal(DiskPurgeStatus.Completed, report.Status);
            Assert.True(report.Success);
            Assert.True(report.DirectoryExisted);
            Assert.False(report.PartialSuccess);
            Assert.Equal(3, report.DeletedFilesCount);
            Assert.Equal(1500 + 2500 + 4096, report.FreedBytes);
            Assert.Equal(0, report.SkippedLockedCount);
            Assert.Equal(0, report.SkippedSymlinksCount);
            Assert.Empty(report.Errors);

            Assert.False(File.Exists(file1));
            Assert.False(File.Exists(file2));
            Assert.False(File.Exists(file3));
            // Empty subfolder is cleaned up and root folder remains empty
            Assert.False(Directory.Exists(subDir));
            Assert.True(Directory.Exists(tempDir));
            Assert.Empty(Directory.GetFileSystemEntries(tempDir));

            // Subsequent purge on the now-empty directory must return NothingToClean
            var postPurgeReport = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");
            Assert.Equal(DiskPurgeStatus.NothingToClean, postPurgeReport.Status);
            Assert.False(postPurgeReport.Success);
            Assert.True(postPurgeReport.DirectoryExisted);
            Assert.Equal(0, postPurgeReport.DeletedFilesCount);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsSubPathOfOrEqual_SimilarPrefixSiblingDirectory_ReturnsFalseAndRejectsTraversal()
    {
        string baseRoot = Path.Combine(Path.GetTempPath(), "pccontrol_authorized_root");
        string siblingWithSamePrefix = Path.Combine(Path.GetTempPath(), "pccontrol_authorized_root_sibling");
        string siblingNestedFile = Path.Combine(siblingWithSamePrefix, "secret.txt");
        string validChildFile = Path.Combine(baseRoot, "SubFolder", "cache.bin");
        string traversalEscape = Path.Combine(baseRoot, "SubFolder", "..", "..", "pccontrol_authorized_root_sibling", "secret.txt");

        Assert.True(DiskCleanerEngine.IsSubPathOfOrEqual(baseRoot, baseRoot));
        Assert.True(DiskCleanerEngine.IsSubPathOfOrEqual(validChildFile, baseRoot));
        Assert.False(DiskCleanerEngine.IsSubPathOfOrEqual(siblingWithSamePrefix, baseRoot));
        Assert.False(DiskCleanerEngine.IsSubPathOfOrEqual(siblingNestedFile, baseRoot));
        Assert.False(DiskCleanerEngine.IsSubPathOfOrEqual(traversalEscape, baseRoot));
    }

    [Fact]
    public void SimulateCategoryDryRun_WhenReadFailureOccurs_RecordsErrorAndClassifiesPartialOrFailedWithoutDeleting()
    {
        string tempDir = CreateIsolatedTempFolder("dryrun_read_error");
        try
        {
            string readableFile = Path.Combine(tempDir, "readable.tmp");
            string unreadableFile = Path.Combine(tempDir, "unreadable.tmp");
            File.WriteAllBytes(readableFile, new byte[1024]);
            File.WriteAllBytes(unreadableFile, new byte[2048]);

            // 1. Partial Dry-Run: one readable file + one file throwing UnauthorizedAccessException
            using (DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir },
                simulatedFileFailureHook: path =>
                {
                    if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(unreadableFile), StringComparison.OrdinalIgnoreCase))
                    {
                        return new UnauthorizedAccessException("Access denied while reading metadata during dry-run.");
                    }
                    return null;
                }))
            {
                var partialDryRun = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
                Assert.Equal(DiskPurgeStatus.Partial, partialDryRun.Status);
                Assert.True(partialDryRun.Success);
                Assert.True(partialDryRun.DirectoryExisted);
                Assert.Equal(1, partialDryRun.CandidateFilesCount);
                Assert.Equal(1024, partialDryRun.EstimatedBytes);
                Assert.Single(partialDryRun.Errors);
                Assert.True(File.Exists(readableFile));
                Assert.True(File.Exists(unreadableFile));
            }

            // 2. Failed Dry-Run: all files fail to read during dry-run
            using (DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir },
                simulatedFileFailureHook: _ => new IOException("I/O error while reading file metadata.")))
            {
                var failedDryRun = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
                Assert.Equal(DiskPurgeStatus.Failed, failedDryRun.Status);
                Assert.False(failedDryRun.Success);
                Assert.True(failedDryRun.DirectoryExisted);
                Assert.Equal(0, failedDryRun.CandidateFilesCount);
                Assert.Equal(0, failedDryRun.EstimatedBytes);
                Assert.NotEmpty(failedDryRun.Errors);
                Assert.True(File.Exists(readableFile));
                Assert.True(File.Exists(unreadableFile));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LockedFile_WithUnlockedFiles_ReturnsPartialSuccessAndPreservesLockedFile()
    {
        string tempDir = CreateIsolatedTempFolder("locked_partial");
        try
        {
            string unlockedFile = Path.Combine(tempDir, "unlocked.tmp");
            string lockedFile = Path.Combine(tempDir, "locked.tmp");
            File.WriteAllBytes(unlockedFile, new byte[1024]);
            File.WriteAllBytes(lockedFile, new byte[2048]);

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir },
                simulatedFileFailureHook: path =>
                {
                    if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(lockedFile), StringComparison.OrdinalIgnoreCase))
                    {
                        return new IOException("The process cannot access the file because it is being used by another process.");
                    }
                    return null;
                }
            );

            var report = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");

            Assert.Equal(DiskPurgeStatus.Partial, report.Status);
            Assert.True(report.Success);
            Assert.True(report.PartialSuccess);
            Assert.Equal(1, report.DeletedFilesCount);
            Assert.Equal(1024, report.FreedBytes);
            Assert.Equal(1, report.SkippedLockedCount);
            Assert.NotEmpty(report.Warnings);
            Assert.False(File.Exists(unlockedFile));
            Assert.True(File.Exists(lockedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LockedFile_WhenOnlyLockedFileExists_ReturnsFailedStatusAndSuccessFalse()
    {
        string tempDir = CreateIsolatedTempFolder("locked_only");
        try
        {
            string lockedFile = Path.Combine(tempDir, "locked_only.tmp");
            File.WriteAllBytes(lockedFile, new byte[4096]);

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir },
                simulatedFileFailureHook: _ => new IOException("Sharing violation: file in use.")
            );

            var report = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");

            Assert.Equal(DiskPurgeStatus.Failed, report.Status);
            Assert.False(report.Success);
            Assert.False(report.PartialSuccess);
            Assert.Equal(0, report.DeletedFilesCount);
            Assert.Equal(0, report.FreedBytes);
            Assert.Equal(1, report.SkippedLockedCount);
            Assert.True(File.Exists(lockedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AccessError_DuringPurge_RecordsErrorAndClassifiesPartialOrFailedAccurately()
    {
        string tempDir = CreateIsolatedTempFolder("access_error");
        try
        {
            string normalFile = Path.Combine(tempDir, "normal.tmp");
            string deniedFile = Path.Combine(tempDir, "denied.tmp");
            File.WriteAllBytes(normalFile, new byte[512]);
            File.WriteAllBytes(deniedFile, new byte[1024]);

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir },
                simulatedFileFailureHook: path =>
                {
                    if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(deniedFile), StringComparison.OrdinalIgnoreCase))
                    {
                        return new UnauthorizedAccessException("Access to the path is denied.");
                    }
                    return null;
                }
            );

            var report = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");

            Assert.Equal(DiskPurgeStatus.Partial, report.Status);
            Assert.True(report.Success);
            Assert.True(report.PartialSuccess);
            Assert.Equal(1, report.DeletedFilesCount);
            Assert.Equal(512, report.FreedBytes);
            Assert.Single(report.Errors);
            Assert.Contains("Acesso negado", report.Errors[0], StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(deniedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PreservedSymlink_NeverFollowedOrDeleted_AndIncludedInPartialOrNothingToCleanClassification()
    {
        string tempDir = CreateIsolatedTempFolder("symlink_test");
        string externalTargetDir = CreateIsolatedTempFolder("symlink_external_target");
        try
        {
            string regularFile = Path.Combine(tempDir, "regular.tmp");
            File.WriteAllBytes(regularFile, new byte[768]);

            string externalProtectedFile = Path.Combine(externalTargetDir, "do_not_delete.txt");
            File.WriteAllBytes(externalProtectedFile, new byte[1024]);

            string symlinkPath = Path.Combine(tempDir, "linked_file.tmp");
            bool symlinkCreated = false;
            try
            {
                File.CreateSymbolicLink(symlinkPath, externalProtectedFile);
                symlinkCreated = true;
            }
            catch
            {
                // Fallback if OS privilege blocks symlink creation: mark ReparsePoint attribute if supported
                File.WriteAllBytes(symlinkPath, new byte[128]);
                try
                {
                    File.SetAttributes(symlinkPath, File.GetAttributes(symlinkPath) | FileAttributes.ReparsePoint);
                    symlinkCreated = (File.GetAttributes(symlinkPath) & FileAttributes.ReparsePoint) != 0;
                }
                catch
                {
                    symlinkCreated = false;
                }
            }

            if (!symlinkCreated)
            {
                return;
            }

            using var scope = DiskCleanerEngine.UseCategoryPathOverrideScope(
                new Dictionary<string, string> { ["temp-user"] = tempDir }
            );

            // 1. Purge when regularFile + symlink exist -> regularFile deleted, symlink preserved -> Partial
            var partialReport = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");
            Assert.Equal(DiskPurgeStatus.Partial, partialReport.Status);
            Assert.True(partialReport.Success);
            Assert.True(partialReport.PartialSuccess);
            Assert.Equal(1, partialReport.DeletedFilesCount);
            Assert.Equal(768, partialReport.FreedBytes);
            Assert.Equal(1, partialReport.SkippedSymlinksCount);
            Assert.True(File.Exists(externalProtectedFile));

            // 2. Purge again when only the preserved symlink remains -> 0 deleted -> NothingToClean (Success = false)
            var secondReport = DiskCleanerEngine.PurgeCategoryWithReport("temp-user");
            Assert.Equal(DiskPurgeStatus.NothingToClean, secondReport.Status);
            Assert.False(secondReport.Success);
            Assert.Equal(0, secondReport.DeletedFilesCount);
            Assert.Equal(1, secondReport.SkippedSymlinksCount);
            Assert.True(File.Exists(externalProtectedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(externalTargetDir)) Directory.Delete(externalTargetDir, true);
        }
    }

    [Fact]
    public void CategoryPathOverrideScope_DisposesCleanly_AndDoesNotBypassAllowedCategoryWhitelist()
    {
        string tempDir = CreateIsolatedTempFolder("scope_guard");
        try
        {
            string originalPath = DiskCleanerEngine.GetCategoryPath("temp-user")!;

            using (DiskCleanerEngine.UseCategoryPathOverrideScope(new Dictionary<string, string>
            {
                ["temp-user"] = tempDir,
                ["unlisted-category"] = tempDir
            }))
            {
                Assert.Equal(tempDir, DiskCleanerEngine.GetCategoryPath("temp-user"));
                // Unlisted category must still return null even if passed to override dictionary
                Assert.Null(DiskCleanerEngine.GetCategoryPath("unlisted-category"));
            }

            // Disposing the scope must restore the production path immediately
            Assert.Equal(originalPath, DiskCleanerEngine.GetCategoryPath("temp-user"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}


