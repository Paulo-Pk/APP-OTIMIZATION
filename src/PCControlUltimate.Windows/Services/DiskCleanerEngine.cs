using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;

namespace PCControlUltimate.Windows.Services;

public enum DiskPurgeStatus
{
    Completed,
    Partial,
    NothingToClean,
    Failed
}

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
    bool DirectoryExisted = true,
    IReadOnlyList<string>? WarningsOrErrors = null
)
{
    public int EstimatedFilesCount => CandidateFilesCount;
    public IReadOnlyList<string> Errors => WarningsOrErrors ?? Array.Empty<string>();
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
    DiskPurgeStatus Status = DiskPurgeStatus.NothingToClean,
    bool DirectoryExisted = true
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

    private sealed class TestOverrideContext
    {
        public IReadOnlyDictionary<string, string>? CategoryPathOverrides { get; init; }
        public Func<string, Exception?>? SimulatedFileFailureHook { get; init; }
    }

    private static readonly AsyncLocal<TestOverrideContext?> _testOverrideContext = new();

    private sealed class TestOverrideScope : IDisposable
    {
        private readonly TestOverrideContext? _previous;
        private bool _disposed;

        public TestOverrideScope(TestOverrideContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _testOverrideContext.Value = _previous;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Internal test-only scope allowing DiskCleanerTests to map authorized categories to isolated temporary directories
    /// and simulate specific file access exceptions without touching real Windows system or user directories.
    /// Does NOT bypass AllowedCategories, IsPathSafe, symlink protection, or canonical root containment.
    /// </summary>
    internal static IDisposable UseCategoryPathOverrideScope(
        IReadOnlyDictionary<string, string>? categoryPathOverrides,
        Func<string, Exception?>? simulatedFileFailureHook = null)
    {
        var previous = _testOverrideContext.Value;
        _testOverrideContext.Value = new TestOverrideContext
        {
            CategoryPathOverrides = categoryPathOverrides,
            SimulatedFileFailureHook = simulatedFileFailureHook
        };
        return new TestOverrideScope(previous);
    }

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

        var testContext = _testOverrideContext.Value;
        if (testContext?.CategoryPathOverrides != null &&
            testContext.CategoryPathOverrides.TryGetValue(categoryKey.Trim(), out string? overridePath) &&
            !string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

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
                if (!string.IsNullOrEmpty(personal) && fullPath.StartsWith(personal, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

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

    /// <summary>
    /// Performs a read-only dry-run scan and explicitly marks the result as an estimate.
    /// Records any directory/file access errors encountered during scanning.
    /// </summary>
    public static DiskDryRunReport SimulateCategoryDryRun(string categoryKey)
    {
        var warningsOrErrors = new List<string>();
        string? targetDir = GetCategoryPath(categoryKey);
        if (string.IsNullOrEmpty(targetDir) || !IsPathSafe(targetDir))
        {
            warningsOrErrors.Add("Categoria não autorizada ou caminho inseguro rejeitado.");
            return new DiskDryRunReport(
                categoryKey,
                targetDir ?? "N/D",
                IsEstimateOnly: true,
                EstimatedBytes: 0,
                CandidateFilesCount: 0,
                SkippedSymlinksCount: 0,
                Success: false,
                DirectoryExisted: false,
                WarningsOrErrors: warningsOrErrors
            );
        }

        if (!Directory.Exists(targetDir))
        {
            return new DiskDryRunReport(
                categoryKey,
                targetDir,
                IsEstimateOnly: true,
                EstimatedBytes: 0,
                CandidateFilesCount: 0,
                SkippedSymlinksCount: 0,
                Success: true,
                DirectoryExisted: false,
                WarningsOrErrors: warningsOrErrors
            );
        }

        var rootDir = new DirectoryInfo(targetDir);
        if (IsReparsePointOrSymlink(rootDir))
        {
            warningsOrErrors.Add("Operação abortada: o diretório alvo é um link simbólico ou junction NTFS.");
            return new DiskDryRunReport(
                categoryKey,
                targetDir,
                IsEstimateOnly: true,
                EstimatedBytes: 0,
                CandidateFilesCount: 0,
                SkippedSymlinksCount: 1,
                Success: false,
                DirectoryExisted: true,
                WarningsOrErrors: warningsOrErrors
            );
        }

        long bytes = 0;
        int files = 0;
        int symlinks = 0;
        var failureHook = _testOverrideContext.Value?.SimulatedFileFailureHook;
        ScanDirectoryRecursiveSafe(rootDir, ref bytes, ref files, ref symlinks, warningsOrErrors, failureHook);

        return new DiskDryRunReport(
            categoryKey,
            targetDir,
            IsEstimateOnly: true,
            EstimatedBytes: bytes,
            CandidateFilesCount: files,
            SkippedSymlinksCount: symlinks,
            Success: true,
            DirectoryExisted: true,
            WarningsOrErrors: warningsOrErrors
        );
    }

    public static long ScanCategoryBytes(string categoryKey)
    {
        var dryRun = SimulateCategoryDryRun(categoryKey);
        return dryRun.EstimatedBytes;
    }

    private static void ScanDirectoryRecursiveSafe(
        DirectoryInfo dir,
        ref long bytes,
        ref int filesCount,
        ref int symlinksCount,
        List<string> warningsOrErrors,
        Func<string, Exception?>? failureHook)
    {
        try
        {
            var simulatedDirEx = failureHook?.Invoke(dir.FullName);
            if (simulatedDirEx != null)
            {
                throw simulatedDirEx;
            }

            foreach (var file in dir.EnumerateFiles())
            {
                try
                {
                    if (IsReparsePointOrSymlink(file))
                    {
                        symlinksCount++;
                        continue;
                    }

                    var simulatedFileEx = failureHook?.Invoke(file.FullName);
                    if (simulatedFileEx != null)
                    {
                        throw simulatedFileEx;
                    }

                    bytes += file.Length;
                    filesCount++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    warningsOrErrors.Add($"Acesso negado ao inspecionar arquivo '{file.FullName}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    warningsOrErrors.Add($"Não foi possível inspecionar arquivo '{file.FullName}': {ex.Message}");
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

                    ScanDirectoryRecursiveSafe(subDir, ref bytes, ref filesCount, ref symlinksCount, warningsOrErrors, failureHook);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    warningsOrErrors.Add($"Acesso negado ao inspecionar subpasta '{subDir.FullName}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    warningsOrErrors.Add($"Não foi possível inspecionar subpasta '{subDir.FullName}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
        {
            warningsOrErrors.Add($"Acesso negado ao listar diretório '{dir.FullName}': {ex.Message}");
        }
        catch (Exception ex)
        {
            warningsOrErrors.Add($"Erro ao ler diretório '{dir.FullName}': {ex.Message}");
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
            return new DiskPurgeReport(
                Success: false,
                PartialSuccess: false,
                CategoryKey: categoryKey,
                FreedBytes: 0,
                DeletedFilesCount: 0,
                SkippedLockedCount: 0,
                SkippedSymlinksCount: 0,
                AffectedItemsLog: affectedItems,
                Errors: errors,
                Status: DiskPurgeStatus.Failed,
                DirectoryExisted: false
            );
        }

        if (string.Equals(categoryKey, "update-cache", StringComparison.OrdinalIgnoreCase) && !confirmServiceStop)
        {
            errors.Add("A limpeza do cache do Windows Update exige confirmação explícita do usuário.");
            return new DiskPurgeReport(
                Success: false,
                PartialSuccess: false,
                CategoryKey: categoryKey,
                FreedBytes: 0,
                DeletedFilesCount: 0,
                SkippedLockedCount: 0,
                SkippedSymlinksCount: 0,
                AffectedItemsLog: affectedItems,
                Errors: errors,
                Status: DiskPurgeStatus.Failed,
                DirectoryExisted: Directory.Exists(targetDir)
            );
        }

        if (!Directory.Exists(targetDir))
        {
            affectedItems.Add($"[INFO] Diretório alvo '{targetDir}' não existe; nenhum arquivo elegível encontrado.");
            return new DiskPurgeReport(
                Success: false,
                PartialSuccess: false,
                CategoryKey: categoryKey,
                FreedBytes: 0,
                DeletedFilesCount: 0,
                SkippedLockedCount: 0,
                SkippedSymlinksCount: 0,
                AffectedItemsLog: affectedItems,
                Errors: errors,
                Status: DiskPurgeStatus.NothingToClean,
                DirectoryExisted: false
            );
        }

        var rootDir = new DirectoryInfo(targetDir);
        if (IsReparsePointOrSymlink(rootDir))
        {
            errors.Add("Operação abortada: o diretório alvo é um link simbólico ou junction NTFS.");
            return new DiskPurgeReport(
                Success: false,
                PartialSuccess: false,
                CategoryKey: categoryKey,
                FreedBytes: 0,
                DeletedFilesCount: 0,
                SkippedLockedCount: 0,
                SkippedSymlinksCount: 1,
                AffectedItemsLog: affectedItems,
                Errors: errors,
                Status: DiskPurgeStatus.Failed,
                DirectoryExisted: true
            );
        }

        long freedBytes = 0;
        int deletedFiles = 0;
        int skippedLocked = 0;
        int skippedSymlinks = 0;
        var failureHook = _testOverrideContext.Value?.SimulatedFileFailureHook;

        PurgeDirectoryRecursiveSafe(
            rootDir,
            Path.GetFullPath(targetDir),
            isRoot: true,
            ref freedBytes,
            ref deletedFiles,
            ref skippedLocked,
            ref skippedSymlinks,
            affectedItems,
            errors,
            failureHook
        );

        DiskPurgeStatus status;
        if (deletedFiles > 0)
        {
            bool hasSkippedOrErrors = skippedLocked > 0 || skippedSymlinks > 0 || errors.Count > 0;
            status = hasSkippedOrErrors ? DiskPurgeStatus.Partial : DiskPurgeStatus.Completed;
        }
        else
        {
            bool hasFailures = skippedLocked > 0 || errors.Count > 0;
            status = hasFailures ? DiskPurgeStatus.Failed : DiskPurgeStatus.NothingToClean;
        }

        bool isPartial = status == DiskPurgeStatus.Partial;
        bool isSuccess = status == DiskPurgeStatus.Completed || status == DiskPurgeStatus.Partial;

        return new DiskPurgeReport(
            Success: isSuccess,
            PartialSuccess: isPartial,
            CategoryKey: categoryKey,
            FreedBytes: freedBytes,
            DeletedFilesCount: deletedFiles,
            SkippedLockedCount: skippedLocked,
            SkippedSymlinksCount: skippedSymlinks,
            AffectedItemsLog: affectedItems.Take(100).ToList(),
            Errors: errors,
            Status: status,
            DirectoryExisted: true
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
        List<string> errors,
        Func<string, Exception?>? failureHook)
    {
        try
        {
            string currentCanonical = Path.GetFullPath(currentDir.FullName);
            if (!currentCanonical.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Contenção violada: o caminho '{currentCanonical}' escapa da raiz autorizada '{canonicalRoot}'.");
                return;
            }

            var simulatedDirEx = failureHook?.Invoke(currentDir.FullName);
            if (simulatedDirEx != null)
            {
                throw simulatedDirEx;
            }

            foreach (var file in currentDir.EnumerateFiles())
            {
                try
                {
                    if (IsReparsePointOrSymlink(file))
                    {
                        skippedSymlinks++;
                        if (affectedItems.Count < 100)
                        {
                            affectedItems.Add($"[SYMLINK PRESERVADO] {file.FullName}");
                        }
                        continue;
                    }

                    var simulatedFileEx = failureHook?.Invoke(file.FullName);
                    if (simulatedFileEx != null)
                    {
                        throw simulatedFileEx;
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
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    skippedLocked++;
                    errors.Add($"Acesso negado ao remover arquivo '{file.FullName}': {ex.Message}");
                    if (affectedItems.Count < 100)
                    {
                        affectedItems.Add($"[ACESSO NEGADO] {file.FullName}");
                    }
                }
                catch (IOException ex)
                {
                    skippedLocked++;
                    if (affectedItems.Count < 100)
                    {
                        affectedItems.Add($"[BLOQUEADO / EM USO] {file.FullName} ({ex.Message})");
                    }
                }
                catch (Exception ex)
                {
                    skippedLocked++;
                    errors.Add($"Erro ao remover arquivo '{file.FullName}': {ex.Message}");
                }
            }

            foreach (var subDir in currentDir.EnumerateDirectories())
            {
                try
                {
                    if (IsReparsePointOrSymlink(subDir))
                    {
                        skippedSymlinks++;
                        if (affectedItems.Count < 100)
                        {
                            affectedItems.Add($"[JUNCTION/SYMLINK PRESERVADO] {subDir.FullName}");
                        }
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
                        errors,
                        failureHook
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
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    skippedLocked++;
                    errors.Add($"Acesso negado ao processar subpasta '{subDir.FullName}': {ex.Message}");
                }
                catch (IOException)
                {
                    skippedLocked++;
                }
                catch (Exception ex)
                {
                    skippedLocked++;
                    errors.Add($"Erro ao processar subpasta '{subDir.FullName}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
        {
            skippedLocked++;
            errors.Add($"Acesso negado ao varrer diretório '{currentDir.FullName}': {ex.Message}");
        }
        catch (IOException ex)
        {
            skippedLocked++;
            errors.Add($"Falha de I/O ao varrer diretório '{currentDir.FullName}': {ex.Message}");
        }
        catch (Exception ex)
        {
            skippedLocked++;
            errors.Add($"Erro inesperado ao varrer diretório '{currentDir.FullName}': {ex.Message}");
        }
    }

    public static long PurgeCategory(string categoryKey)
    {
        var report = PurgeCategoryWithReport(categoryKey, confirmServiceStop: false);
        return report.FreedBytes;
    }
}