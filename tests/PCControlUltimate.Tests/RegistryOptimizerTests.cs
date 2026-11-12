using System;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PCControlUltimate.Windows.Services;
using Xunit;

[assembly: SupportedOSPlatform("windows")]

namespace PCControlUltimate.Tests;

[SupportedOSPlatform("windows")]
public class RegistryOptimizerTests
{
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
    public void RestoreCatalogTweak_ClientCannotChooseArbitraryOriginalValue_BackendUsesLedgerInstead()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);
        RegistryStateLedger.ClearLedgerForTesting();

        var def = RegistryOptimizer.Catalog["perf-game-dvr"];
        probe.SetValue(def.Hive, def.SubKeyPath, def.ValueName, 1, RegistryValueKind.DWord);

        // Apply tweak to HKCU\System\GameConfigStore\GameDVR_Enabled
        var applyResult = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "0");
        Assert.True(applyResult.Success);
        Assert.NotNull(applyResult.OperationId);

        // Even if caller passes a malicious/arbitrary originalRawValue ("999999") to the legacy method,
        // the backend ignores the caller-supplied value and restores strictly from the DPAPI+HMAC ledger!
        var restoreResult = RegistryOptimizer.RestoreCatalogTweak(
            "perf-game-dvr",
            wasInitiallyAbsent: false,
            originalRawValue: "999999",
            originalKind: RegistryValueKind.String,
            keyCreatedByApp: false
        );

        Assert.True(restoreResult.Success);
        Assert.Equal("1", restoreResult.VerifiedCurrentValue);
        Assert.NotEqual("999999", restoreResult.VerifiedCurrentValue);
    }

    [Fact]
    public void RestoreCatalogTweakFromTrustedSource_WhenValueWasInitiallyAbsent_RemovesValueAndVerifiesAbsence()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);
        RegistryStateLedger.ClearLedgerForTesting();

        var def = RegistryOptimizer.Catalog["perf-fso-flags"];
        probe.EnsureSubKeyExists(def.Hive, def.SubKeyPath);
        probe.RemoveValue(def.Hive, def.SubKeyPath, def.ValueName);

        var applyResult = RegistryOptimizer.ApplyCatalogTweak("perf-fso-flags", "2");
        Assert.True(applyResult.Success);
        Assert.True(applyResult.WasInitiallyAbsent);

        var activeReport = RegistryOptimizer.InspectCatalogTweak("perf-fso-flags");
        Assert.Equal(TweakInspectionState.Active, activeReport.State);

        var restoreResult = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("perf-fso-flags", applyResult.OperationId);
        Assert.True(restoreResult.Success);
        Assert.Equal("REMOVED_ABSENT_VALUE", restoreResult.VerifiedCurrentValue);

        var postRestoreReport = RegistryOptimizer.InspectCatalogTweak("perf-fso-flags");
        Assert.Equal(TweakInspectionState.NotConfigured, postRestoreReport.State);
    }

    [Fact]
    public void InspectCatalogTweak_ExistingValues_DistinguishesActiveAndInactive()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);

        var def = RegistryOptimizer.Catalog["perf-game-dvr"];

        // 1. Default reference value (1) -> INACTIVE
        probe.SetValue(def.Hive, def.SubKeyPath, def.ValueName, 1, RegistryValueKind.DWord);
        var inactiveReport = RegistryOptimizer.InspectCatalogTweak("perf-game-dvr");
        Assert.Equal(TweakInspectionState.Inactive, inactiveReport.State);
        Assert.Equal("INATIVO", inactiveReport.StateBadgePt);
        Assert.Equal("1", inactiveReport.CurrentRawValue);
        Assert.Equal(RegistryValueKind.DWord, inactiveReport.ActualValueKind);

        // 2. Recommended value (0) with matching DWord kind -> ACTIVE
        probe.SetValue(def.Hive, def.SubKeyPath, def.ValueName, 0, RegistryValueKind.DWord);
        var activeReport = RegistryOptimizer.InspectCatalogTweak("perf-game-dvr");
        Assert.Equal(TweakInspectionState.Active, activeReport.State);
        Assert.Equal("ATIVO", activeReport.StateBadgePt);
        Assert.Equal("0", activeReport.CurrentRawValue);
    }

    [Fact]
    public void InspectCatalogTweak_MissingKeyOrMissingValue_ReturnsNotConfigured()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);

        var def = RegistryOptimizer.Catalog["start-edge-preload"];

        // 1. Subkey does not exist at all
        probe.RemoveSubKey(def.Hive, def.SubKeyPath);
        var keyMissingReport = RegistryOptimizer.InspectCatalogTweak("start-edge-preload");
        Assert.Equal(TweakInspectionState.NotConfigured, keyMissingReport.State);
        Assert.False(keyMissingReport.KeyExists);
        Assert.False(keyMissingReport.ValueExists);
        Assert.Equal("NÃO CONFIGURADO", keyMissingReport.StateBadgePt);

        // 2. Subkey exists, but value does not exist
        probe.EnsureSubKeyExists(def.Hive, def.SubKeyPath);
        var valueMissingReport = RegistryOptimizer.InspectCatalogTweak("start-edge-preload");
        Assert.Equal(TweakInspectionState.NotConfigured, valueMissingReport.State);
        Assert.True(valueMissingReport.KeyExists);
        Assert.False(valueMissingReport.ValueExists);
    }

    [Fact]
    public void InspectCatalogTweak_WrongValueKind_EvenWithMatchingTextRepresentation_ReturnsWrongTypeAndNeverActive()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);

        var def = RegistryOptimizer.Catalog["perf-game-dvr"]; // expects REG_DWORD = 0

        // Value is "0", which matches RecommendedValue textually, but stored as REG_SZ (String) instead of REG_DWORD
        probe.SetValue(def.Hive, def.SubKeyPath, def.ValueName, "0", RegistryValueKind.String);

        var report = RegistryOptimizer.InspectCatalogTweak("perf-game-dvr");
        Assert.Equal(TweakInspectionState.WrongType, report.State);
        Assert.NotEqual(TweakInspectionState.Active, report.State);
        Assert.Equal("TIPO INCORRETO", report.StateBadgePt);
        Assert.Equal(RegistryValueKind.String, report.ActualValueKind);
        Assert.Equal(RegistryValueKind.DWord, report.ExpectedValueKind);
    }

    [Fact]
    public void InspectCatalogTweak_CustomValue_ReturnsCustomState()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);

        var def = RegistryOptimizer.Catalog["perf-hpet"]; // Win32PrioritySeparation: Recommended=38, Default=2
        probe.SetValue(def.Hive, def.SubKeyPath, def.ValueName, 24, RegistryValueKind.DWord);

        var report = RegistryOptimizer.InspectCatalogTweak("perf-hpet");
        Assert.Equal(TweakInspectionState.Custom, report.State);
        Assert.Equal("PERSONALIZADO", report.StateBadgePt);
        Assert.Equal("24", report.CurrentRawValue);
    }

    [Fact]
    public void InspectCatalogTweak_AccessDeniedAndReadErrorAndIncompatible_ReturnExactStates()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);

        // 1. Access Denied on HKLM subkey
        var defHpet = RegistryOptimizer.Catalog["perf-hpet"];
        probe.SetAccessDenied(defHpet.Hive, defHpet.SubKeyPath);
        var deniedReport = RegistryOptimizer.InspectCatalogTweak("perf-hpet");
        Assert.Equal(TweakInspectionState.AccessDenied, deniedReport.State);
        Assert.Equal("ACESSO NEGADO", deniedReport.StateBadgePt);

        // 2. Read Error on subkey
        var defSysMain = RegistryOptimizer.Catalog["serv-sysmain"];
        probe.SetReadError(defSysMain.Hive, defSysMain.SubKeyPath);
        var errorReport = RegistryOptimizer.InspectCatalogTweak("serv-sysmain");
        Assert.Equal(TweakInspectionState.ReadError, errorReport.State);
        Assert.Equal("ERRO DE LEITURA", errorReport.StateBadgePt);

        // 3. Incompatible Windows build (bg-cortana-news is Windows 10 taskbar feed, incompatible on Win11 Build 22631 when absent)
        probe.CurrentWindowsBuild = 22631;
        var win10FeedReport = RegistryOptimizer.InspectCatalogTweak("bg-cortana-news");
        Assert.Equal(TweakInspectionState.Incompatible, win10FeedReport.State);
        Assert.Equal("INCOMPATÍVEL", win10FeedReport.StateBadgePt);
    }

    [Fact]
    public void ApplyAndRestoreCatalogTweak_VerifiesPostOperationStateTransitions_AndDetectsUnconfirmedOperations()
    {
        var probe = new SimulatedRegistryKeyProbe();
        using var scope = RegistryOptimizer.UseProbeScope(probe);
        RegistryStateLedger.ClearLedgerForTesting();

        var def = RegistryOptimizer.Catalog["bg-telemetry"]; // AllowTelemetry: Recommended=1, Default=3
        probe.SetValue(def.Hive, def.SubKeyPath, def.ValueName, 3, RegistryValueKind.DWord);

        // Initial state must be Inactive (3)
        Assert.Equal(TweakInspectionState.Inactive, RegistryOptimizer.InspectCatalogTweak("bg-telemetry").State);

        // If write is silently ignored by OS/policy, ApplyCatalogTweak must fail post-write verification
        probe.SimulateWriteIgnore = true;
        var failedApply = RegistryOptimizer.ApplyCatalogTweak("bg-telemetry", "1");
        Assert.False(failedApply.Success);
        Assert.Equal(TweakInspectionState.Inactive, RegistryOptimizer.InspectCatalogTweak("bg-telemetry").State);

        // Normal write succeeds and transitions state to Active (1)
        probe.SimulateWriteIgnore = false;
        var okApply = RegistryOptimizer.ApplyCatalogTweak("bg-telemetry", "1");
        Assert.True(okApply.Success);
        Assert.Equal(TweakInspectionState.Active, RegistryOptimizer.InspectCatalogTweak("bg-telemetry").State);

        // Restore transitions state back to Inactive (3)
        var okRestore = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource("bg-telemetry", okApply.OperationId);
        Assert.True(okRestore.Success);
        var postRestore = RegistryOptimizer.InspectCatalogTweak("bg-telemetry");
        Assert.Equal(TweakInspectionState.Inactive, postRestore.State);
        Assert.Equal("3", postRestore.CurrentRawValue);
    }

    [Fact]
    public void CatalogAudit_All23Entries_HaveAccurateMetadataAndCorrectedMisidentifications()
    {
        Assert.Equal(23, RegistryOptimizer.Catalog.Count);

        // Verify the 6 previously misidentified tweaks now document their real Registry target honestly
        var hpet = RegistryOptimizer.Catalog["perf-hpet"];
        Assert.Equal("Win32PrioritySeparation", hpet.ValueName);
        Assert.Contains("Win32PrioritySeparation", hpet.FriendlyNamePt);
        Assert.Contains("não o timer HPET", hpet.WhatItDoesPt);

        var nagle = RegistryOptimizer.Catalog["perf-tcp-nagle"];
        Assert.Equal("NetworkThrottlingIndex", nagle.ValueName);
        Assert.Contains("NetworkThrottlingIndex", nagle.FriendlyNamePt);
        Assert.Contains("não o algoritmo TCP Nagle", nagle.WhatItDoesPt);

        var autoTuning = RegistryOptimizer.Catalog["net-tcp-autotuning"];
        Assert.Equal("TcpMaxDataRetransmissions", autoTuning.ValueName);
        Assert.False(autoTuning.IsRecommendedForApply);
        Assert.Equal(TweakRecommendationStatus.NotRecommendedOrDefault, autoTuning.RecommendationStatus);

        var pcie = RegistryOptimizer.Catalog["power-pcie-link"];
        Assert.Equal("Scheduling Category", pcie.ValueName);
        Assert.Equal("Gaming", pcie.Category);
        Assert.Contains("MMCSS", pcie.FriendlyNamePt);

        var standby = RegistryOptimizer.Catalog["mem-standby-clean"];
        Assert.Equal("LargeSystemCache", standby.ValueName);
        Assert.False(standby.IsRecommendedForApply);

        var ultimate = RegistryOptimizer.Catalog["power-ultimate-plan"];
        Assert.Equal("PowerThrottlingOff", ultimate.ValueName);
        Assert.Contains("PowerThrottlingOff", ultimate.FriendlyNamePt);

        // Verify every catalog entry has complete PT-BR and EN-US technical explanations
        foreach (var entry in RegistryOptimizer.Catalog.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.FriendlyNamePt), $"Missing FriendlyNamePt for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.FriendlyNameEn), $"Missing FriendlyNameEn for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.WhatItDoesPt), $"Missing WhatItDoesPt for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.WhatItDoesEn), $"Missing WhatItDoesEn for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.ProblemSolvedPt), $"Missing ProblemSolvedPt for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedBenefitPt), $"Missing ExpectedBenefitPt for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.PotentialSideEffectsPt), $"Missing PotentialSideEffectsPt for {entry.TweakId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.DefaultWindowsBehaviorPt), $"Missing DefaultWindowsBehaviorPt for {entry.TweakId}");
        }
    }
}
