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
        RegistryStateLedger.ProtectedTokenWriter = PCControlUltimate.Security.IntegrityVerifier.SaveProtectedSessionTokenWithReport;
        RegistryStateLedger.ProtectedTokenReader = PCControlUltimate.Security.IntegrityVerifier.ReadProtectedSessionToken;
        RegistryOptimizer.RegistryBackend = _fakeRegistry;
        RegistryStateLedger.ClearLedgerForTesting();
    }

    public void Dispose()
    {
        RegistryStateLedger.ClearLedgerForTesting();
        RegistryOptimizer.RegistryBackend = new WindowsRegistryBackend();
        RegistryStateLedger.ProtectedTokenWriter = PCControlUltimate.Security.IntegrityVerifier.SaveProtectedSessionTokenWithReport;
        RegistryStateLedger.ProtectedTokenReader = PCControlUltimate.Security.IntegrityVerifier.ReadProtectedSessionToken;
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
        Assert.Equal("REGISTRY_ACCESS_DENIED", result.ErrorCode);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void ApplyCatalogTweak_WriteFailure_LeavesTrustedRecoveryEntryBeforeMutation()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        _fakeRegistry.WriteException = new UnauthorizedAccessException("simulated write denied");

        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");

        Assert.False(result.Success);
        Assert.Contains("simulated write denied", result.ErrorMessage ?? string.Empty);
        Assert.Equal("REGISTRY_ACCESS_DENIED", result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.OperationId));
        var ledgerEntry = Assert.Single(RegistryStateLedger.GetAllEntries());
        Assert.Equal(result.OperationId, ledgerEntry.OperationId);
        Assert.Equal("1", ledgerEntry.OriginalRawValue);
        Assert.Equal("0", ledgerEntry.AppliedValue);

        _fakeRegistry.WriteException = null;
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", result.OperationId);
        Assert.True(restored.Success, restored.ErrorMessage);
        var snapshot = _fakeRegistry.Read("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        Assert.Equal(1, snapshot.Value);
        Assert.Equal(RegistryValueKind.DWord, snapshot.Kind);
    }

    [Fact]
    public void RestoreCatalogTweak_OriginalValueAlreadyPresent_DoesNotWriteAgain()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        var entry = RegistryStateLedger.RecordAppliedTweakState(
            def,
            originalRawValue: "1",
            originalKind: RegistryValueKind.DWord,
            wasInitiallyAbsent: false,
            keyCreatedByApp: false,
            appliedValue: "0");
        _fakeRegistry.Seed(def.Hive, def.SubKeyPath, def.ValueName, 1, RegistryValueKind.DWord);

        var result = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource(def.TweakId, entry.OperationId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1", result.VerifiedValue);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
        var persisted = Assert.Single(RegistryStateLedger.GetAllEntries());
        Assert.True(persisted.IsRestored);
    }

    [Fact]
    public void ApplyCatalogTweak_AbsentUndeterminedState_IsNotModified()
    {
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");

        Assert.False(result.Success);
        Assert.Equal("REGISTRY_DEFAULT_UNDETERMINED", result.ErrorCode);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
        Assert.Empty(RegistryStateLedger.GetAllEntries());
    }

    [Fact]
    public void ApplyCatalogTweak_CustomRegistryState_IsNotOverwritten()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 7, RegistryValueKind.DWord);

        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");

        Assert.False(result.Success);
        Assert.Equal("REGISTRY_STATE_NOT_DEFAULT", result.ErrorCode);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
        var snapshot = _fakeRegistry.Read("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        Assert.Equal(7, snapshot.Value);
    }

    [Fact]
    public void ApplyCatalogTweak_ReadbackMismatch_IsNotReportedAsSuccess()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        _fakeRegistry.CorruptReadback = true;
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.False(result.Success);
        Assert.Contains("Verificação falhou", result.ErrorMessage ?? string.Empty);
        Assert.Equal("REGISTRY_READBACK_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public void RestoreCatalogTweak_RestoresCustomOriginalValueAndKind_UsingFakeBackend()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        _fakeRegistry.Seed(def.Hive, def.SubKeyPath, def.ValueName, 0, RegistryValueKind.DWord);
        var ledgerEntry = RegistryStateLedger.RecordBeforeModification(
            def,
            originalKind: RegistryValueKind.String,
            originalRawValue: "custom-original",
            wasInitiallyAbsent: false,
            keyCreatedByApp: false,
            appliedValue: "0"
        );
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", ledgerEntry.OperationId);
        Assert.True(restored.Success, restored.ErrorMessage);
        var snapshot = _fakeRegistry.Read("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        Assert.True(snapshot.ValueExists);
        Assert.Equal(RegistryValueKind.String, snapshot.Kind);
        Assert.Equal("custom-original", snapshot.Value);
    }

    [Fact]
    public void RestoreCatalogTweak_InitiallyAbsentValue_PreservesOtherValuesInExistingKey()
    {
        _fakeRegistry.Seed("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "UnrelatedValue", 123, RegistryValueKind.DWord);

        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-mmcss", "10");
        Assert.True(applied.Success, applied.ErrorMessage);

        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-mmcss", applied.OperationId);

        Assert.True(restored.Success, restored.ErrorMessage);
        var targetSnapshot = _fakeRegistry.Read("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness");
        var unrelatedSnapshot = _fakeRegistry.Read("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "UnrelatedValue");
        Assert.True(targetSnapshot.KeyExists);
        Assert.False(targetSnapshot.ValueExists);
        Assert.True(unrelatedSnapshot.KeyExists);
        Assert.True(unrelatedSnapshot.ValueExists);
        Assert.Equal(RegistryValueKind.DWord, unrelatedSnapshot.Kind);
        Assert.Equal(123, unrelatedSnapshot.Value);
    }

    [Fact]
    public void RestoreCatalogTweak_InitiallyAbsentValue_RemovesValueAndCreatedKey()
    {
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-mmcss", "10");
        Assert.True(applied.Success, applied.ErrorMessage);
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-mmcss", applied.OperationId);
        Assert.True(restored.Success, restored.ErrorMessage);
        var snapshot = _fakeRegistry.Read("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness");
        Assert.False(snapshot.ValueExists);
        Assert.False(snapshot.KeyExists);
    }

    [Fact]
    public void RestoreCatalogTweak_ReadbackMismatch_ReturnsFailure()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.True(applied.Success, applied.ErrorMessage);

        // Corrupt only the post-write read-back; the pre-restore guard must see the genuine applied value.
        _fakeRegistry.CorruptReadbackAtReadCall = _fakeRegistry.ReadCalls + 2;
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", applied.OperationId);

        Assert.False(restored.Success);
        Assert.Contains("Verificação pós-restauração falhou", restored.ErrorMessage ?? string.Empty);
        Assert.Equal("REGISTRY_READBACK_MISMATCH", restored.ErrorCode);
    }

    [Fact]
    public void RestoreCatalogTweak_ExternalRegistryChange_IsNotOverwritten()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.True(applied.Success, applied.ErrorMessage);

        // Simulate the user or another application changing the value after our tweak.
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 7, RegistryValueKind.DWord);
        int writesBeforeRestore = _fakeRegistry.WriteCalls;

        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", applied.OperationId);

        Assert.False(restored.Success);
        Assert.Equal("REGISTRY_STATE_CHANGED", restored.ErrorCode);
        Assert.Equal(writesBeforeRestore, _fakeRegistry.WriteCalls);
        var snapshot = _fakeRegistry.Read("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        Assert.Equal(7, snapshot.Value);
        Assert.Equal(RegistryValueKind.DWord, snapshot.Kind);
    }

    [Fact]
    public void RestoreCatalogTweak_AccessDeniedOnRead_ReturnsSpecificErrorCode()
    {
        _fakeRegistry.Seed("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.True(applied.Success, applied.ErrorMessage);

        _fakeRegistry.ReadException = new UnauthorizedAccessException("simulated restore read denied");
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-game-dvr", applied.OperationId);

        Assert.False(restored.Success);
        Assert.Equal("REGISTRY_ACCESS_DENIED", restored.ErrorCode);
        Assert.Contains("simulated restore read denied", restored.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public void RestoreCatalogTweak_DeleteAccessDenied_ReturnsFailure()
    {
        var applied = RegistryOptimizer.ApplyCatalogTweak("perf-mmcss", "10");
        Assert.True(applied.Success, applied.ErrorMessage);

        _fakeRegistry.DeleteException = new UnauthorizedAccessException("simulated delete denied");
        var restored = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-mmcss", applied.OperationId);

        Assert.False(restored.Success);
        Assert.Contains("simulated delete denied", restored.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_KnownDocumentedAbsentValue_UsesDocumentedDefault()
    {
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState("perf-mmcss");
        Assert.Equal("Inativo", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_AbsentWithKnownDefault_UsesKnownDefault()
    {
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState("mem-lock-executive");
        Assert.Equal("Inativo", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_EditionDependentAbsentValue_IsNotAssumedInactive()
    {
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState("perf-game-dvr");
        Assert.Equal("Padrão Dependente", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_WrongRegistryType_ReturnsWrongTypeWithoutWriting()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        _fakeRegistry.Seed(def.Hive, def.SubKeyPath, def.ValueName, "0", RegistryValueKind.String);
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState(def.TweakId);
        Assert.Equal("Tipo Incorreto", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_AbsentUndocumentedValue_ReturnsUndeterminedWithoutWriting()
    {
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState("maint-startup-delay");
        Assert.Equal("Estado Não Determinável", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_AccessDenied_PreservesAccessDeniedStateWithoutWriting()
    {
        _fakeRegistry.ReadException = new UnauthorizedAccessException("simulated inspection denial");
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState("perf-game-dvr");
        Assert.Equal("Acesso Negado", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweakCurrentState_ReadError_PreservesReadErrorStateWithoutWriting()
    {
        _fakeRegistry.ReadException = new IOException("simulated inspection read error");
        string state = RegistryOptimizer.InspectCatalogTweakCurrentState("perf-game-dvr");
        Assert.Equal("Erro de Leitura", state);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweak_WrongRegistryType_MarksInspectionUndeterminable()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        _fakeRegistry.Seed(def.Hive, def.SubKeyPath, def.ValueName, "0", RegistryValueKind.String);
        var result = RegistryOptimizer.InspectCatalogTweak(def.TweakId);
        Assert.Equal("Tipo Incorreto", result.State);
        Assert.True(result.IsUndeterminable);
        Assert.Equal(RegistryValueKind.String, result.ActualValueKind);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweak_LargeSystemCacheAbsent_ReportsNoChangeNeeded()
    {
        var result = RegistryOptimizer.InspectCatalogTweak("mem-standby-clean");
        Assert.Equal("Sem Alteração Necessária", result.State);
        Assert.False(result.IsUndeterminable);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }


    [Theory]
    [InlineData("mem-standby-clean")]
    [InlineData("net-tcp-autotuning")]
    public void InspectCatalogTweak_NoOpCatalogEntry_AbsentValueReportsNoChangeNeeded(string tweakId)
    {
        var result = RegistryOptimizer.InspectCatalogTweak(tweakId);

        Assert.Equal("Sem Alteração Necessária", result.State);
        Assert.False(result.IsUndeterminable);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Theory]
    [InlineData("mem-standby-clean")]
    [InlineData("net-tcp-autotuning")]
    public void InspectCatalogTweak_NoOpCatalogEntry_DefaultValueReportsNoChangeNeeded(string tweakId)
    {
        var def = RegistryOptimizer.Catalog[tweakId];
        object value = RegistryOptimizer.DeserializeValue(def.RecommendedValue, def.ValueKind)!;
        _fakeRegistry.Seed(def.Hive, def.SubKeyPath, def.ValueName, value, def.ValueKind);

        var result = RegistryOptimizer.InspectCatalogTweak(tweakId);

        Assert.Equal("Sem Alteração Necessária", result.State);
        Assert.Equal(def.RecommendedValue, result.CurrentValue);
        Assert.False(result.IsUndeterminable);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweak_AccessDenied_MarksInspectionUndeterminable()
    {
        _fakeRegistry.ReadException = new UnauthorizedAccessException("simulated inspection denial");
        var result = RegistryOptimizer.InspectCatalogTweak("perf-game-dvr");
        Assert.Equal("Acesso Negado", result.State);
        Assert.True(result.IsUndeterminable);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    [Fact]
    public void InspectCatalogTweak_ReadError_MarksInspectionUndeterminable()
    {
        _fakeRegistry.ReadException = new IOException("simulated inspection read error");
        var result = RegistryOptimizer.InspectCatalogTweak("perf-game-dvr");
        Assert.Equal("Erro de Leitura", result.State);
        Assert.True(result.IsUndeterminable);
        Assert.Equal(0, _fakeRegistry.WriteCalls);
    }

    private sealed class FakeRegistryBackend : IRegistryBackend
    {
        private readonly Dictionary<string, (object Value, RegistryValueKind Kind)> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        public Exception? ReadException { get; set; }
        public Exception? WriteException { get; set; }
        public Exception? DeleteException { get; set; }
        public bool CorruptReadback { get; set; }
        public int CorruptReadbackAtReadCall { get; set; }
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
                if ((CorruptReadback && ReadCalls >= 2) || ReadCalls == CorruptReadbackAtReadCall)
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
            if (DeleteException != null) throw DeleteException;
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

    [Theory]
    [InlineData("power-ultimate-plan")]
    [InlineData("serv-sysmain")]
    [InlineData("serv-spooler")]
    public void Catalog_ChangesWithPowerOrServiceSideEffects_RequireIndividualReview(string tweakId)
    {
        var tweak = RegistryOptimizer.Catalog[tweakId];

        Assert.Equal("Médio", tweak.RiskLevel);
        Assert.False(string.IsNullOrWhiteSpace(tweak.Description));
        Assert.False(string.IsNullOrWhiteSpace(tweak.DescriptionEn));
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
    public void RegistryStateLedger_FailedAtomicSave_PreservesPreviousVerifiedLedger()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        var first = RegistryStateLedger.RecordAppliedTweakState(def, "1", RegistryValueKind.DWord, false, false, "0");
        RegistryStateLedger.ProtectedTokenWriter = (path, json) =>
            new PCControlUltimate.Security.TokenProtectionResult(false, false, false, "simulated protected-write failure");

        var ex = Assert.Throws<IOException>(() =>
            RegistryStateLedger.RecordAppliedTweakState(def, "0", RegistryValueKind.DWord, false, false, "1"));

        Assert.Contains("simulated protected-write failure", ex.Message);
        var preserved = Assert.Single(RegistryStateLedger.GetAllEntries());
        Assert.Equal(first.OperationId, preserved.OperationId);
        Assert.Equal("1", preserved.OriginalRawValue);
        Assert.False(preserved.IsRestored);
        Assert.Empty(Directory.GetFiles(_testLedgerDirectory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_testLedgerDirectory, "*.bak"));
    }

    [Fact]
    public void RegistryStateLedger_FailedRestoredMarkerSave_DoesNotMarkOperationRestored()
    {
        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        var entry = RegistryStateLedger.RecordAppliedTweakState(def, "1", RegistryValueKind.DWord, false, false, "0");
        RegistryStateLedger.ProtectedTokenWriter = (path, json) =>
            new PCControlUltimate.Security.TokenProtectionResult(false, false, false, "simulated marker save failure");

        Assert.Throws<IOException>(() => RegistryStateLedger.MarkOperationRestored(entry.OperationId));

        RegistryStateLedger.ProtectedTokenWriter = PCControlUltimate.Security.IntegrityVerifier.SaveProtectedSessionTokenWithReport;
        var persisted = Assert.Single(RegistryStateLedger.GetAllEntries());
        Assert.False(persisted.IsRestored);
        Assert.Null(persisted.RestoredAtUtc);
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
