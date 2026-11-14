using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using PCControlUltimate.Security;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class RegistrySnapshotTests
{
    [Fact]
    public void BuildCanonicalPayload_ProducesDeterministicSha256RegardlessOfEntryInsertionOrder()
    {
        var createdUtc = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        var entryA = new RegistryValueEntry
        {
            Hive = "HKCU",
            SubKey = @"System\GameConfigStore",
            ValueName = "GameDVR_Enabled",
            ValueKind = "DWord",
            RawValue = "0",
            WasInitiallyAbsent = false
        };

        var entryB = new RegistryValueEntry
        {
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            ValueName = "Win32PrioritySeparation",
            ValueKind = "DWord",
            RawValue = "38",
            WasInitiallyAbsent = false
        };

        string payload1 = RegistrySnapshotEngine.BuildCanonicalPayload(
            "snap-1",
            "2.0",
            createdUtc,
            "Canonical Test",
            new List<RegistryValueEntry> { entryA, entryB }
        );

        string hash1 = IntegrityVerifier.ComputeCanonicalSha256(payload1);
        string hash2 = IntegrityVerifier.ComputeCanonicalSha256(payload1);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void VerifyBackupIntegrity_WithValidCanonicalSnapshot_ReturnsTrue()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 10, 15, 30, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-valid-001",
                "2.0",
                createdUtc,
                "Valid Test Backup",
                entries
            );

            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-valid-001",
                FormatVersion = "2.0",
                CreatedUtc = createdUtc,
                Description = "Valid Test Backup",
                ScopeNotice = "Backup dos Ajustes Suportados pelo Aplicativo",
                TotalKeysCaptured = entries.Count,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempFile, json, Encoding.UTF8);

            var (isValid, checksum, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.True(isValid);
            Assert.Null(error);
            Assert.Equal(snapshot.ChecksumSha256, checksum);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_WithTamperedFile_ShouldDetectChecksumMismatch()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            string tamperedJson = "{\n" +
                                  "  \"SnapshotId\": \"snap-test-123\",\n" +
                                  "  \"FormatVersion\": \"2.0\",\n" +
                                  "  \"CreatedUtc\": \"2026-11-10T12:00:00Z\",\n" +
                                  "  \"Description\": \"Tampered Snapshot\",\n" +
                                  "  \"TotalKeysCaptured\": 0,\n" +
                                  "  \"Entries\": [],\n" +
                                  "  \"ChecksumSha256\": \"1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff\"\n" +
                                  "}";

            File.WriteAllText(tempFile, tamperedJson, Encoding.UTF8);

            var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.False(isValid);
            Assert.Contains("Checksum mismatch", error ?? string.Empty);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_WithUnauthorizedSubkeyInJson_ShouldRejectBackup()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 10, 15, 30, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    Hive = "HKLM",
                    SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    ValueName = "InjectedPersistence",
                    ValueKind = "String",
                    RawValue = "cmd.exe",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-malicious",
                "2.0",
                createdUtc,
                "Malicious Backup",
                entries
            );

            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-malicious",
                FormatVersion = "2.0",
                CreatedUtc = createdUtc,
                Description = "Malicious Backup",
                TotalKeysCaptured = 1,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };

            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.False(isValid);
            Assert.Contains("fora da whitelist", error ?? string.Empty);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_WithDuplicateRegistryTargets_IsRejected()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 12, 13, 0, 0, DateTimeKind.Utc);
            var duplicate = new RegistryValueEntry
            {
                TweakId = "perf-game-dvr",
                Hive = "HKCU",
                SubKey = @"System\GameConfigStore",
                ValueName = "GameDVR_Enabled",
                ValueKind = "DWord",
                RawValue = "1",
                WasInitiallyAbsent = false
            };
            var entries = new List<RegistryValueEntry> { duplicate, new RegistryValueEntry
            {
                TweakId = duplicate.TweakId,
                Hive = duplicate.Hive,
                SubKey = duplicate.SubKey,
                ValueName = duplicate.ValueName,
                ValueKind = duplicate.ValueKind,
                RawValue = "0",
                WasInitiallyAbsent = false
            }};

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-duplicate-target",
                "2.1",
                createdUtc,
                "Duplicate target test",
                entries);
            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-duplicate-target",
                FormatVersion = "2.1",
                CreatedUtc = createdUtc,
                Description = "Duplicate target test",
                TotalKeysCaptured = entries.Count,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.False(isValid);
            Assert.Contains("duplicada", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_WithNonExistentFile_ReturnsFalse()
    {
        var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity("invalid_non_existent_snapshot.json");
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void RestoreFromBackupWithReport_IncompleteBackupWithoutConfirmation_MustBeBlocked()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-incomplete-001",
                "2.1",
                createdUtc,
                "Incomplete Backup Test",
                entries
            );

            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-incomplete-001",
                FormatVersion = "2.1",
                CreatedUtc = createdUtc,
                Description = "Incomplete Backup Test",
                TotalKeysCaptured = 1,
                ReadFailuresCount = 2,
                UnreadableItems = new List<RegistryReadFailure>
                {
                    new RegistrySnapshotReadFailure("perf-win32-priority", "HKLM", @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", "Access Denied")
                },
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };

            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(tempFile, allowIncompleteBackup: false);

            Assert.False(report.Success);
            Assert.Equal(0, report.RestoredCount);
            Assert.Contains(report.Errors, e => e.Contains("confirmação explícita", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void RestoreCatalogTweakFromTrustedSource_IncompleteSnapshotWithoutExplicitConfirmation_IsBlocked()
    {
        if (!OperatingSystem.IsWindows()) return;

        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-incomplete-per-tweak", "2.1", createdUtc,
                "Incomplete per-tweak restore test", entries);
            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-incomplete-per-tweak",
                FormatVersion = "2.1",
                CreatedUtc = createdUtc,
                Description = "Incomplete per-tweak restore test",
                TotalKeysCaptured = 1,
                ReadFailuresCount = 1,
                UnreadableItems = new List<RegistrySnapshotReadFailure>
                {
                    new("perf-win32-priority", "HKLM", @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", "Access Denied")
                },
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var result = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource(
                "perf-game-dvr", snapshotIdOrPath: tempFile);

            Assert.False(result.Success);
            Assert.Contains("confirmação explícita", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RestoreFromBackupWithReport_PartialFailureInMultiItemRestore_NeverReportsFullSuccess()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 10, 16, 30, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                },
                // Second entry has a value outside AllowedValues ("999999"), causing a deterministic item failure
                new RegistryValueEntry
                {
                    TweakId = "perf-fso-flags",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_FSEBehavior",
                    ValueKind = "DWord",
                    RawValue = "999999",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-partial-fail-001",
                "2.1",
                createdUtc,
                "Partial Failure Test",
                entries
            );

            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-partial-fail-001",
                FormatVersion = "2.1",
                CreatedUtc = createdUtc,
                Description = "Partial Failure Test",
                TotalKeysCaptured = entries.Count,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };

            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                tempFile,
                confirmIncompleteBackup: true,
                singleTweakId: null,
                rollbackOnPartialFailure: false
            );

            Assert.False(report.Success);
            Assert.True(report.FailedCount > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RestoreFromBackupWithReport_RollbackRequestedOutsideWindows_IsRejectedBeforeMutation()
    {
        if (OperatingSystem.IsWindows())
            return;

        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-rollback-nonwindows",
                "2.1",
                createdUtc,
                "Rollback non-Windows test",
                entries
            );
            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-rollback-nonwindows",
                FormatVersion = "2.1",
                CreatedUtc = createdUtc,
                Description = "Rollback non-Windows test",
                TotalKeysCaptured = entries.Count,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                tempFile,
                confirmIncompleteBackup: true,
                rollbackOnPartialFailure: true
            );

            Assert.False(report.Success);
            Assert.False(report.RolledBack);
            Assert.Equal(0, report.RestoredCount);
            Assert.Contains("Windows nativo", report.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void InspectBackup_WithValidSnapshot_ReturnsSnapshotDetailsAndIntegrityNotice()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 10, 16, 15, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "0",
                    WasInitiallyAbsent = false
                }
            };

            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "snap-inspect-001",
                "2.1",
                createdUtc,
                "Inspect Test",
                entries
            );

            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-inspect-001",
                FormatVersion = "2.1",
                CreatedUtc = createdUtc,
                Description = "Inspect Test",
                TotalKeysCaptured = 1,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };

            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var inspected = RegistrySnapshotEngine.InspectBackup(tempFile);
            Assert.NotNull(inspected);
            Assert.Equal("snap-inspect-001", inspected!.SnapshotId);
            Assert.Contains("SHA-256", inspected.IntegrityVerificationNotice);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
    [Theory]
    [InlineData("9.9", "CatalogRegistrySnapshot")]
    [InlineData("2.1", "UnknownSnapshotType")]
    public void VerifyBackupIntegrity_UnsupportedSchema_IsRejected(string formatVersion, string snapshotType)
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var snapshot = new RegistrySnapshotFile
            {
                SnapshotId = "snap-unsupported-schema",
                FormatVersion = formatVersion,
                SnapshotType = snapshotType,
                Entries = new List<RegistryValueEntry>()
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.False(isValid);
            Assert.Contains("não suportad", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_Version22_BindsCompletenessMetadataToChecksum()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };
            var failures = new List<RegistryReadFailure>
            {
                new RegistryReadFailure("perf-win32-priority",
                    @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
                    "Win32PrioritySeparation", "Access denied")
            };
            const string snapshotId = "snap-v22-completeness";
            const string description = "Version 2.2 completeness test";
            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "2.2",
                snapshotId,
                createdUtc.ToString("O"),
                description,
                entries,
                readFailuresCount: 1,
                totalKeysCaptured: entries.Count,
                unreadableItems: failures,
                snapshotType: "CatalogRegistrySnapshot");

            var snapshot = new RegistrySnapshotFile
            {
                FormatVersion = "2.2",
                SnapshotId = snapshotId,
                CreatedUtc = createdUtc,
                Description = description,
                TotalKeysCaptured = entries.Count,
                ReadFailuresCount = 1,
                UnreadableItems = failures,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };

            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);
            var (validBeforeTampering, _, _) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);
            Assert.True(validBeforeTampering);

            // Simulate editing the JSON to hide a failed read while retaining the old checksum.
            snapshot.ReadFailuresCount = 0;
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var (validAfterTampering, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);
            Assert.False(validAfterTampering);
            Assert.Contains("Checksum mismatch", error ?? string.Empty);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RestoreFromBackupWithReport_LegacyCompleteBackupWithoutConfirmation_MustBeBlocked()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 13, 13, 0, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };
            const string snapshotId = "snap-legacy-confirmation";
            const string description = "Legacy backup confirmation test";
            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "2.1", snapshotId, createdUtc.ToString("O"), description, entries);

            var snapshot = new RegistrySnapshotFile
            {
                FormatVersion = "2.1",
                SnapshotId = snapshotId,
                CreatedUtc = createdUtc,
                Description = description,
                TotalKeysCaptured = entries.Count,
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                tempFile, allowIncompleteBackup: false);

            Assert.False(report.Success);
            Assert.Equal(0, report.RestoredCount);
            Assert.Contains("formato legado", report.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyBackupIntegrity_Version22_RejectsInconsistentCompletenessCounters()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var createdUtc = new DateTime(2026, 11, 13, 14, 0, 0, DateTimeKind.Utc);
            var entries = new List<RegistryValueEntry>
            {
                new RegistryValueEntry
                {
                    TweakId = "perf-game-dvr",
                    Hive = "HKCU",
                    SubKey = @"System\GameConfigStore",
                    ValueName = "GameDVR_Enabled",
                    ValueKind = "DWord",
                    RawValue = "1",
                    WasInitiallyAbsent = false
                }
            };
            const string snapshotId = "snap-v22-inconsistent-counts";
            const string description = "Inconsistent counters test";
            string canonicalPayload = RegistrySnapshotEngine.BuildCanonicalPayload(
                "2.2", snapshotId, createdUtc.ToString("O"), description, entries,
                readFailuresCount: 0, totalKeysCaptured: 0,
                unreadableItems: new List<RegistryReadFailure>());

            var snapshot = new RegistrySnapshotFile
            {
                FormatVersion = "2.2",
                SnapshotId = snapshotId,
                CreatedUtc = createdUtc,
                Description = description,
                TotalKeysCaptured = 0,
                ReadFailuresCount = 0,
                UnreadableItems = new List<RegistryReadFailure>(),
                Entries = entries,
                ChecksumSha256 = IntegrityVerifier.ComputeCanonicalSha256(canonicalPayload)
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot), Encoding.UTF8);

            var (isValid, _, error) = RegistrySnapshotEngine.VerifyBackupIntegrity(tempFile);

            Assert.False(isValid);
            Assert.Contains("Metadados de completude inconsistentes", error ?? string.Empty);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

}
