using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PCControlUltimate.Windows.Services;
using Xunit;

[assembly: SupportedOSPlatform("windows")]

namespace PCControlUltimate.Tests;

[SupportedOSPlatform("windows")]
public class RegistryOptimizerTests : IDisposable
{
    private readonly string _testLedgerDirectory;
    private readonly FakeRegistryBackend _fakeRegistry = new();

    public RegistryOptimizerTests()
    {
        _testLedgerDirectory = Path.Combine(Path.GetTempPath(), $"pccontrol-registry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLedgerDirectory);
        RegistryStateLedger.LedgerFilePath = Path.Combine(_testLedgerDirectory, "test-ledger.dpapi");
        RegistryOptimizer.RegistryBackend = _fakeRegistry;
        RegistryStateLedger.ClearLedgerForTesting();
    }

    public void Dispose()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        RegistryOptimizer.RegistryBackend = new WindowsRegistryBackend();
        try
        {
            if (Directory.Exists(_testLedgerDirectory))
                Directory.Delete(_testLedgerDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not mask the actual assertion result.
        }
    }

    [Fact]
    public void ApplyCatalogTweak_AccessDeniedOnRead_ReturnsFailureWithoutWriting()
    {
        _fakeRegistry.ReadException = new UnauthorizedAccessException("simulated access denied");
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.False(result.Success);
        Assert.Contains("simulated access denied", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void ApplyCatalogTweak_WriteFailure_ReturnsFailure()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        _fakeRegistry.WriteException = new UnauthorizedAccessException("simulated write denied");
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.False(result.Success);
        Assert.Contains("simulated write denied", result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public void ApplyCatalogTweak_ReadbackMismatch_IsNotReportedAsSuccess()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        _fakeRegistry.CorruptReadback = true;
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.False(result.Success);
        Assert.Contains("Verificação falhou", result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public void RestoreCatalogTweak_RestoresCustomOriginalValueAndKind_UsingFakeBackend()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", "custom-original", RegistryValueKind.String);
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.True(applied.Success, applied.ErrorMessage);
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", applied.OperationId);
        Assert.True(restored.Success, restored.ErrorMessage);
        var snapshot = _fakeRegistry.Read("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        Assert.True(snapshot.ValueExists);
        Assert.Equal(RegistryValueKind.String, snapshot.Kind);
        Assert.Equal("custom-original", snapshot.Value);
    }

    [Fact]
    public void RestoreCatalogTweak_InitiallyAbsentValue_RemovesValueAndCreatedKey()
    {
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.True(applied.Success, applied.ErrorMessage);
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", applied.OperationId);
        Assert.True(restored.Success, restored.ErrorMessage);
        var snapshot = _fakeRegistry.Read("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        Assert.False(snapshot.ValueExists);
        Assert.False(snapshot.KeyExists);
    }

    private sealed class FakeRegistryBackend : IRegistryBackend
    {
        private readonly Dictionary<string, (object Value, RegistryValueKind Kind)> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        public Exception? ReadException { get; set; }
        public Exception? WriteException { get; set; }
        public bool CorruptReadback { get; set; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }

        private static string Key(string hive, string path) => $"{hive}|{path}";
        private static string ValueKey(string hive, string path, string name) => $"{Key(hive, path)}|{name}";

        public void Seed(string hive, string path, string name, object value, RegistryValueKind kind)
        {
            _keys.Add(Key(hive, path));
            _values[ValueKey(hive, path, name)] = (value, kind);
        }

        public RegistryValueSnapshot Read(string hive, string subKeyPath, string valueName)
        {
            ReadCalls++;
            if (ReadException != null) throw ReadException;
            string key = Key(hive, subKeyPath);
            if (!_keys.Contains(key))
                return new RegistryValueSnapshot(false, false, null, RegistryValueKind.Unknown, 0, 0);
            if (_values.TryGetValue(ValueKey(hive, subKeyPath, valueName), out var item))
            {
                if (CorruptReadback && ReadCalls >= 2)
                    return new RegistryValueSnapshot(true, true, "unexpected-value", RegistryValueKind.String, 1, 0);
                return new RegistryValueSnapshot(true, true, item.Value, item.Kind, 1, 0);
            }
            int count = _values.Keys.Count(k => k.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase));
            return new RegistryValueSnapshot(true, false, null, RegistryValueKind.Unknown, count, 0);
        }

        public void Write(string hive, string subKeyPath, string valueName, object value, RegistryValueKind kind)
        {
            WriteCalls++;
            if (WriteException != null) throw WriteException;
            _keys.Add(Key(hive, subKeyPath));
            _values[ValueKey(hive, subKeyPath, valueName)] = (value, kind);
        }

        public void DeleteValue(string hive, string subKeyPath, string valueName)
        {
            _values.Remove(ValueKey(hive, subKeyPath, valueName));
        }

        public void DeleteKeyIfEmpty(string hive, string subKeyPath)
        {
            string key = Key(hive, subKeyPath);
            if (!_values.Keys.Any(k => k.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)))
                _keys.Remove(key);
        }
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\PriorityControl")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games")]
    [InlineData(@"System\GameConfigStore")]
    public void AllowedSubkeys_ShouldContainCoreGamingOptimizationPaths(string subkey)
    {
        Assert.Contains(subkey, RegistryOptimizer.AllowedSubkeys);
        Assert.True(RegistryOptimizer.IsSubkeyAllowed(subkey));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows Defender")]
    [InlineData(@"SOFTWARE\Policies\Microsoft\WindowsFirewall")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\WinDefend")]
    public void DangerousOrSecurityPaths_MustNeverBeInWhitelist(string dangerousPath)
    {
        Assert.DoesNotContain(dangerousPath, RegistryOptimizer.AllowedSubkeys);
        Assert.False(RegistryOptimizer.IsSubkeyAllowed(dangerousPath));
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\PriorityControl\ArbitraryChildSubkey")]
    [InlineData(@"System\GameConfigStore\NestedBypassKey")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\EvilSubkey")]
    public void IsSubkeyAllowed_ChildSubkeysNotExplicitlyCataloged_MustBeRejected(string childSubkey)
    {
        Assert.False(RegistryOptimizer.IsSubkeyAllowed(childSubkey));
    }

    [Fact]
    public void Catalog_ShouldContainMandatoryGamingTweaksWithExplicitTypesAndAllowedValues()
    {
        Assert.NotEmpty(RegistryOptimizer.Catalog);
        Assert.True(RegistryOptimizer.Catalog.ContainsKey("perf-mmcss"));
        Assert.True(RegistryOptimizer.Catalog.ContainsKey("perf-game-dvr"));
        Assert.True(RegistryOptimizer.Catalog.ContainsKey("power-pcie-link"));

        var stringTweak = RegistryOptimizer.Catalog["power-pcie-link"];
        Assert.Equal(RegistryValueKind.String, stringTweak.ValueKind);
        Assert.Equal("High", stringTweak.TargetRawValue);
        Assert.Contains("High", stringTweak.AllowedRawValues);
    }

    [Fact]
    public void ApplyCatalogTweak_WithUnknownTweakId_MustBeRejectedAndLogged()
    {
        var result = RegistryOptimizer.ApplyCatalogTweak("unknown-arbitrary-tweak-id", "1");

        Assert.False(result.Success);
        Assert.Contains("não encontrado no catálogo fechado", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var logs = RegistryOptimizer.GetRecentAuditLogs(10);
        Assert.Contains(logs, l => l.TweakId == "unknown-arbitrary-tweak-id" && !l.Success);
    }

    [Fact]
    public void ApplyCatalogTweak_WithUnauthorizedTargetValue_MustBeRejected()
    {
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "999999");

        Assert.False(result.Success);
        Assert.Contains("não está na lista de valores permitidos", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryApplyTypedValue_UncatalogedValueNameInsideAllowedSubkey_MustBeRejected()
    {
        var result = RegistryOptimizer.TryApplyTypedValue(
            RegistryHive.CurrentUser,
            @"System\GameConfigStore",
            "ArbitraryUncatalogedValue",
            RegistryValueKind.DWord,
            "1"
        );

        Assert.False(result.Success);
        Assert.Contains("catálogo fechado", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RegistryValueKind.DWord, 42, "42")]
    [InlineData(RegistryValueKind.QWord, 9876543210L, "9876543210")]
    [InlineData(RegistryValueKind.String, "High", "High")]
    [InlineData(RegistryValueKind.ExpandString, "%SystemRoot%\\System32", "%SystemRoot%\\System32")]
    public void SerializeAndDeserializeValue_ScalarTypes_RoundtripsAccurately(
        RegistryValueKind kind,
        object inputObj,
        string expectedRaw)
    {
        string? raw = RegistryOptimizer.SerializeValue(inputObj, kind);
        Assert.Equal(expectedRaw, raw);

        object? deserialized = RegistryOptimizer.DeserializeValue(raw, kind);
        Assert.NotNull(deserialized);
        Assert.Equal(Convert.ToString(inputObj), Convert.ToString(deserialized));
    }

    [Fact]
    public void SerializeAndDeserializeValue_MultiStringAndBinary_RoundtripsAccurately()
    {
        string[] multiInput = new[] { "Line1", "Line2", "Line3" };
        string? rawMulti = RegistryOptimizer.SerializeValue(multiInput, RegistryValueKind.MultiString);
        Assert.NotNull(rawMulti);

        var restoredMulti = RegistryOptimizer.DeserializeValue(rawMulti, RegistryValueKind.MultiString) as string[];
        Assert.NotNull(restoredMulti);
        Assert.Equal(multiInput, restoredMulti);

        byte[] binaryInput = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        string? rawBin = RegistryOptimizer.SerializeValue(binaryInput, RegistryValueKind.Binary);
        Assert.NotNull(rawBin);

        var restoredBin = RegistryOptimizer.DeserializeValue(rawBin, RegistryValueKind.Binary) as byte[];
        Assert.NotNull(restoredBin);
        Assert.Equal(binaryInput, restoredBin);
    }

    [Fact]
    public void TryApplyDword_WithForbiddenPath_ShouldFailWithoutModifyingSystem()
    {
        bool success4Arg = RegistryOptimizer.TryApplyDword(
            @"SOFTWARE\MaliciousKey\Payload",
            "DisruptiveValue",
            1,
            out var previousValue
        );

        Assert.False(success4Arg);
        Assert.Null(previousValue);

        bool success3Arg = RegistryOptimizer.TryApplyDword(
            @"SOFTWARE\MaliciousKey\Payload",
            "DisruptiveValue",
            1
        );

        Assert.False(success3Arg);
    }

    [Fact]
    public void TryRestoreValue_WithForbiddenPath_EvenWhenInitiallyAbsent_MustFailSafely()
    {
        var result = RegistryOptimizer.TryRestoreValue(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "UnauthorizedEntry",
            wasInitiallyAbsent: true,
            originalRawValue: null,
            originalKind: RegistryValueKind.String
        );

        Assert.False(result.Success);
        Assert.Contains("catálogo fechado", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRestoreValue_WithUncatalogedValueNameInsideAllowedSubkey_MustFailSafely()
    {
        var result = RegistryOptimizer.TryRestoreValue(
            RegistryHive.CurrentUser,
            @"System\GameConfigStore",
            "UncatalogedMaliciousValue",
            wasInitiallyAbsent: false,
            originalRawValue: "1",
            originalKind: RegistryValueKind.DWord
        );

        Assert.False(result.Success);
        Assert.Contains("catálogo fechado", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRestoreParametersAgainstCatalog_RestoredValueOutsideAllowedValues_MustBeRejected()
    {
        // "perf-game-dvr" only allows "0" or "1"
        var (isValid, errorReason) = RegistryOptimizer.ValidateRestoreParametersAgainstCatalog(
            "perf-game-dvr",
            originalRawValue: "999999",
            originalKind: RegistryValueKind.DWord,
            wasInitiallyAbsent: false
        );

        Assert.False(isValid);
        Assert.Contains("RESTORE_VALUE_NOT_ALLOWED", errorReason);
    }

    [Fact]
    public void ValidateRestoreParametersAgainstCatalog_IncompatibleValueKind_MustBeRejected()
    {
        // "perf-game-dvr" expects DWord, passing String must be rejected
        var (isValid, errorReason) = RegistryOptimizer.ValidateRestoreParametersAgainstCatalog(
            "perf-game-dvr",
            originalRawValue: "1",
            originalKind: RegistryValueKind.String,
            wasInitiallyAbsent: false
        );

        Assert.False(isValid);
        Assert.Contains("INCOMPATIBLE_VALUE_KIND", errorReason);
    }

    [Fact]
    public void RegistryStateLedger_RestoreWithoutTrustedHistory_MustBeBlocked()
    {
        RegistryStateLedger.ClearLedgerForTesting();

        var validation = RegistryStateLedger.ResolveAndValidateRestore("perf-game-dvr");
        Assert.False(validation.IsValid);
        Assert.Equal("NO_TRUSTED_BACKEND_HISTORY", validation.ErrorCode);
        Assert.Contains("NO_TRUSTED_BACKEND_HISTORY", validation.ErrorReason);

        var restoreResult = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr");
        Assert.False(restoreResult.Success);
        Assert.Equal("NO_TRUSTED_BACKEND_HISTORY", restoreResult.ErrorCode);
        Assert.Contains("NO_TRUSTED_BACKEND_HISTORY", restoreResult.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public void RegistryStateLedger_CrossTweakOperationIdMismatch_MustBeRejected()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];

        var recorded = RegistryStateLedger.RecordBeforeModification(
            def,
            originalKind: RegistryValueKind.DWord,
            originalRawValue: "1",
            wasInitiallyAbsent: false,
            keyCreatedByApp: false,
            appliedValue: "0"
        );

        // Attempting to use perf-game-dvr's operationId to restore a different tweak ("perf-fso-flags") must fail
        var mismatchValidation = RegistryStateLedger.ResolveAndValidateRestore("perf-fso-flags", recorded.OperationId);
        Assert.False(mismatchValidation.IsValid);
        Assert.Contains("OPERATION_TWEAK_MISMATCH", mismatchValidation.ErrorReason);
    }

    [Fact]
    public void RegistryStateLedger_TamperedEntryHmac_MustBeRejected()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        var recorded = RegistryStateLedger.RecordBeforeModification(
            def,
            originalKind: RegistryValueKind.DWord,
            originalRawValue: "1",
            wasInitiallyAbsent: false,
            keyCreatedByApp: false,
            appliedValue: "0"
        );

        // Tamper with OriginalRawValue while keeping the old HMAC
        var tamperedEntry = recorded with { OriginalRawValue = "999999" };
        Assert.False(RegistryStateLedger.VerifyEntryHmac(tamperedEntry));
    }

    [Fact]
    public void RestoreCatalogTweak_LegacyCallerSuppliedState_IsIgnoredWithoutTrustedHistory()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        var result = RegistryOptimizer.RestoreCatalogTweak(
            "perf-game-dvr",
            wasInitiallyAbsent: false,
            originalRawValue: "999999",
            originalKind: RegistryValueKind.String,
            keyCreatedByApp: false
        );

        Assert.False(result.Success);
        Assert.Equal("NO_TRUSTED_BACKEND_HISTORY", result.ErrorCode);
    }

    [Fact]
    public void RestoreCatalogTweakFromTrustedSource_DoesNotTouchRealRegistryWithoutTrustedHistory()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        var result = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-fso-flags");
        Assert.False(result.Success);
        Assert.Equal("NO_TRUSTED_BACKEND_HISTORY", result.ErrorCode);
    }

    [Fact]
    public void RegistryStateLedger_AuthenticatedCustomOriginalValueAndKind_AreRestorable()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];

        var recorded = RegistryStateLedger.RecordBeforeModification(
            def,
            originalKind: RegistryValueKind.String,
            originalRawValue: "custom-original-value",
            wasInitiallyAbsent: false,
            keyCreatedByApp: false,
            appliedValue: "0"
        );

        var resolved = RegistryStateLedger.ResolveTrustedRestoreEntry(def.TweakId, recorded.OperationId);

        Assert.True(resolved.IsValid, resolved.ErrorReason);
        Assert.NotNull(resolved.Entry);
        Assert.Equal("custom-original-value", resolved.Entry!.OriginalRawValue);
        Assert.Equal(nameof(RegistryValueKind.String), resolved.Entry.OriginalValueKind);
    }

    [Fact]
    public void RegistryStateLedger_RestoredAtUtc_IsCoveredByEntryHmac()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        var entry = new RegistryLedgerEntry
        {
            OperationId = "op-test-restored-at",
            TweakId = "perf-game-dvr",
            Hive = "HKCU",
            SubKeyPath = @"System\GameConfigStore",
            ValueName = "GameDVR_Enabled",
            OriginalValueKind = nameof(RegistryValueKind.DWord),
            OriginalRawValue = "1",
            WasInitiallyAbsent = false,
            KeyCreatedByApp = false,
            AppliedValue = "0",
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
            IsRestored = true,
            RestoredAtUtc = "2026-11-12T10:00:00.0000000Z"
        };
        entry.EntryHmacSha256 = RegistryStateLedger.ComputeEntryHmac(entry);

        Assert.True(RegistryStateLedger.VerifyEntryHmac(entry));
        entry.RestoredAtUtc = "2026-11-12T10:01:00.0000000Z";
        Assert.False(RegistryStateLedger.VerifyEntryHmac(entry));
    }

}
