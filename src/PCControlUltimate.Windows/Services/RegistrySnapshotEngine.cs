using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PCControlUltimate.Windows.Services;

public record RegistryValueEntry(
    string KeyPath,
    string ValueName,
    string ValueKind,
    string? ValueData
);

public record RegistrySnapshotManifest(
    string SnapshotId,
    DateTime CreatedUtc,
    string Description,
    int TotalKeysCaptured,
    List<RegistryValueEntry> Entries,
    string ChecksumSha256
);

public static class RegistrySnapshotEngine
{
    private static readonly string BackupDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PCControlUltimate",
        "Backups"
    );

    public static object CreateBackup(string description = "Manual Safety Snapshot")
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory);

            var capturedEntries = new List<RegistryValueEntry>();

            // Capture all whitelisted keys from RegistryOptimizer
            foreach (var keyPath in RegistryOptimizer.AllowedSubkeys)
            {
                CaptureKeyValues(Registry.LocalMachine, keyPath, capturedEntries);
                CaptureKeyValues(Registry.CurrentUser, keyPath, capturedEntries);
            }

            string snapshotId = $"snap-{Guid.NewGuid():N}";
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string fileName = $"Snapshot_{timestamp}_{snapshotId}.json";
            string filePath = Path.Combine(BackupDirectory, fileName);

            // Create temporary manifest without hash to calculate checksum
            var tempManifest = new
            {
                SnapshotId = snapshotId,
                CreatedUtc = DateTime.UtcNow,
                Description = description,
                TotalKeysCaptured = capturedEntries.Count,
                Entries = capturedEntries
            };

            string jsonContent = JsonSerializer.Serialize(tempManifest, new JsonSerializerOptions { WriteIndented = true });
            
            // Calculate SHA-256 of the content
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(jsonContent));
            string checksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            // Write final signed manifest
            var finalManifest = new RegistrySnapshotManifest(
                SnapshotId: snapshotId,
                CreatedUtc: tempManifest.CreatedUtc,
                Description: description,
                TotalKeysCaptured: capturedEntries.Count,
                Entries: capturedEntries,
                ChecksumSha256: checksum
            );

            string finalJson = JsonSerializer.Serialize(finalManifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, finalJson, Encoding.UTF8);

            var fileInfo = new FileInfo(filePath);

            return new
            {
                success = true,
                snapshotId,
                filePath,
                fileSizeBytes = fileInfo.Length,
                checksumSha256 = checksum,
                capturedKeysCount = capturedEntries.Count
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                error = $"Failed to create registry snapshot: {ex.Message}"
            };
        }
    }

    private static void CaptureKeyValues(RegistryKey rootKey, string subKeyPath, List<RegistryValueEntry> list)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath, false);
            if (key == null) return;

            string fullPrefix = rootKey == Registry.LocalMachine ? "HKLM\\" : "HKCU\\";
            foreach (var valueName in key.GetValueNames())
            {
                var kind = key.GetValueKind(valueName);
                var rawVal = key.GetValue(valueName);
                string? dataStr = rawVal?.ToString();

                list.Add(new RegistryValueEntry(
                    KeyPath: fullPrefix + subKeyPath,
                    ValueName: valueName,
                    ValueKind: kind.ToString(),
                    ValueData: dataStr
                ));
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
            if (!File.Exists(filePath))
                return (false, null, "File does not exist");

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotManifest>(json);
            if (manifest == null)
                return (false, null, "Corrupt or invalid JSON structure");

            // Recalculate checksum excluding the checksum field
            var tempManifest = new
            {
                SnapshotId = manifest.SnapshotId,
                CreatedUtc = manifest.CreatedUtc,
                Description = manifest.Description,
                TotalKeysCaptured = manifest.TotalKeysCaptured,
                Entries = manifest.Entries
            };

            string recalculateJson = JsonSerializer.Serialize(tempManifest, new JsonSerializerOptions { WriteIndented = true });
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(recalculateJson));
            string calculated = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            bool matches = string.Equals(calculated, manifest.ChecksumSha256, StringComparison.OrdinalIgnoreCase);
            return (matches, manifest.ChecksumSha256, matches ? null : "Checksum mismatch");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public static (bool success, int restoredCount, int failedCount, string? error) RestoreFromBackup(string filePath)
    {
        try
        {
            var (isValid, _, error) = VerifyBackupIntegrity(filePath);
            if (!isValid)
                return (false, 0, 0, $"Backup integrity check failed: {error}");

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<RegistrySnapshotManifest>(json);
            if (manifest == null || manifest.Entries == null)
                return (false, 0, 0, "No entries found in snapshot");

            int restored = 0;
            int failed = 0;

            foreach (var entry in manifest.Entries)
            {
                try
                {
                    bool isHklm = entry.KeyPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase);
                    string rawPath = entry.KeyPath.Substring(5); // Remove HKLM\ or HKCU\

                    if (int.TryParse(entry.ValueData, out int intVal))
                    {
                        bool ok = RegistryOptimizer.TryApplyDword(rawPath, entry.ValueName, intVal, out _);
                        if (ok) restored++;
                        else failed++;
                    }
                    else
                    {
                        // Fallback string / general registry write
                        RegistryKey root = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
                        using var key = root.OpenSubKey(rawPath, true);
                        if (key != null)
                        {
                            key.SetValue(entry.ValueName, entry.ValueData ?? string.Empty);
                            restored++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }
                catch
                {
                    failed++;
                }
            }

            return (restored > 0, restored, failed, null);
        }
        catch (Exception ex)
        {
            return (false, 0, 0, ex.Message);
        }
    }
}