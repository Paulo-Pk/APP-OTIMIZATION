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

public record RegistryValueEntry(
    string KeyPath,
    string ValueName,
    string ValueKind,
    string? ValueData,
    bool WasInitiallyAbsent = false,
    bool KeyCreatedByApp = false
);

public record RegistrySnapshotManifest(
    string FormatVersion,
    string SnapshotId,
    string CreatedUtcIso,
    string Description,
    int TotalKeysCaptured,
    List<RegistryValueEntry> Entries,
    string ChecksumSha256
);

public record SnapshotRestoreResult(
    bool Success,
    bool PartialSuccess,
    int RestoredCount,
    int FailedCount,
    string? ErrorMessage
);

public static class RegistrySnapshotEngine
{
    public const string CurrentFormatVersion = "2.0";

    private static readonly string BackupDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PCControlUltimate",
        "Backups"
    );

    /// <summary>
    /// Computes a deterministic canonical string over the snapshot metadata and sorted entries
    /// so that the SHA-256 hash never depends on JSON whitespace or serializer indentation.
    /// </summary>
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
              .Append(entry.ValueData ?? "<NULL>")
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
            var seenPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 1. Capture exact state of all cataloged tweaks (including absent values)
                foreach (var tweak in RegistryOptimizer.Catalog.Values)
                {
                    RegistryKey root = tweak.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                        ? Registry.CurrentUser
                        : Registry.LocalMachine;

                    string fullKey = $"{tweak.Hive}\\{tweak.SubKeyPath}";
                    CaptureSingleCatalogValue(root, tweak.Hive, tweak.SubKeyPath, tweak.ValueName, tweak.ValueKind, capturedEntries);
                    seenPairs.Add($"{fullKey}::{tweak.ValueName}");
                }

                // 2. Capture existing values in all whitelisted subkeys
                foreach (var subKeyPath in RegistryOptimizer.AllowedSubkeys)
                {
                    CaptureKeyValues(Registry.LocalMachine, "HKLM", subKeyPath, capturedEntries, seenPairs);
                    CaptureKeyValues(Registry.CurrentUser, "HKCU", subKeyPath, capturedEntries, seenPairs);
                }
            }

            string snapshotId = $"snap-{Guid.NewGuid():N}";
            string createdIso = DateTime.UtcNow.ToString("O");
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
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

            var finalManifest = new RegistrySnapshotManifest(
                FormatVersion: CurrentFormatVersion,
                SnapshotId: snapshotId,
                CreatedUtcIso: createdIso,
                Description: description,
                TotalKeysCaptured: sortedEntries.Count,
                Entries: sortedEntries,
                ChecksumSha256: checksum
            );

            string finalJson = JsonSerializer.Serialize(finalManifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, finalJson, Encoding.UTF8);

            var fileInfo = new FileInfo(filePath);

            return new
            {
                success = true,
                snapshotId,
                formatVersion = CurrentFormatVersion,
                description,
                filePath,
                fileSizeBytes = fileInfo.Length,
                checksumSha256 = checksum,
                capturedKeysCount = sortedEntries.Count
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                error = $"Falha ao criar backup dos ajustes suportados: {ex.Message}"
            };
        }
    }

    private static void CaptureSingleCatalogValue(
        RegistryKey rootKey,
        string hivePrefix,
        string subKeyPath,
        string valueName,
        RegistryValueKind defaultKind,
        List<RegistryValueEntry> list)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath, false);
            if (key == null)
            {
                list.Add(new RegistryValueEntry(
                    KeyPath: $"{hivePrefix}\\{subKeyPath}",
                    ValueName: valueName,
                    ValueKind: defaultKind.ToString(),
                    ValueData: null,
                    WasInitiallyAbsent: true,
                    KeyCreatedByApp: false
                ));
                return;
            }

            object? rawVal = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawVal == null)
            {
                list.Add(new RegistryValueEntry(
                    KeyPath: $"{hivePrefix}\\{subKeyPath}",
                    ValueName: valueName,
                    ValueKind: defaultKind.ToString(),
                    ValueData: null,
                    WasInitiallyAbsent: true,
                    KeyCreatedByApp: false
                ));
            }
            else
            {
                var kind = key.GetValueKind(valueName);
                string? serialized = RegistryOptimizer.SerializeRegistryValue(rawVal, kind);
                list.Add(new RegistryValueEntry(
                    KeyPath: $"{hivePrefix}\\{subKeyPath}",
                    ValueName: valueName,
                    ValueKind: kind.ToString(),
                    ValueData: serialized,
                    WasInitiallyAbsent: false,
                    KeyCreatedByApp: false
                ));
            }
        }
        catch
        {
            // Ignore unreadable keys
        }
    }

    private static void CaptureKeyValues(
        RegistryKey rootKey,
        string hivePrefix,
        string subKeyPath,
        List<RegistryValueEntry> list,
        HashSet<string> seenPairs)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath, false);
            if (key == null) return;

            string fullKey = $"{hivePrefix}\\{subKeyPath}";
            foreach (var valueName in key.GetValueNames())
            {
                string pairId = $"{fullKey}::{valueName}";
                if (seenPairs.Contains(pairId)) continue;

                var kind = key.GetValueKind(valueName);
                var rawVal = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string? dataStr = RegistryOptimizer.SerializeRegistryValue(rawVal, kind);

                list.Add(new RegistryValueEntry(
                    KeyPath: fullKey,
                    ValueName: valueName,
                    ValueKind: kind.ToString(),
                    ValueData: dataStr,
                    WasInitiallyAbsent: false,
                    KeyCreatedByApp: false
                ));
                seenPairs.Add(pairId);
            }
        }
        catch
        {
            // Ignore subkeys that do not exist in the current hive
        }
    }

    public static (bool isValid, string? checksum, string? error) VerifyBackupIntegrity(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (false, null, "File does not exist");

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotManifest>(json);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.SnapshotId) || manifest.Entries == null)
                return (false, null, "Corrupt or invalid JSON structure");

            // Validate all entry paths against the strict whitelist before trusting the backup
            foreach (var entry in manifest.Entries)
            {
                if (!RegistryOptimizer.IsPathAllowed(entry.KeyPath))
                {
                    return (false, manifest.ChecksumSha256,
                        $"Rejected unauthorized Registry path in backup: {entry.KeyPath}");
                }
            }

            string canonicalPayload = BuildCanonicalPayload(
                manifest.FormatVersion ?? CurrentFormatVersion,
                manifest.SnapshotId,
                manifest.CreatedUtcIso ?? string.Empty,
                manifest.Description ?? string.Empty,
                manifest.Entries
            );

            string calculated = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload);
            bool matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(calculated),
                Encoding.UTF8.GetBytes((manifest.ChecksumSha256 ?? string.Empty).ToLowerInvariant())
            );

            return (matches, manifest.ChecksumSha256, matches ? null : "Checksum mismatch");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public static SnapshotRestoreResult RestoreFromBackupWithReport(string filePath)
    {
        try
        {
            var (isValid, _, error) = VerifyBackupIntegrity(filePath);
            if (!isValid)
            {
                return new SnapshotRestoreResult(false, false, 0, 0,
                    $"Backup integrity or whitelist check failed: {error}");
            }

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotManifest>(json);
            if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
            {
                return new SnapshotRestoreResult(false, false, 0, 0, "No entries found in backup file.");
            }

            int restored = 0;
            int failed = 0;

            foreach (var entry in manifest.Entries)
            {
                if (!RegistryOptimizer.IsPathAllowed(entry.KeyPath))
                {
                    failed++;
                    continue;
                }

                var res = RegistryOptimizer.TryRestoreValue(
                    entry.KeyPath,
                    entry.ValueName,
                    entry.ValueData,
                    entry.ValueKind,
                    entry.WasInitiallyAbsent,
                    entry.KeyCreatedByApp
                );

                if (res.Success)
                    restored++;
                else
                    failed++;
            }

            bool fullSuccess = restored > 0 && failed == 0;
            bool partialSuccess = restored > 0 && failed > 0;

            return new SnapshotRestoreResult(
                Success: fullSuccess,
                PartialSuccess: partialSuccess,
                RestoredCount: restored,
                FailedCount: failed,
                ErrorMessage: failed > 0 ? $"{failed} entradas falharam durante a restauração." : null
            );
        }
        catch (Exception ex)
        {
            return new SnapshotRestoreResult(false, false, 0, 0, ex.Message);
        }
    }

    public static (bool success, int restoredCount, int failedCount, string? error) RestoreFromBackup(string filePath)
    {
        var res = RestoreFromBackupWithReport(filePath);
        return (res.Success, res.RestoredCount, res.FailedCount, res.ErrorMessage);
    }
}