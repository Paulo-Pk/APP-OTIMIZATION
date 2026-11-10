using System;
using Microsoft.Win32;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

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
        // Regression test: StartsWith prefix matching was removed; only exact catalog subkeys are allowed
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
        // "perf-game-dvr" only allows "0" or "1"
        var result = RegistryOptimizer.ApplyCatalogTweak("perf-game-dvr", "999999");

        Assert.False(result.Success);
        Assert.Contains("não está na lista de valores permitidos", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryApplyTypedValue_UncatalogedValueNameInsideAllowedSubkey_MustBeRejected()
    {
        // Subkey is in AllowedSubkeys, but "ArbitraryUncatalogedValue" is NOT in Catalog
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
}

