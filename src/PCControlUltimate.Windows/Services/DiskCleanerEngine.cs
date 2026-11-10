using System;
using System.IO;
using System.Collections.Generic;

namespace PCControlUltimate.Windows.Services;

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
        if (!AllowedCategories.Contains(categoryKey))
            return null;

        return categoryKey switch
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
        
        string fullPath = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar);
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd(Path.DirectorySeparatorChar);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);

        // Disallow dangerous root paths
        if (fullPath.Equals(winDir, StringComparison.OrdinalIgnoreCase)) return false;
        if (fullPath.Equals(sys32, StringComparison.OrdinalIgnoreCase)) return false;
        if (fullPath.Equals(userProfile, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    public static long ScanCategoryBytes(string categoryKey)
    {
        string? targetDir = GetCategoryPath(categoryKey);
        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir) || !Directory.Exists(targetDir))
            return 0;

        long totalBytes = 0;
        try
        {
            var dir = new DirectoryInfo(targetDir);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    totalBytes += file.Length;
                }
                catch
                {
                    // Locked or permission issue
                }
            }
        }
        catch
        {
            // Directory access denied
        }

        return totalBytes;
    }

    public static long PurgeCategory(string categoryKey)
    {
        string? targetDir = GetCategoryPath(categoryKey);
        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir) || !Directory.Exists(targetDir))
            return 0;

        long freedBytes = 0;
        try
        {
            var dir = new DirectoryInfo(targetDir);

            // Delete files safely
            foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    long size = file.Length;
                    file.Delete();
                    freedBytes += size;
                }
                catch
                {
                    // Locked file in use by active OS process - skip cleanly
                }
            }

            // Clean subdirectories if safe
            foreach (var subDir in dir.EnumerateDirectories())
            {
                try
                {
                    foreach (var file in subDir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long size = file.Length;
                            file.Delete();
                            freedBytes += size;
                        }
                        catch
                        {
                            // In use
                        }
                    }
                    subDir.Delete(true);
                }
                catch
                {
                    // Directory in use
                }
            }
        }
        catch
        {
            // Root permission
        }

        return freedBytes;
    }
}