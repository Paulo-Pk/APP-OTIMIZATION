using System;
using System.Collections.Generic;
using System.IO;

namespace PCControlUltimate.Windows.Services;

public record DiskPurgeReport(
    string CategoryKey,
    long FreedBytes,
    int DeletedFilesCount,
    int SkippedLockedCount,
    int SkippedSymlinksCount,
    IReadOnlyList<string> Errors
);

public static class DiskCleanerEngine
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache-dx-shaders",
        "temp-windows",
        "temp-user",
        "update-cache"
    };

    public static string? GetCategoryPath(string categoryKey)
    {
        if (string.IsNullOrWhiteSpace(categoryKey) || !AllowedCategories.Contains(categoryKey))
            return null;

        return categoryKey.ToLowerInvariant() switch
        {
            "cache-dx-shaders" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
            "temp-windows" => @"C:\Windows\Temp",
            "temp-user" => Path.GetTempPath(),
            "update-cache" => @"C:\Windows\SoftwareDistribution\Download",
            _ => null
        };
    }

    public static bool IsPathSafe(string? targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir)) return false;
        if (targetDir.Contains("..")) return false;

        try
        {
            string fullPath = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(fullPath)) return false;

            // Block root drives (e.g., C: or /)
            string? root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)) return false;

            var protectedFolders = new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic,
                Environment.SpecialFolder.MyVideos
            };

            foreach (var folder in protectedFolders)
            {
                string folderPath = Environment.GetFolderPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrEmpty(folderPath) &&
                    fullPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Ensure personal subfolders (Documents, Desktop, Pictures) are never inside targetDir
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs) && fullPath.StartsWith(docs, StringComparison.OrdinalIgnoreCase))
                return false;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(desktop) && fullPath.StartsWith(desktop, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsReparsePointOrSymlink(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget != null;
        }
        catch
        {
            return true; // Fail safe: treat unreadable attribute as unsafe
        }
    }

    public static long ScanCategoryBytes(string categoryKey)
    {
        string? targetDir = GetCategoryPath(categoryKey);
        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir) || !Directory.Exists(targetDir))
            return 0;

        var rootDir = new DirectoryInfo(targetDir);
        if (IsReparsePointOrSymlink(rootDir))
            return 0;

        return ScanDirectoryRecursiveSafe(rootDir);
    }

    private static long ScanDirectoryRecursiveSafe(DirectoryInfo dir)
    {
        long bytes = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                try
                {
                    if (!IsReparsePointOrSymlink(file))
                        bytes += file.Length;
                }
                catch { }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                try
                {
                    if (!IsReparsePointOrSymlink(subDir))
                        bytes += ScanDirectoryRecursiveSafe(subDir);
                }
                catch { }
            }
        }
        catch { }

        return bytes;
    }

    public static DiskPurgeReport PurgeCategoryWithReport(string categoryKey, bool confirmUpdateCache = false)
    {
        var errors = new List<string>();
        string? targetDir = GetCategoryPath(categoryKey);

        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir))
        {
            errors.Add("Categoria não autorizada ou caminho inseguro rejeitado.");
            return new DiskPurgeReport(categoryKey, 0, 0, 0, 0, errors);
        }

        if (string.Equals(categoryKey, "update-cache", StringComparison.OrdinalIgnoreCase) && !confirmUpdateCache)
        {
            errors.Add("A limpeza do cache do Windows Update exige confirmação explícita.");
            return new DiskPurgeReport(categoryKey, 0, 0, 0, 0, errors);
        }

        if (!Directory.Exists(targetDir))
        {
            return new DiskPurgeReport(categoryKey, 0, 0, 0, 0, errors);
        }

        var rootDir = new DirectoryInfo(targetDir);
        if (IsReparsePointOrSymlink(rootDir))
        {
            errors.Add("Operação abortada: o diretório alvo é um link simbólico ou junction NTFS.");
            return new DiskPurgeReport(categoryKey, 0, 0, 0, 1, errors);
        }

        long freedBytes = 0;
        int deletedFiles = 0;
        int skippedLocked = 0;
        int skippedSymlinks = 0;

        PurgeDirectoryRecursiveSafe(
            rootDir,
            Path.GetFullPath(targetDir),
            isRoot: true,
            ref freedBytes,
            ref deletedFiles,
            ref skippedLocked,
            ref skippedSymlinks
        );

        return new DiskPurgeReport(categoryKey, freedBytes, deletedFiles, skippedLocked, skippedSymlinks, errors);
    }

    private static void PurgeDirectoryRecursiveSafe(
        DirectoryInfo currentDir,
        string canonicalRoot,
        bool isRoot,
        ref long freedBytes,
        ref int deletedFiles,
        ref int skippedLocked,
        ref int skippedSymlinks)
    {
        try
        {
            string currentCanonical = Path.GetFullPath(currentDir.FullName);
            if (!currentCanonical.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return; // Prevent directory traversal outside root
            }

            foreach (var file in currentDir.EnumerateFiles())
            {
                try
                {
                    if (IsReparsePointOrSymlink(file))
                    {
                        skippedSymlinks++;
                        continue;
                    }

                    long size = file.Length;
                    file.Delete();
                    freedBytes += size;
                    deletedFiles++;
                }
                catch
                {
                    skippedLocked++;
                }
            }

            foreach (var subDir in currentDir.EnumerateDirectories())
            {
                try
                {
                    if (IsReparsePointOrSymlink(subDir))
                    {
                        skippedSymlinks++;
                        continue;
                    }

                    PurgeDirectoryRecursiveSafe(
                        subDir,
                        canonicalRoot,
                        isRoot: false,
                        ref freedBytes,
                        ref deletedFiles,
                        ref skippedLocked,
                        ref skippedSymlinks
                    );

                    // Only delete the subdirectory if it is now empty and not a reparse point
                    if (subDir.GetFileSystemInfos().Length == 0)
                    {
                        subDir.Delete(false);
                    }
                }
                catch
                {
                    skippedLocked++;
                }
            }
        }
        catch
        {
            skippedLocked++;
        }
    }

    public static long PurgeCategory(string categoryKey)
    {
        var report = PurgeCategoryWithReport(categoryKey, confirmUpdateCache: true);
        return report.FreedBytes;
    }
}