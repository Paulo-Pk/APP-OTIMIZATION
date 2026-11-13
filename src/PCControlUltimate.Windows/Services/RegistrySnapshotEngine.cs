using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
)
{
    public bool RestoredAndVerified => Success;
    public string Message => Success
        ? $"Restaurado e verificado ({RestoredValue ?? "REMOVED_ABSENT_VALUE"})"
        : (ErrorMessage ?? "Falha na restauração");
}

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


[SupportedOSPlatform("windows")]
public static class RegistrySnapshotEngine
{
    public const string CurrentFormatVersion = "2.2";

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
        IEnumerable<RegistryValueEntry> entries,
        int readFailuresCount = 0,
        int totalKeysCaptured = 0,
        IEnumerable<RegistryReadFailure>? unreadableItems = null,
        string snapshotType = "CatalogRegistrySnapshot")
    {
        return BuildCanonicalPayload(
            formatVersion,
            snapshotId,
            createdUtc.ToUniversalTime().ToString("O"),
            description,
            entries,
            readFailuresCount,
            totalKeysCaptured,
            unreadableItems,
            snapshotType
        );
    }

    public static string BuildCanonicalPayload(
        string formatVersion,
        string snapshotId,
        string createdUtcIso,
        string description,
        IEnumerable<RegistryValueEntry> entries,
        int readFailuresCount = 0,
        int totalKeysCaptured = 0,
        IEnumerable<RegistryReadFailure>? unreadableItems = null,
        string snapshotType = "CatalogRegistrySnapshot")
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

        // Version 2.2 binds completeness metadata to the checksum. Keep the legacy
        // canonical payload unchanged for 2.0/2.1 so existing backups remain verifiable.
        if (string.Equals(formatVersion, "2.2", StringComparison.Ordinal))
        {
            static string Encode(string? value) =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

            sb.Append("snapshotType=").Append(Encode(snapshotType)).Append('\n');
            sb.Append("totalKeysCaptured=").Append(totalKeysCaptured).Append('\n');
            sb.Append("readFailuresCount=").Append(readFailuresCount).Append('\n');

            var failures = (unreadableItems ?? Array.Empty<RegistryReadFailure>())
                .OrderBy(item => item.TweakId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TweakId, StringComparer.Ordinal)
                .ThenBy(item => item.KeyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.KeyPath, StringComparer.Ordinal)
                .ThenBy(item => item.ValueName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ValueName, StringComparer.Ordinal)
                .ThenBy(item => item.Reason, StringComparer.Ordinal);
            foreach (var failure in failures)
            {
                sb.Append("unreadable=")
                  .Append(Encode(failure.TweakId)).Append('|')
                  .Append(Encode(failure.KeyPath)).Append('|')
                  .Append(Encode(failure.ValueName)).Append('|')
                  .Append(Encode(failure.Reason)).Append('\n');
            }
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

            if (OperatingSystem.IsWindows())
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
                sortedEntries,
                readFailures.Count,
                sortedEntries.Count,
                readFailures,
                "CatalogRegistrySnapshot"
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

        // Preserve explicit file-path support, but never resolve an identifier by
        // substring: two SnapshotIds can share a prefix and select the wrong backup.
        if (File.Exists(filePathOrId)) return filePathOrId;

        try
        {
            if (!Directory.Exists(BackupDirectory)) return null;

            string requested = filePathOrId.Trim();
            string? match = null;

            foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json"))
            {
                string fileName = Path.GetFileName(file);
                bool exactFileName = string.Equals(fileName, requested, StringComparison.OrdinalIgnoreCase);
                bool exactSnapshotId = false;

                if (!exactFileName)
                {
                    try
                    {
                        using var stream = File.OpenRead(file);
                        var manifest = JsonSerializer.Deserialize<RegistrySnapshotFile>(stream);
                        exactSnapshotId = manifest != null &&
                            string.Equals(manifest.SnapshotId, requested, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        // Ignore malformed unrelated backup files during identifier lookup.
                    }
                }

                if (!exactFileName && !exactSnapshotId) continue;

                // Duplicate identifiers are ambiguous; fail closed instead of choosing
                // whichever file happens to be returned first by the filesystem.
                if (match != null) return null;
                match = file;
            }

            return match;
        }
        catch
        {
            return null;
        }
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

            // Only accept snapshot schemas whose semantics are understood by this
            // restore engine. Missing fields retain their model defaults for legacy files;
            // explicitly unknown or null values fail closed.
            if (manifest.FormatVersion is not ("2.0" or "2.1" or CurrentFormatVersion))
                return (false, manifest.ChecksumSha256,
                    $"Versão de backup não suportada: {manifest.FormatVersion ?? "(nula)"}.");
            if (!string.Equals(manifest.SnapshotType, "CatalogRegistrySnapshot", StringComparison.Ordinal))
                return (false, manifest.ChecksumSha256,
                    $"Tipo de backup não suportado: {manifest.SnapshotType ?? "(nulo)"}.");

            // Validate every entry path against the strict whitelist and reject duplicate
            // targets before any restore can write the same Registry value more than once.
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Entries)
            {
                if (!RegistryOptimizer.IsPathAllowed(entry.KeyPath))
                {
                    return (false, manifest.ChecksumSha256,
                        $"Subchave fora da whitelist detectada no arquivo de backup: {entry.KeyPath}");
                }

                string targetIdentity = $"{RegistryOptimizer.ExtractExplicitHive(entry.KeyPath)?.ToUpperInvariant()}|{RegistryOptimizer.NormalizeSubKeyPath(entry.KeyPath)}|{entry.ValueName}";
                if (!seenTargets.Add(targetIdentity))
                {
                    return (false, manifest.ChecksumSha256,
                        $"Entrada duplicada detectada no arquivo de backup: {entry.KeyPath}\\{entry.ValueName}");
                }
            }

            string canonicalPayload = BuildCanonicalPayload(
                manifest.FormatVersion ?? CurrentFormatVersion,
                manifest.SnapshotId,
                manifest.CreatedUtcIso,
                manifest.Description ?? string.Empty,
                manifest.Entries,
                manifest.ReadFailuresCount,
                manifest.TotalKeysCaptured,
                manifest.UnreadableItems,
                manifest.SnapshotType
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

            bool legacyCompletenessMetadata = !string.Equals(manifest.FormatVersion, CurrentFormatVersion, StringComparison.Ordinal);
            if ((manifest.ReadFailuresCount > 0 || legacyCompletenessMetadata) && !allowIncomplete)
            {
                string reason = legacyCompletenessMetadata
                    ? $"Este backup usa o formato legado {manifest.FormatVersion}, que não vincula os metadados de completude ao checksum."
                    : $"Este backup é incompleto ({manifest.ReadFailuresCount} falhas de leitura na captura).";
                return new SnapshotRestoreResult(
                    false, false, false, 0, 0,
                    $"{reason} Confirmação explícita é necessária antes de restaurar."
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

            // Transactional rollback is only safe when every authorized target has a verified
            // pre-restore state captured before the first Registry write.
            var preRestoreState = new Dictionary<string, RegistryValueEntry>(StringComparer.OrdinalIgnoreCase);
            if (rollbackOnPartialFailure)
            {
                if (!OperatingSystem.IsWindows())
                {
                    return new SnapshotRestoreResult(false, false, false, 0, 0,
                        "Rollback transacional exige Windows nativo; nenhuma alteração foi aplicada.");
                }

                preRestoreState = CaptureCurrentStateForRollback(targetEntries, out var captureFailures);
                var expectedRollbackIds = targetEntries
                    .Select(entry =>
                    {
                        var byPath = RegistryOptimizer.FindAuthorizedCatalogEntry(entry.KeyPath, entry.ValueName);
                        var byId = !string.IsNullOrWhiteSpace(entry.TweakId) && RegistryOptimizer.Catalog.TryGetValue(entry.TweakId, out var catalogDef)
                            ? catalogDef
                            : null;
                        return byPath != null && (byId == null || string.Equals(byPath.TweakId, byId.TweakId, StringComparison.OrdinalIgnoreCase))
                            ? byPath.TweakId
                            : null;
                    })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var missingRollbackStates = expectedRollbackIds
                    .Where(id => !preRestoreState.ContainsKey(id!))
                    .ToList();
                if (captureFailures.Count > 0 || missingRollbackStates.Count > 0)
                {
                    string details = string.Join("; ", captureFailures.Select(f => $"{f.TweakId}: {f.Reason}"));
                    if (missingRollbackStates.Count > 0)
                        details = string.Join("; ", new[] { details, $"Estado anterior não capturado: {string.Join(", ", missingRollbackStates)}" }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    return new SnapshotRestoreResult(false, false, false, 0, 0,
                        $"Rollback transacional cancelado antes de qualquer alteração: não foi possível capturar e validar todos os estados anteriores. {details}");
                }
            }

            // Track every authorized restore attempt, not only successful read-backs.
            // A Registry write can take effect even when its subsequent verification fails;
            // that item must still be considered by the rollback pass.
            var attemptedBeforeFailure = new List<(RegistryValueEntry PreviousState, RegistryValueEntry AppliedSnapshotState)>();
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
                if (preRestoreState.TryGetValue(def.TweakId, out var prevEntry))
                {
                    attemptedBeforeFailure.Add((prevEntry, entry));
                }

                RegistryOperationResult res;
                try
                {
                    res = RegistryOptimizer.RestoreFromVerifiedSnapshotEntry(def, entry);
                }
                catch (Exception ex)
                {
                    // Keep the transaction alive so previously or partially changed values
                    // still reach the rollback pass instead of escaping to the outer catch.
                    res = new RegistryOperationResult(
                        false, null, null, entry.ValueKind,
                        entry.WasInitiallyAbsent, entry.KeyCreatedByApp,
                        $"Falha inesperada durante a restauração: {ex.Message}",
                        def.TweakId,
                        ErrorCode: "SNAPSHOT_RESTORE_EXCEPTION");
                }

                if (res.Success)
                {
                    restored++;
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

            // Execute and verify every rollback operation. Never report rollback success merely
            // because rollback was attempted.
            bool rolledBack = false;
            if (failed > 0 && attemptedBeforeFailure.Count > 0 && rollbackOnPartialFailure)
            {
                var rollbackFailures = new List<SnapshotItemRestoreReport>();
                foreach (var rollbackPair in attemptedBeforeFailure)
                {
                    var rollbackEntry = rollbackPair.PreviousState;
                    var appliedSnapshotEntry = rollbackPair.AppliedSnapshotState;
                    if (!RegistryOptimizer.Catalog.TryGetValue(rollbackEntry.TweakId, out var rDef))
                    {
                        rollbackFailures.Add(new SnapshotItemRestoreReport(
                            rollbackEntry.TweakId, rollbackEntry.KeyPath, rollbackEntry.ValueName, false, null,
                            "Rollback falhou: ajuste não encontrado no catálogo fechado."));
                        continue;
                    }

                    // Do not overwrite a Registry value changed by another process after the
                    // snapshot restore; only roll back if the snapshot value is still present.
                    RegistryOperationResult rollbackResult;
                    try
                    {
                        rollbackResult = RegistryOptimizer.RestoreFromCapturedRollbackState(
                            rDef,
                            rollbackEntry,
                            appliedSnapshotEntry);
                    }
                    catch (Exception ex)
                    {
                        rollbackResult = new RegistryOperationResult(
                            false, null, null, rollbackEntry.ValueKind,
                            rollbackEntry.WasInitiallyAbsent, rollbackEntry.KeyCreatedByApp,
                            $"Exceção inesperada durante o rollback: {ex.Message}",
                            rollbackEntry.TweakId,
                            ErrorCode: "SNAPSHOT_ROLLBACK_EXCEPTION");
                    }

                    if (!rollbackResult.Success)
                    {
                        rollbackFailures.Add(new SnapshotItemRestoreReport(
                            rollbackEntry.TweakId, rollbackEntry.KeyPath, rollbackEntry.ValueName, false, null,
                            $"Rollback falhou: {rollbackResult.ErrorMessage ?? "restauração do estado anterior não confirmada."}"));
                    }
                }

                rolledBack = rollbackFailures.Count == 0;
                var rollbackReports = itemReports
                    .Select(report => report.Success
                        ? report with { Success = false, ErrorMessage = "A restauração do snapshot foi revertida para o estado anterior." }
                        : report)
                    .Concat(rollbackFailures)
                    .ToList();

                return new SnapshotRestoreResult(
                    Success: false,
                    PartialSuccess: !rolledBack,
                    RolledBack: rolledBack,
                    RestoredCount: rollbackFailures.Count,
                    FailedCount: failed + rollbackFailures.Count,
                    ErrorMessage: rolledBack
                        ? $"Restauração abortada após {failed} falha(s). Todos os {attemptedBeforeFailure.Count} item(ns) tentados foram verificados e retornaram ao estado anterior quando necessário."
                        : $"Restauração abortada após {failed} falha(s). O rollback também falhou em {rollbackFailures.Count} item(ns); verifique os itens e erros individualmente.",
                    ItemReports: rollbackReports
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

    private static Dictionary<string, RegistryValueEntry> CaptureCurrentStateForRollback(
        IEnumerable<RegistryValueEntry> targetEntries,
        out List<RegistryReadFailure> captureFailures)
    {
        var map = new Dictionary<string, RegistryValueEntry>(StringComparer.OrdinalIgnoreCase);
        captureFailures = new List<RegistryReadFailure>();

        if (!OperatingSystem.IsWindows())
            return map;

        foreach (var entry in targetEntries)
        {
            // Resolve by the actual Registry path first; a snapshot-supplied TweakId is never
            // allowed to redirect rollback capture to a different catalog entry.
            var def = RegistryOptimizer.FindAuthorizedCatalogEntry(entry.KeyPath, entry.ValueName);
            var defById = !string.IsNullOrWhiteSpace(entry.TweakId) && RegistryOptimizer.Catalog.TryGetValue(entry.TweakId, out var d)
                ? d
                : null;
            if (def == null ||
                (defById != null && !string.Equals(def.TweakId, defById.TweakId, StringComparison.OrdinalIgnoreCase)) ||
                map.ContainsKey(def.TweakId))
                continue;

            var captured = new List<RegistryValueEntry>();
            var failures = new List<RegistryReadFailure>();
            RegistryKey root = def.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            CaptureSingleCatalogValue(root, def, captured, failures);
            if (captured.Count > 0)
                map[def.TweakId] = captured[0];
            if (failures.Count > 0)
                captureFailures.AddRange(failures);
            else if (captured.Count == 0)
                captureFailures.Add(new RegistryReadFailure(
                    def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                    "A captura do estado anterior não retornou valor nem ausência confirmada."));
        }

        return map;
    }

    public static (bool success, int restoredCount, int failedCount, string? error) RestoreFromBackup(string filePath)
    {
        var res = RestoreFromBackupWithReport(filePath);
        return (res.Success, res.RestoredCount, res.FailedCount, res.ErrorMessage);
    }
}