using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCControlUltimate.Windows.Services;

public record CleanerCategoryMetadata(
    string CategoryKey,
    string NamePt,
    string NameEn,
    string TargetPath,
    bool RequiresAdminUac,
    bool RequiresExplicitConfirmation
);

public record DiskDryRunReport(
    string CategoryKey,
    string TargetPath,
    bool IsEstimateOnly,
    long EstimatedBytes,
    int CandidateFilesCount,
    int SkippedSymlinksCount,
    bool Success = true,
    IReadOnlyList<string>? Errors = null
)
{
    public int EstimatedFilesCount => CandidateFilesCount;
}

public record DiskPurgeReport(
    bool Success,
    bool PartialSuccess,
    string CategoryKey,
    long FreedBytes,
    int DeletedFilesCount,
    int SkippedLockedCount,
    int SkippedSymlinksCount,
    IReadOnlyList<string> AffectedItemsLog,
    IReadOnlyList<string> Errors,
    string Status = "Completed",
    bool DirectoryExisted = true,
    IReadOnlyList<string>? Warnings = null
)
{
    public long BytesFreed => FreedBytes;
}

public static class DiskCleanerEngine
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache-dx-shaders",
        "temp-windows",
        "temp-user",
        "update-cache"
    };

    public static IReadOnlyList<CleanerCategoryMetadata> GetAuthorizedCategories()
    {
        return new List<CleanerCategoryMetadata>
        {
            new("cache-dx-shaders", "Cache de Shaders DirectX (D3DSCache)", "DirectX Shader Cache (D3DSCache)",
                GetCategoryPath("cache-dx-shaders") ?? "", false, true),
            new("temp-user", "Arquivos Temporários do Usuário (%TEMP%)", "User Temporary Files (%TEMP%)",
                GetCategoryPath("temp-user") ?? "", false, true),
            new("temp-windows", "Arquivos Temporários do Sistema (C:\\Windows\\Temp)", "System Temporary Files (C:\\Windows\\Temp)",
                GetCategoryPath("temp-windows") ?? "", true, true),
            new("update-cache", "Cache de Downloads do Windows Update (SoftwareDistribution)", "Windows Update Download Cache (SoftwareDistribution)",
                GetCategoryPath("update-cache") ?? "", true, true)
        };
    }

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

            // Block any personal directory subtree
            var personalSubtrees = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            foreach (var personal in personalSubtrees)
            {
                if (!string.IsNullOrEmpty(personal) && IsSameOrDescendantPath(fullPath, personal))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSameOrDescendantPath(string candidatePath, string parentPath)
    {
        string candidate = Path.GetFullPath(candidatePath);
        string parent = Path.GetFullPath(parentPath);
        string relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
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

    /// <summary>
    /// Performs a read-only dry-run scan and explicitly marks the result as an estimate.
    /// </summary>
    public static DiskDryRunReport SimulateCategoryDryRun(string categoryKey)
    {
        string? targetDir = GetCategoryPath(categoryKey);
        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir))
        {
            return new DiskDryRunReport(categoryKey, targetDir ?? "N/D", true, 0, 0, 0, Success: false);
        }

        if (!Directory.Exists(targetDir))
        {
            return new DiskDryRunReport(categoryKey, targetDir, true, 0, 0, 0, Success: false);
        }

        var rootDir = new DirectoryInfo(targetDir);
        if (IsReparsePointOrSymlink(rootDir))
        {
            return new DiskDryRunReport(categoryKey, targetDir, true, 0, 0, 1, Success: false);
        }

        long bytes = 0;
        int files = 0;
        int symlinks = 0;
        var scanErrors = new List<string>();
        ScanDirectoryRecursiveSafe(rootDir, ref bytes, ref files, ref symlinks, scanErrors);

        return new DiskDryRunReport(categoryKey, targetDir, true, bytes, files, symlinks,
            Success: scanErrors.Count == 0, Errors: scanErrors);
    }

    public static long ScanCategoryBytes(string categoryKey)
    {
        var dryRun = SimulateCategoryDryRun(categoryKey);
        return dryRun.EstimatedBytes;
    }

    private static void ScanDirectoryRecursiveSafe(DirectoryInfo dir, ref long bytes, ref int filesCount, ref int symlinksCount, List<string> errors)
    {
        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                try
                {
                    if (IsReparsePointOrSymlink(file))
                    {
                        symlinksCount++;
                        continue;
                    }
                    bytes += file.Length;
                    filesCount++;
                }
                catch (Exception ex)
                {
                    if (errors.Count < 100) errors.Add($"Falha ao inspecionar arquivo '{file.FullName}': {ex.Message}");
                }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                try
                {
                    if (IsReparsePointOrSymlink(subDir))
                    {
                        symlinksCount++;
                        continue;
                    }
                    ScanDirectoryRecursiveSafe(subDir, ref bytes, ref filesCount, ref symlinksCount, errors);
                }
                catch (Exception ex)
                {
                    if (errors.Count < 100) errors.Add($"Falha ao inspecionar pasta '{subDir.FullName}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            if (errors.Count < 100) errors.Add($"Falha ao enumerar pasta '{dir.FullName}': {ex.Message}");
        }
    }

    public static DiskPurgeReport PurgeCategoryWithReport(string categoryKey, bool confirmServiceStop = false)
    {
        var errors = new List<string>();
        var affectedItems = new List<string>();
        string? targetDir = GetCategoryPath(categoryKey);

        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir))
        {
            errors.Add("Categoria não autorizada ou caminho inseguro rejeitado.");
            return new DiskPurgeReport(false, false, categoryKey, 0, 0, 0, 0, affectedItems, errors);
        }

        if (string.Equals(categoryKey, "update-cache", StringComparison.OrdinalIgnoreCase) && !confirmServiceStop)
        {
            errors.Add("A limpeza do cache do Windows Update exige confirmação explícita do usuário.");
            return new DiskPurgeReport(false, false, categoryKey, 0, 0, 0, 0, affectedItems, errors);
        }

        if (!Directory.Exists(targetDir))
        {
            errors.Add("O diretório autorizado não existe; nenhuma limpeza foi executada.");
            return new DiskPurgeReport(false, false, categoryKey, 0, 0, 0, 0, affectedItems, errors,
                Status: "DirectoryMissing", DirectoryExisted: false);
        }

        var rootDir = new DirectoryInfo(targetDir);
        if (IsReparsePointOrSymlink(rootDir))
        {
            errors.Add("Operação abortada: o diretório alvo é um link simbólico ou junction NTFS.");
            return new DiskPurgeReport(false, false, categoryKey, 0, 0, 0, 1, affectedItems, errors);
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
            ref skippedSymlinks,
            affectedItems,
            errors
        );

        bool hasSkippedItems = skippedLocked > 0 || skippedSymlinks > 0;
        bool fullSuccess = errors.Count == 0 && !hasSkippedItems && deletedFiles > 0;
        bool partialSuccess = (deletedFiles > 0 && (hasSkippedItems || errors.Count > 0)) ||
                              (deletedFiles == 0 && hasSkippedItems && errors.Count == 0);
        bool nothingToClean = deletedFiles == 0 && skippedLocked == 0 && skippedSymlinks == 0 && errors.Count == 0;
        string status = fullSuccess ? "Completed" :
            partialSuccess ? "Partial" :
            nothingToClean ? "NothingToClean" : "Failed";

        return new DiskPurgeReport(
            Success: fullSuccess || partialSuccess || nothingToClean,
            PartialSuccess: partialSuccess,
            CategoryKey: categoryKey,
            FreedBytes: freedBytes,
            DeletedFilesCount: deletedFiles,
            SkippedLockedCount: skippedLocked,
            SkippedSymlinksCount: skippedSymlinks,
            AffectedItemsLog: affectedItems.Take(100).ToList(),
            Errors: errors,
            Status: status,
            DirectoryExisted: true,
            Warnings: skippedSymlinks > 0
                ? new[] { $"{skippedSymlinks} link(s) simbólico(s) ou junction(s) foram preservados." }
                : Array.Empty<string>()
        );
    }

    private static void PurgeDirectoryRecursiveSafe(
        DirectoryInfo currentDir,
        string canonicalRoot,
        bool isRoot,
        ref long freedBytes,
        ref int deletedFiles,
        ref int skippedLocked,
        ref int skippedSymlinks,
        List<string> affectedItems,
        List<string> errors)
    {
        try
        {
            string currentCanonical = Path.GetFullPath(currentDir.FullName);
            string relativePath = Path.GetRelativePath(canonicalRoot, currentCanonical);
            if (Path.IsPathRooted(relativePath) ||
                relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                errors.Add($"Caminho fora da raiz autorizada bloqueado: '{currentCanonical}'.");
                return;
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
                    string fullName = file.FullName;
                    file.Delete();
                    freedBytes += size;
                    deletedFiles++;
                    if (affectedItems.Count < 100)
                    {
                        affectedItems.Add($"[DEL] {fullName} ({size} B)");
                    }
                }
                catch (Exception ex)
                {
                    skippedLocked++;
                    if (errors.Count < 100) errors.Add($"Falha ao excluir arquivo '{file.FullName}': {ex.Message}");
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
                        ref skippedSymlinks,
                        affectedItems,
                        errors
                    );

                    if (subDir.GetFileSystemInfos().Length == 0)
                    {
                        string dirName = subDir.FullName;
                        subDir.Delete(false);
                        if (affectedItems.Count < 100)
                        {
                            affectedItems.Add($"[DIR] {dirName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    skippedLocked++;
                    if (errors.Count < 100) errors.Add($"Falha ao processar pasta '{subDir.FullName}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            skippedLocked++;
            if (errors.Count < 100) errors.Add($"Falha ao acessar pasta '{currentDir.FullName}': {ex.Message}");
        }
    }

    public static long PurgeCategory(string categoryKey)
    {
        var report = PurgeCategoryWithReport(categoryKey, confirmServiceStop: false);
        return report.FreedBytes;
    }
}