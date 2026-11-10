using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PCControlUltimate.Security;

namespace PCControlUltimate.Windows.Services;

public class RegistryValueEntry
{
    public string TweakId { get; set; } = string.Empty;
    public string Hive { get; set; } = "HKLM";
    public string SubKey { get; set; } = string.Empty;
    public string KeyPath
    {
        get => string.IsNullOrWhiteSpace(_keyPath) ? $"{Hive}\\{SubKey}".Trim('\\') : _keyPath;
        set
        {
            _keyPath = value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_keyPath))
            {
                Hive = RegistryOptimizer.ExtractExplicitHive(_keyPath) ?? Hive;
                SubKey = RegistryOptimizer.NormalizeSubKeyPath(_keyPath);
            }
        }
    }
    private string _keyPath = string.Empty;

    public string ValueName { get; set; } = string.Empty;
    public string ValueKind { get; set; } = "DWord";
    public string? RawValue { get; set; }
    public string? ValueData
    {
        get => RawValue;
        set => RawValue = value;
    }
    public bool WasInitiallyAbsent { get; set; }
    public bool KeyCreatedByApp { get; set; }

    public RegistryValueEntry() { }

    public RegistryValueEntry(
        string KeyPath,
        string ValueName,
        string ValueKind,
        string? ValueData,
        bool WasInitiallyAbsent = false,
        bool KeyCreatedByApp = false,
        string TweakId = "")
    {
        this.KeyPath = KeyPath;
        this.ValueName = ValueName;
        this.ValueKind = ValueKind;
        this.RawValue = ValueData;
        this.WasInitiallyAbsent = WasInitiallyAbsent;
        this.KeyCreatedByApp = KeyCreatedByApp;
        this.TweakId = string.IsNullOrWhiteSpace(TweakId)
            ? (RegistryOptimizer.FindAuthorizedCatalogEntry(KeyPath, ValueName)?.TweakId ?? string.Empty)
            : TweakId;
    }
}

public record RegistryReadFailure(
    string TweakId,
    string KeyPath,
    string ValueName,
    string Reason
);

public record RegistrySnapshotReadFailure(
    string TweakId,
    string Hive,
    string SubKeyPath,
    string ValueName,
    string Reason
) : RegistryReadFailure(TweakId, $"{Hive}\\{SubKeyPath}", ValueName, Reason);

public class RegistrySnapshotFile
{
    public string FormatVersion { get; set; } = RegistrySnapshotEngine.CurrentFormatVersion;
    public string SnapshotId { get; set; } = string.Empty;
    public string SnapshotType { get; set; } = "CatalogRegistrySnapshot";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string CreatedUtcIso
    {
        get => CreatedUtc.ToString("O");
        set
        {
            if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                CreatedUtc = dt.ToUniversalTime();
        }
    }
    public string Description { get; set; } = "Backup dos Ajustes Suportados pelo Aplicativo";
    public string ScopeNotice { get; set; } = "Backup dos Ajustes Suportados pelo Aplicativo (Checksum SHA-256 detecta corrupção de arquivo, não substitui assinatura digital).";
    public string IntegrityVerificationNotice => ScopeNotice;
    public int TotalKeysCaptured { get; set; }
    public int ReadFailuresCount { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsComplete => ReadFailuresCount == 0;
    public List<RegistryValueEntry> Entries { get; set; } = new();
    public List<RegistryReadFailure> UnreadableItems { get; set; } = new();
    public string ChecksumSha256 { get; set; } = string.Empty;
}

public record SnapshotSummaryItem(
    string SnapshotId,
    string FilePath,
    string CreatedUtcIso,
    string Description,
    int CapturedCount,
    int ReadFailuresCount,
    bool IsComplete,
    bool IntegrityVerified,
    string ChecksumSha256,
    IReadOnlyList<string> IncludedTweakIds,
    string SnapshotType = "CatalogRegistrySnapshot",
    long FileSizeBytes = 0
)
{
    public string SizeFormatted => FileSizeBytes >= 1024
        ? $"{(FileSizeBytes / 1024.0):F1} KB ({FileSizeBytes} B)"
        : $"{FileSizeBytes} B";
}

public record SnapshotItemRestoreReport(
    string TweakId,
    string KeyPath,
    string ValueName,
    bool Success,
    string? RestoredValue,
    string? ErrorMessage
);

public record SnapshotRestoreResult(
    bool Success,
    bool PartialSuccess,
    bool RolledBack,
    int RestoredCount,
    int FailedCount,
    string? ErrorMessage,
    IReadOnlyList<SnapshotItemRestoreReport>? ItemReports = null
)
{
    public IReadOnlyList<string> Errors =>
        !string.IsNullOrWhiteSpace(ErrorMessage)
            ? new[] { ErrorMessage! }
            : (ItemReports?.Where(r => !r.Success && !string.IsNullOrWhiteSpace(r.ErrorMessage)).Select(r => r.ErrorMessage!).ToList()
               ?? (IReadOnlyList<string>)Array.Empty<string>());
}


public static class RegistrySnapshotEngine
{
    public const string CurrentFormatVersion = "2.1";

    public static string BackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "Backups"
    );

    /// <summary>
    /// Computes a deterministic canonical string over the snapshot metadata and sorted entries
    /// so that the SHA-256 hash never depends on JSON whitespace or serializer indentation.
    /// </summary>
    public static string BuildCanonicalPayload(
        string snapshotId,
        string formatVersion,
        DateTime createdUtc,
        string description,
        IEnumerable<RegistryValueEntry> entries)
    {
        return BuildCanonicalPayload(
            formatVersion,
            snapshotId,
            createdUtc.ToUniversalTime().ToString("O"),
            description,
            entries
        );
    }

    public static string BuildCanonicalPayload(
        string formatVersion,
        string snapshotId,
        string createdUtcIso,
        string description,
        IEnumerable<RegistryValueEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("v=").Append(formatVersion).Append('|');
        sb.Append("id=").Append(snapshotId).Append('|');
        sb.Append("ts=").Append(createdUtcIso).Append('|');
        sb.Append("desc=").Append(description).Append('\n');

        var ordered = entries
            .OrderBy(e => e.KeyPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ValueName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ordered)
        {
            sb.Append(entry.KeyPath).Append('|')
              .Append(entry.ValueName).Append('|')
              .Append(entry.ValueKind).Append('|')
              .Append(entry.WasInitiallyAbsent ? "1" : "0").Append('|')
              .Append(entry.KeyCreatedByApp ? "1" : "0").Append('|')
              .Append(entry.RawValue ?? "<NULL>")
              .Append('\n');
        }

        return sb.ToString();
    }

    public static object CreateBackup(string description = "Backup dos Ajustes Suportados pelo Aplicativo")
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory);

            var capturedEntries = new List<RegistryValueEntry>();
            var readFailures = new List<RegistryReadFailure>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Capture the exact state of every tweak in the closed catalog
                foreach (var tweak in RegistryOptimizer.Catalog.Values)
                {
                    RegistryKey root = tweak.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                        ? Registry.CurrentUser
                        : Registry.LocalMachine;

                    CaptureSingleCatalogValue(root, tweak, capturedEntries, readFailures);
                }
            }
            else
            {
                foreach (var tweak in RegistryOptimizer.Catalog.Values)
                {
                    readFailures.Add(new RegistryReadFailure(
                        tweak.TweakId,
                        $"{tweak.Hive}\\{tweak.SubKeyPath}",
                        tweak.ValueName,
                        "Leitura do Registro requer sistema operacional Windows nativo."
                    ));
                }
            }

            string snapshotId = $"snap-{Guid.NewGuid():N}";
            DateTime createdUtc = DateTime.UtcNow;
            string createdIso = createdUtc.ToString("O");
            string timestamp = createdUtc.ToString("yyyyMMdd_HHmmss");
            string fileName = $"BackupAjustes_{timestamp}_{snapshotId}.json";
            string filePath = Path.Combine(BackupDirectory, fileName);

            var sortedEntries = capturedEntries
                .OrderBy(e => e.KeyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.ValueName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string canonicalPayload = BuildCanonicalPayload(
                CurrentFormatVersion,
                snapshotId,
                createdIso,
                description,
                sortedEntries
            );

            string checksum = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload);

            var snapshotFile = new RegistrySnapshotFile
            {
                FormatVersion = CurrentFormatVersion,
                SnapshotId = snapshotId,
                CreatedUtc = createdUtc,
                Description = description,
                TotalKeysCaptured = sortedEntries.Count,
                ReadFailuresCount = readFailures.Count,
                Entries = sortedEntries,
                UnreadableItems = readFailures,
                ChecksumSha256 = checksum
            };

            string finalJson = JsonSerializer.Serialize(snapshotFile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, finalJson, Encoding.UTF8);

            // Mandatory rule: Never declare backup verified before running post-write verification on disk!
            var (isVerifiedOnDisk, verifiedChecksum, verifyErr) = VerifyBackupIntegrity(filePath);
            var fileInfo = new FileInfo(filePath);

            return new
            {
                success = isVerifiedOnDisk && sortedEntries.Count > 0,
                isVerified = isVerifiedOnDisk,
                isComplete = readFailures.Count == 0 && sortedEntries.Count > 0,
                snapshotId,
                formatVersion = CurrentFormatVersion,
                createdUtc = createdIso,
                description,
                scopeNotice = snapshotFile.ScopeNotice,
                filePath,
                fileSizeBytes = fileInfo.Length,
                checksumSha256 = verifiedChecksum ?? checksum,
                capturedKeysCount = sortedEntries.Count,
                readFailuresCount = readFailures.Count,
                includedTweakIds = sortedEntries.Select(e => e.TweakId).Where(id => !string.IsNullOrEmpty(id)).ToList(),
                unreadableItems = readFailures,
                error = !isVerifiedOnDisk
                    ? (verifyErr ?? "A verificação pós-gravação do arquivo de backup falhou.")
                    : (sortedEntries.Count == 0 ? "Nenhum valor do Registro pôde ser lido neste ambiente." : null)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                isVerified = false,
                isComplete = false,
                error = $"Falha ao criar backup dos ajustes suportados: {ex.Message}"
            };
        }
    }

    private static void CaptureSingleCatalogValue(
        RegistryKey rootKey,
        TweakDefinition tweak,
        List<RegistryValueEntry> captured,
        List<RegistryReadFailure> failures)
    {
        string fullKey = $"{tweak.Hive}\\{tweak.SubKeyPath}";
        try
        {
            using var key = rootKey.OpenSubKey(tweak.SubKeyPath, false);
            if (key == null)
            {
                captured.Add(new RegistryValueEntry(
                    KeyPath: fullKey,
                    ValueName: tweak.ValueName,
                    ValueKind: tweak.ValueKind.ToString(),
                    ValueData: null,
                    WasInitiallyAbsent: true,
                    KeyCreatedByApp: false,
                    TweakId: tweak.TweakId
                ));
                return;
            }

            object? rawVal = key.GetValue(tweak.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawVal == null)
            {
                captured.Add(new RegistryValueEntry(
                    KeyPath: fullKey,
                    ValueName: tweak.ValueName,
                    ValueKind: tweak.ValueKind.ToString(),
                    ValueData: null,
                    WasInitiallyAbsent: true,
                    KeyCreatedByApp: false,
                    TweakId: tweak.TweakId
                ));
            }
            else
            {
                var kind = key.GetValueKind(tweak.ValueName);
                string? serialized = RegistryOptimizer.SerializeRegistryValue(rawVal, kind);
                captured.Add(new RegistryValueEntry(
                    KeyPath: fullKey,
                    ValueName: tweak.ValueName,
                    ValueKind: kind.ToString(),
                    ValueData: serialized,
                    WasInitiallyAbsent: false,
                    KeyCreatedByApp: false,
                    TweakId: tweak.TweakId
                ));
            }
        }
        catch (Exception ex)
        {
            failures.Add(new RegistryReadFailure(
                tweak.TweakId,
                fullKey,
                tweak.ValueName,
                ex.Message
            ));
        }
    }

    public static IReadOnlyList<SnapshotSummaryItem> ListBackups()
    {
        var items = new List<SnapshotSummaryItem>();
        try
        {
            if (!Directory.Exists(BackupDirectory))
                return items;

            foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json").OrderByDescending(f => f))
            {
                try
                {
                    string json = File.ReadAllText(file, Encoding.UTF8);
                    var manifest = JsonSerializer.Deserialize<RegistrySnapshotFile>(json);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.SnapshotId))
                        continue;

                    var (isValid, _, _) = VerifyBackupIntegrity(file);
                    var fInfo = new FileInfo(file);
                    var tweakIds = manifest.Entries
                        .Select(e => !string.IsNullOrWhiteSpace(e.TweakId)
                            ? e.TweakId
                            : (RegistryOptimizer.FindAuthorizedCatalogEntry(e.KeyPath, e.ValueName)?.TweakId ?? e.ValueName))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    items.Add(new SnapshotSummaryItem(
                        SnapshotId: manifest.SnapshotId,
                        FilePath: file,
                        CreatedUtcIso: manifest.CreatedUtcIso,
                        Description: manifest.Description,
                        CapturedCount: manifest.Entries.Count,
                        ReadFailuresCount: manifest.ReadFailuresCount,
                        IsComplete: manifest.ReadFailuresCount == 0 && manifest.Entries.Count > 0,
                        IntegrityVerified: isValid,
                        ChecksumSha256: manifest.ChecksumSha256,
                        IncludedTweakIds: tweakIds,
                        SnapshotType: string.IsNullOrWhiteSpace(manifest.SnapshotType) ? "CatalogRegistrySnapshot" : manifest.SnapshotType,
                        FileSizeBytes: fInfo.Exists ? fInfo.Length : 0
                    ));
                }
                catch { }
            }
        }
        catch { }

        return items;
    }

    public static RegistrySnapshotFile? InspectBackup(string filePathOrId)
    {
        string? resolved = ResolveBackupFilePath(filePathOrId);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            return null;

        try
        {
            string json = File.ReadAllText(resolved, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotFile>(json);
            if (manifest != null)
            {
                var fInfo = new FileInfo(resolved);
                manifest.FileSizeBytes = fInfo.Exists ? fInfo.Length : 0;
            }
            return manifest;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveBackupFilePath(string filePathOrId)
    {
        if (string.IsNullOrWhiteSpace(filePathOrId)) return null;
        if (File.Exists(filePathOrId)) return filePathOrId;

        try
        {
            if (!Directory.Exists(BackupDirectory)) return null;
            foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json"))
            {
                if (Path.GetFileName(file).Contains(filePathOrId, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch { }

        return null;
    }

    public static (bool isValid, string? checksum, string? error) VerifyBackupIntegrity(string filePath)
    {
        try
        {
            string? resolved = ResolveBackupFilePath(filePath) ?? filePath;
            if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
                return (false, null, "Arquivo de backup não encontrado no disco.");

            string json = File.ReadAllText(resolved, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotFile>(json);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.SnapshotId) || manifest.Entries == null)
                return (false, null, "Estrutura JSON do arquivo de backup está corrompida ou inválida.");

            // Validate every entry path against the strict whitelist before trusting the backup
            foreach (var entry in manifest.Entries)
            {
                if (!RegistryOptimizer.IsPathAllowed(entry.KeyPath))
                {
                    return (false, manifest.ChecksumSha256,
                        $"Subchave fora da whitelist detectada no arquivo de backup: {entry.KeyPath}");
                }
            }

            string canonicalPayload = BuildCanonicalPayload(
                manifest.FormatVersion ?? CurrentFormatVersion,
                manifest.SnapshotId,
                manifest.CreatedUtcIso,
                manifest.Description ?? string.Empty,
                manifest.Entries
            );

            string calculated = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload);
            bool matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(calculated),
                Encoding.UTF8.GetBytes((manifest.ChecksumSha256 ?? string.Empty).ToLowerInvariant())
            );

            return (matches, manifest.ChecksumSha256, matches ? null : "Checksum mismatch: o conteúdo do arquivo foi alterado ou corrompido.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Restores all entries or a specific TweakId from a verified snapshot,
    /// with pre-restore state capture and automatic rollback if a critical inconsistency occurs.
    /// </summary>
    public static SnapshotRestoreResult RestoreFromBackupWithReport(
        string filePathOrId,
        bool confirmIncompleteBackup = false,
        string? singleTweakId = null,
        bool rollbackOnPartialFailure = false,
        bool? allowIncompleteBackup = null)
    {
        bool allowIncomplete = allowIncompleteBackup ?? confirmIncompleteBackup;
        try
        {
            string? resolvedPath = ResolveBackupFilePath(filePathOrId) ?? filePathOrId;
            var (isValid, _, error) = VerifyBackupIntegrity(resolvedPath);
            if (!isValid)
            {
                return new SnapshotRestoreResult(false, false, false, 0, 0,
                    $"Verificação de integridade ou whitelist falhou: {error}");
            }

            string json = File.ReadAllText(resolvedPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotFile>(json);
            if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
            {
                return new SnapshotRestoreResult(false, false, false, 0, 0, "Nenhuma entrada encontrada no arquivo de backup.");
            }

            if (manifest.ReadFailuresCount > 0 && !allowIncomplete)
            {
                return new SnapshotRestoreResult(
                    false, false, false, 0, 0,
                    $"Este backup é incompleto ({manifest.ReadFailuresCount} falhas de leitura na captura). Confirmação explícita é necessária antes de restaurar."
                );
            }

            var targetEntries = manifest.Entries;
            if (!string.IsNullOrWhiteSpace(singleTweakId))
            {
                targetEntries = manifest.Entries
                    .Where(e =>
                        string.Equals(e.TweakId, singleTweakId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(RegistryOptimizer.FindAuthorizedCatalogEntry(e.KeyPath, e.ValueName)?.TweakId, singleTweakId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetEntries.Count == 0)
                {
                    return new SnapshotRestoreResult(false, false, false, 0, 0,
                        $"Ajuste '{singleTweakId}' não foi encontrado neste arquivo de backup.");
                }
            }

            // Capture current state before restoration to enable transactional rollback
            var preRestoreState = CaptureCurrentStateForRollback(targetEntries);
            var appliedBeforeFailure = new List<RegistryValueEntry>();
            var itemReports = new List<SnapshotItemRestoreReport>();

            int restored = 0;
            int failed = 0;

            foreach (var entry in targetEntries)
            {
                // Verify that the entry's TweakId (if specified) matches the catalog definition for its KeyPath + ValueName
                var defByPath = RegistryOptimizer.FindAuthorizedCatalogEntry(entry.KeyPath, entry.ValueName);
                var defById = !string.IsNullOrWhiteSpace(entry.TweakId) && RegistryOptimizer.Catalog.TryGetValue(entry.TweakId, out var d)
                    ? d
                    : null;

                if (defByPath == null || (defById != null && !string.Equals(defByPath.TweakId, defById.TweakId, StringComparison.OrdinalIgnoreCase)))
                {
                    failed++;
                    itemReports.Add(new SnapshotItemRestoreReport(
                        entry.TweakId,
                        entry.KeyPath,
                        entry.ValueName,
                        false,
                        null,
                        "Item do snapshot não corresponde ao registro autorizado no catálogo fechado."
                    ));
                    continue;
                }

                var def = defByPath;
                var res = RegistryOptimizer.RestoreFromVerifiedSnapshotEntry(def, entry);

                if (res.Success)
                {
                    restored++;
                    if (preRestoreState.TryGetValue(def.TweakId, out var prevEntry))
                    {
                        appliedBeforeFailure.Add(prevEntry);
                    }
                    itemReports.Add(new SnapshotItemRestoreReport(
                        def.TweakId,
                        entry.KeyPath,
                        entry.ValueName,
                        true,
                        res.VerifiedValue,
                        null
                    ));
                }
                else
                {
                    failed++;
                    itemReports.Add(new SnapshotItemRestoreReport(
                        def.TweakId,
                        entry.KeyPath,
                        entry.ValueName,
                        false,
                        null,
                        res.ErrorMessage
                    ));
                }
            }

            // Execute rollback if requested and a partial failure occurred
            bool rolledBack = false;
            if (failed > 0 && restored > 0 && rollbackOnPartialFailure)
            {
                foreach (var rollbackEntry in appliedBeforeFailure)
                {
                    if (RegistryOptimizer.Catalog.TryGetValue(rollbackEntry.TweakId, out var rDef))
                    {
                        RegistryOptimizer.RestoreFromVerifiedSnapshotEntry(rDef, rollbackEntry);
                    }
                }
                rolledBack = true;
                return new SnapshotRestoreResult(
                    Success: false,
                    PartialSuccess: false,
                    RolledBack: true,
                    RestoredCount: 0,
                    FailedCount: failed,
                    ErrorMessage: $"Restauração abortada com {failed} falha(s). Rollback transacional executado para manter consistência.",
                    ItemReports: itemReports
                );
            }

            bool fullSuccess = restored > 0 && failed == 0;
            bool partialSuccess = restored > 0 && failed > 0;

            return new SnapshotRestoreResult(
                Success: fullSuccess,
                PartialSuccess: partialSuccess,
                RolledBack: rolledBack,
                RestoredCount: restored,
                FailedCount: failed,
                ErrorMessage: failed > 0 ? $"Restauração parcial: {restored} restaurado(s), {failed} falha(s)." : null,
                ItemReports: itemReports
            );
        }
        catch (Exception ex)
        {
            return new SnapshotRestoreResult(false, false, false, 0, 0, ex.Message);
        }
    }

    private static Dictionary<string, RegistryValueEntry> CaptureCurrentStateForRollback(IEnumerable<RegistryValueEntry> targetEntries)
    {
        var map = new Dictionary<string, RegistryValueEntry>(StringComparer.OrdinalIgnoreCase);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return map;

        foreach (var entry in targetEntries)
        {
            var def = !string.IsNullOrWhiteSpace(entry.TweakId) && RegistryOptimizer.Catalog.TryGetValue(entry.TweakId, out var d)
                ? d
                : RegistryOptimizer.FindAuthorizedCatalogEntry(entry.KeyPath, entry.ValueName);

            if (def == null || map.ContainsKey(def.TweakId)) continue;

            var captured = new List<RegistryValueEntry>();
            var failures = new List<RegistryReadFailure>();
            RegistryKey root = def.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            CaptureSingleCatalogValue(root, def, captured, failures);
            if (captured.Count > 0)
            {
                map[def.TweakId] = captured[0];
            }
        }

        return map;
    }

    public static (bool success, int restoredCount, int failedCount, string? error) RestoreFromBackup(string filePath)
    {
        var res = RestoreFromBackupWithReport(filePath);
        return (res.Success, res.RestoredCount, res.FailedCount, res.ErrorMessage);
    }
}