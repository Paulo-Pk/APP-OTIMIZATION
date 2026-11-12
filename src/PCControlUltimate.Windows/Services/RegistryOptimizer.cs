using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

[assembly: SupportedOSPlatform("windows")]

namespace PCControlUltimate.Windows.Services;

/// <summary>
/// Explicit real-time Registry inspection states (§2).
/// Distinguishes between Active, Inactive (known reference value), Custom, NotConfigured (key/value absent),
/// AccessDenied, WrongType (value exists with unexpected RegistryValueKind), Incompatible, and ReadError.
/// </summary>
public enum TweakInspectionState
{
    Active,
    Inactive,
    Custom,
    NotConfigured,
    AccessDenied,
    WrongType,
    Incompatible,
    ReadError
}

/// <summary>
/// Technical audit classification for each catalog entry (§1).
/// </summary>
public enum TweakRecommendationStatus
{
    Recommended,
    Conditional,
    NotRecommendedOrDefault
}

/// <summary>
/// Differentiates how Windows behaves by default and when a key or value is absent from the Registry.
/// Prevents falsely labeling every absent key or value as a universal "Windows Default".
/// </summary>
public enum TweakDefaultBehaviorKind
{
    /// <summary>
    /// Value exists by default on standard Windows installations with a known documented default.
    /// </summary>
    KnownDocumentedValue,

    /// <summary>
    /// Key or value is absent by default in the Registry, with a known documented internal Windows default behavior.
    /// </summary>
    AbsentWithKnownDefault,

    /// <summary>
    /// Absence or initial value depends on Windows edition/version, OOBE choices, hardware/driver, or user settings.
    /// </summary>
    EditionOrConfigDependent,

    /// <summary>
    /// Key or value is absent by default without a universal standalone default value or sufficiently determined independent behavior.
    /// </summary>
    UndeterminedWhenAbsent
}

/// <summary>
/// Outcome of a low-level Registry key/value probe.
/// </summary>
public enum RegistryProbeReadOutcome
{
    ValueFound,
    Success = ValueFound,
    KeyNotFound,
    ValueNotFound,
    AccessDenied,
    IncompatibleEnvironment,
    ReadError
}

/// <summary>
/// Raw result returned by an IRegistryKeyProbe before catalog semantic classification.
/// </summary>
public record RegistryProbeReadResult(
    RegistryProbeReadOutcome Outcome,
    object? RawValue = null,
    RegistryValueKind ActualValueKind = RegistryValueKind.Unknown,
    string? SerializedValue = null,
    string? ErrorMessage = null
)
{
    public RegistryValueKind ValueKind => ActualValueKind;
}

/// <summary>
/// Injectable abstraction for reading and writing Registry keys so unit tests can deterministically
/// verify all 8 states (including WrongType, AccessDenied, NotConfigured, and unconfirmed writes/restores)
/// without modifying the user's real Windows Registry.
/// </summary>
public interface IRegistryKeyProbe
{
    bool IsSupportedPlatform { get; }
    int CurrentWindowsBuild { get; }
    RegistryProbeReadResult ReadValue(string hive, string subKeyPath, string valueName);
    bool TryWriteValue(string hive, string subKeyPath, string valueName, object typedValue, RegistryValueKind valueKind, out bool keyCreated, out string? errorMessage, out bool isAccessDenied);
    bool TryDeleteValue(string hive, string subKeyPath, string valueName, bool deleteEmptyKeyIfCreatedByApp, out string? errorMessage, out bool isAccessDenied);
}

/// <summary>
/// Production Win32 Registry implementation of IRegistryKeyProbe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32RegistryKeyProbe : IRegistryKeyProbe
{
    public static Win32RegistryKeyProbe Instance { get; } = new();

    public bool IsSupportedPlatform => OperatingSystem.IsWindows();

    public int CurrentWindowsBuild => OperatingSystem.IsWindows() ? Environment.OSVersion.Version.Build : 0;

    public RegistryProbeReadResult ReadValue(string hive, string subKeyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.IncompatibleEnvironment,
                ErrorMessage: "Leitura real do Registro requer plataforma Windows nativa."
            );
        }

        try
        {
            RegistryKey root = RegistryOptimizer.GetRootKey(hive);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key == null)
            {
                return new RegistryProbeReadResult(RegistryProbeReadOutcome.KeyNotFound);
            }

            // Check if value name actually exists in the key (handles empty string or null values accurately)
            string[] valueNames = key.GetValueNames();
            bool valueExists = valueNames.Any(n => string.Equals(n, valueName, StringComparison.OrdinalIgnoreCase));
            object? raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

            if (!valueExists && raw == null)
            {
                return new RegistryProbeReadResult(RegistryProbeReadOutcome.ValueNotFound);
            }

            RegistryValueKind actualKind = key.GetValueKind(valueName);
            string? serialized = RegistryOptimizer.SerializeRegistryValue(raw, actualKind);

            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.ValueFound,
                RawValue: raw,
                ActualValueKind: actualKind,
                SerializedValue: serialized
            );
        }
        catch (SecurityException ex)
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.AccessDenied,
                ErrorMessage: $"Acesso de segurança negado a {hive}\\{subKeyPath}: {ex.Message}"
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.AccessDenied,
                ErrorMessage: $"Permissão negada ao consultar {hive}\\{subKeyPath}: {ex.Message}"
            );
        }
        catch (Exception ex)
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.ReadError,
                ErrorMessage: $"Erro técnico ao consultar {hive}\\{subKeyPath}\\{valueName}: {ex.Message}"
            );
        }
    }

    public bool TryWriteValue(
        string hive,
        string subKeyPath,
        string valueName,
        object typedValue,
        RegistryValueKind valueKind,
        out bool keyCreated,
        out string? errorMessage,
        out bool isAccessDenied)
    {
        keyCreated = false;
        errorMessage = null;
        isAccessDenied = false;

        if (!OperatingSystem.IsWindows())
        {
            errorMessage = "Operação de Registro requer plataforma Windows nativa.";
            return false;
        }

        try
        {
            RegistryKey root = RegistryOptimizer.GetRootKey(hive);
            using (var checkKey = root.OpenSubKey(subKeyPath, writable: false))
            {
                if (checkKey == null)
                {
                    keyCreated = true;
                }
            }

            using (RegistryKey writeKey = root.CreateSubKey(subKeyPath, writable: true))
            {
                writeKey.SetValue(valueName, typedValue, valueKind);
                writeKey.Flush();
            }

            return true;
        }
        catch (SecurityException ex)
        {
            isAccessDenied = true;
            errorMessage = $"Acesso de segurança negado (Elevação UAC necessária para {hive}): {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            isAccessDenied = true;
            errorMessage = $"Privilégios insuficientes para {hive}. Execute como Administrador (UAC): {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Erro ao gravar no Registro: {ex.Message}";
            return false;
        }
    }

    public bool TryDeleteValue(
        string hive,
        string subKeyPath,
        string valueName,
        bool deleteEmptyKeyIfCreatedByApp,
        out string? errorMessage,
        out bool isAccessDenied)
    {
        errorMessage = null;
        isAccessDenied = false;

        if (!OperatingSystem.IsWindows())
        {
            errorMessage = "Operação de Registro requer plataforma Windows nativa.";
            return false;
        }

        try
        {
            RegistryKey root = RegistryOptimizer.GetRootKey(hive);
            using (RegistryKey? writeKey = root.OpenSubKey(subKeyPath, writable: true))
            {
                if (writeKey != null && writeKey.GetValue(valueName) != null)
                {
                    writeKey.DeleteValue(valueName, throwOnMissingValue: false);
                    writeKey.Flush();
                }
            }

            if (deleteEmptyKeyIfCreatedByApp)
            {
                using (RegistryKey? verifyKey = root.OpenSubKey(subKeyPath, writable: false))
                {
                    if (verifyKey != null && verifyKey.ValueCount == 0 && verifyKey.SubKeyCount == 0)
                    {
                        verifyKey.Close();
                        root.DeleteSubKey(subKeyPath, throwOnMissingSubKey: false);
                    }
                }
            }

            return true;
        }
        catch (SecurityException ex)
        {
            isAccessDenied = true;
            errorMessage = $"Acesso de segurança negado ao remover valor em {hive}: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            isAccessDenied = true;
            errorMessage = $"Privilégios insuficientes para remover valor em {hive}: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Falha ao restaurar/remover valor no Registro: {ex.Message}";
            return false;
        }
    }
}

/// <summary>
/// Isolated, deterministic in-memory Registry probe for xUnit testing.
/// Supports simulating existing values, missing keys, missing values, wrong RegistryValueKind,
/// custom values, access denied, OS incompatibility, read errors, and unconfirmed write/restore scenarios.
/// </summary>
public sealed class SimulatedRegistryKeyProbe : IRegistryKeyProbe
{
    private readonly Dictionary<string, Dictionary<string, (object RawValue, RegistryValueKind Kind)>> _keys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _accessDeniedTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _readErrorTargets = new(StringComparer.OrdinalIgnoreCase);

    private bool _triggerReadFailureOnNextRead;

    public bool IsSupportedPlatform { get; set; } = true;
    public int CurrentWindowsBuild { get; set; } = 22631; // Windows 11 23H2 default for simulation
    public bool SimulateWriteIgnore { get; set; }
    public bool SimulateDeleteIgnore { get; set; }
    public RegistryProbeReadOutcome? SimulateReadFailureAfterMutation { get; set; }

    private static string NormalizeKeyId(string hive, string subKeyPath) =>
        $"{hive.Trim().ToUpperInvariant()}\\{RegistryOptimizer.NormalizeSubKeyPath(subKeyPath)}";

    private static string NormalizeValueId(string hive, string subKeyPath, string valueName) =>
        $"{NormalizeKeyId(hive, subKeyPath)}::{valueName.Trim()}";

    public bool SubKeyExists(string hive, string subKeyPath) =>
        _keys.ContainsKey(NormalizeKeyId(hive, subKeyPath));

    public void EnsureSubKeyExists(string hive, string subKeyPath)
    {
        string keyId = NormalizeKeyId(hive, subKeyPath);
        if (!_keys.ContainsKey(keyId))
        {
            _keys[keyId] = new Dictionary<string, (object RawValue, RegistryValueKind Kind)>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetValue(string hive, string subKeyPath, string valueName, object rawValue, RegistryValueKind kind)
    {
        string keyId = NormalizeKeyId(hive, subKeyPath);
        if (!_keys.TryGetValue(keyId, out var values))
        {
            values = new Dictionary<string, (object RawValue, RegistryValueKind Kind)>(StringComparer.OrdinalIgnoreCase);
            _keys[keyId] = values;
        }
        values[valueName.Trim()] = (rawValue, kind);
    }

    public void RemoveSubKey(string hive, string subKeyPath)
    {
        _keys.Remove(NormalizeKeyId(hive, subKeyPath));
    }

    public void RemoveValue(string hive, string subKeyPath, string valueName)
    {
        string keyId = NormalizeKeyId(hive, subKeyPath);
        if (_keys.TryGetValue(keyId, out var values))
        {
            values.Remove(valueName.Trim());
        }
    }

    public void SetAccessDenied(string hive, string subKeyPath, string? valueName = null)
    {
        if (string.IsNullOrWhiteSpace(valueName))
            _accessDeniedTargets.Add(NormalizeKeyId(hive, subKeyPath));
        else
            _accessDeniedTargets.Add(NormalizeValueId(hive, subKeyPath, valueName));
    }

    public void SetReadError(string hive, string subKeyPath, string? valueName = null)
    {
        if (string.IsNullOrWhiteSpace(valueName))
            _readErrorTargets.Add(NormalizeKeyId(hive, subKeyPath));
        else
            _readErrorTargets.Add(NormalizeValueId(hive, subKeyPath, valueName));
    }

    public RegistryProbeReadResult ReadValue(string hive, string subKeyPath, string valueName)
    {
        if (_triggerReadFailureOnNextRead && SimulateReadFailureAfterMutation.HasValue)
        {
            _triggerReadFailureOnNextRead = false;
            return new RegistryProbeReadResult(
                SimulateReadFailureAfterMutation.Value,
                ErrorMessage: $"Falha simulada ({SimulateReadFailureAfterMutation.Value}) na releitura pós-operação."
            );
        }

        if (!IsSupportedPlatform)
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.IncompatibleEnvironment,
                ErrorMessage: "Plataforma simulada incompatível."
            );
        }

        string keyId = NormalizeKeyId(hive, subKeyPath);
        string valId = NormalizeValueId(hive, subKeyPath, valueName);

        if (_accessDeniedTargets.Contains(keyId) || _accessDeniedTargets.Contains(valId))
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.AccessDenied,
                ErrorMessage: $"Acesso negado simulado para {keyId}\\{valueName}."
            );
        }

        if (_readErrorTargets.Contains(keyId) || _readErrorTargets.Contains(valId))
        {
            return new RegistryProbeReadResult(
                RegistryProbeReadOutcome.ReadError,
                ErrorMessage: $"Erro de leitura simulado para {keyId}\\{valueName}."
            );
        }

        if (!_keys.TryGetValue(keyId, out var values))
        {
            return new RegistryProbeReadResult(RegistryProbeReadOutcome.KeyNotFound);
        }

        if (!values.TryGetValue(valueName.Trim(), out var entry))
        {
            return new RegistryProbeReadResult(RegistryProbeReadOutcome.ValueNotFound);
        }

        string? serialized = RegistryOptimizer.SerializeRegistryValue(entry.RawValue, entry.Kind);
        return new RegistryProbeReadResult(
            RegistryProbeReadOutcome.ValueFound,
            RawValue: entry.RawValue,
            ActualValueKind: entry.Kind,
            SerializedValue: serialized
        );
    }

    public bool TryWriteValue(
        string hive,
        string subKeyPath,
        string valueName,
        object typedValue,
        RegistryValueKind valueKind,
        out bool keyCreated,
        out string? errorMessage,
        out bool isAccessDenied)
    {
        keyCreated = false;
        errorMessage = null;
        isAccessDenied = false;

        string keyId = NormalizeKeyId(hive, subKeyPath);
        string valId = NormalizeValueId(hive, subKeyPath, valueName);

        if (_accessDeniedTargets.Contains(keyId) || _accessDeniedTargets.Contains(valId))
        {
            isAccessDenied = true;
            errorMessage = $"Acesso negado ao gravar em {keyId}\\{valueName}.";
            return false;
        }

        if (!_keys.ContainsKey(keyId))
        {
            keyCreated = true;
        }

        if (!SimulateWriteIgnore)
        {
            SetValue(hive, subKeyPath, valueName, typedValue, valueKind);
        }

        if (SimulateReadFailureAfterMutation.HasValue)
        {
            _triggerReadFailureOnNextRead = true;
        }

        return true;
    }

    public bool TryDeleteValue(
        string hive,
        string subKeyPath,
        string valueName,
        bool deleteEmptyKeyIfCreatedByApp,
        out string? errorMessage,
        out bool isAccessDenied)
    {
        errorMessage = null;
        isAccessDenied = false;

        string keyId = NormalizeKeyId(hive, subKeyPath);
        string valId = NormalizeValueId(hive, subKeyPath, valueName);

        if (_accessDeniedTargets.Contains(keyId) || _accessDeniedTargets.Contains(valId))
        {
            isAccessDenied = true;
            errorMessage = $"Acesso negado ao remover {keyId}\\{valueName}.";
            return false;
        }

        if (!SimulateDeleteIgnore && _keys.TryGetValue(keyId, out var values))
        {
            values.Remove(valueName.Trim());
            if (deleteEmptyKeyIfCreatedByApp && values.Count == 0)
            {
                bool hasChildSubKeys = _keys.Keys.Any(k =>
                    k.StartsWith(keyId + "\\", StringComparison.OrdinalIgnoreCase));
                if (!hasChildSubKeys)
                {
                    _keys.Remove(keyId);
                }
            }
        }

        if (SimulateReadFailureAfterMutation.HasValue)
        {
            _triggerReadFailureOnNextRead = true;
        }

        return true;
    }
}

/// <summary>
/// Rich, structured report returned by RegistryOptimizer.InspectCatalogTweak (§2).
/// </summary>
public record TweakStateReport(
    string TweakId,
    TweakInspectionState State,
    string StateDisplayPt,
    string StateDisplayEn,
    bool KeyExists,
    bool ValueExists,
    string? CurrentRawValue,
    RegistryValueKind? ActualValueKind,
    RegistryValueKind ExpectedValueKind,
    string RecommendedValue,
    string DefaultOriginValue,
    bool IsTypeMatch,
    bool IsRecommendedValueMatch,
    string DiagnosticMessagePt,
    string DiagnosticMessageEn,
    TweakDefaultBehaviorKind DefaultBehaviorKind = TweakDefaultBehaviorKind.KnownDocumentedValue,
    bool IsUndeterminable = false,
    string? StateClassificationPt = null,
    string? StateClassificationEn = null,
    string? VerificationSummaryPt = null,
    string? VerificationSummaryEn = null
)
{
    public string StateBadgePt => StateDisplayPt.ToUpperInvariant();
    public string StateBadgeEn => StateDisplayEn.ToUpperInvariant();
    public string ResolvedClassificationPt => !string.IsNullOrWhiteSpace(StateClassificationPt) ? StateClassificationPt : StateDisplayPt;
    public string ResolvedClassificationEn => !string.IsNullOrWhiteSpace(StateClassificationEn) ? StateClassificationEn : StateDisplayEn;
}

public record TweakDefinition(
    string TweakId,
    string Hive,
    string SubKeyPath,
    string ValueName,
    RegistryValueKind ValueKind,
    string RecommendedValue,
    string DefaultOriginValue,
    IReadOnlyList<string> AllowedValues,
    string Description,
    string DescriptionEn,
    string Category,
    string RiskLevel,
    string? FriendlyNamePt = null,
    string? FriendlyNameEn = null,
    string? WhatItDoesPt = null,
    string? WhatItDoesEn = null,
    string? ProblemSolvedPt = null,
    string? ProblemSolvedEn = null,
    string? ExpectedBenefitPt = null,
    string? ExpectedBenefitEn = null,
    string? PotentialSideEffectsPt = null,
    string? PotentialSideEffectsEn = null,
    bool RequiresReboot = false,
    string? HardwareOrOsDependencyPt = null,
    string? HardwareOrOsDependencyEn = null,
    string? DefaultWindowsBehaviorPt = null,
    string? DefaultWindowsBehaviorEn = null,
    TweakRecommendationStatus RecommendationStatus = TweakRecommendationStatus.Recommended,
    bool IsRecommendedForApply = true,
    int MinimumWindowsBuild = 19041,
    int? MaximumWindowsBuild = null,
    TweakDefaultBehaviorKind DefaultBehaviorKind = TweakDefaultBehaviorKind.KnownDocumentedValue,
    string? BenefitConditionalityPt = null,
    string? BenefitConditionalityEn = null
)
{
    public string TargetRawValue => RecommendedValue;
    public IReadOnlyList<string> AllowedRawValues => AllowedValues;

    public string DisplayTitlePt => !string.IsNullOrWhiteSpace(FriendlyNamePt) ? FriendlyNamePt : Description;
    public string DisplayTitleEn => !string.IsNullOrWhiteSpace(FriendlyNameEn) ? FriendlyNameEn : DescriptionEn;
}

public record RegistryAuditLogEntry(
    string TimestampUtc,
    string WindowsUser,
    string Operation,
    string TweakId,
    string KeyPath,
    string ValueName,
    string ValueKind,
    string? PreviousValue,
    string? RequestedValue,
    string? VerifiedValue,
    bool Success,
    string? ErrorMessage
);

public record RegistryOperationResult(
    bool Success,
    string? PreviousValue,
    string? VerifiedValue,
    string ValueKind,
    bool WasInitiallyAbsent,
    bool KeyCreatedByApp,
    string? ErrorMessage,
    string? TweakId = null,
    string? OperationId = null,
    string? ErrorCode = null,
    RegistryValueKind? PreviousKind = null
)
{
    public string? VerifiedCurrentValue => VerifiedValue;
    public string? PreviousRawValue => PreviousValue;
    public string Message => ErrorMessage ?? (Success ? $"OK ({VerifiedValue ?? "Applied"})" : "Falha na operação de Registro.");
}

[SupportedOSPlatform("windows")]
public static class RegistryOptimizer
{
    private static readonly ConcurrentQueue<RegistryAuditLogEntry> _auditHistory = new();
    private static IRegistryKeyProbe? _AmbientTestProbe;

    /// <summary>
    /// Allows unit tests to inject a deterministic IRegistryKeyProbe for all RegistryOptimizer operations
    /// within a disposable scope without modifying the real Windows Registry.
    /// </summary>
    public static IDisposable UseProbeScope(IRegistryKeyProbe probe)
    {
        _AmbientTestProbe = probe ?? throw new ArgumentNullException(nameof(probe));
        return new ProbeScopeReset();
    }

    public static IRegistryKeyProbe ActiveProbe => _AmbientTestProbe ?? Win32RegistryKeyProbe.Instance;

    private sealed class ProbeScopeReset : IDisposable
    {
        public void Dispose() => _AmbientTestProbe = null;
    }

    public static string AuditLogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "Logs",
        "registry_audit.log"
    );

    private static readonly HashSet<string> _allowedSubkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
        @"SYSTEM\CurrentControlSet\Control\PriorityControl",
        @"System\GameConfigStore",
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
        @"SOFTWARE\Policies\Microsoft\Edge",
        @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
        @"Software\Microsoft\Windows\CurrentVersion\Feeds",
        @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
        @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings",
        @"SYSTEM\CurrentControlSet\Services\SysMain",
        @"SYSTEM\CurrentControlSet\Services\Spooler",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
        @"SYSTEM\CurrentControlSet\Control\FileSystem",
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
        @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo"
    };

    public static IReadOnlyCollection<string> AllowedSubkeys => _allowedSubkeys;

    // Strictly forbidden security/boot paths that must never be touched under any circumstance
    private static readonly string[] ForbiddenPathFragments = new[]
    {
        "Windows Defender",
        "WinDefend",
        "WindowsFirewall",
        "SharedAccess\\Parameters\\FirewallPolicy",
        "CurrentVersion\\Run",
        "CurrentVersion\\RunOnce",
        "WindowsUpdate\\AU",
        "SecurityHealth"
    };

    /// <summary>
    /// Closed, immutable catalog of authorized Registry tweaks (§1 Audited & Corrected).
    /// All TweakIds are preserved for compatibility with DPAPI Ledger, SQLite, and verified SHA-256 snapshots.
    /// </summary>
    public static IReadOnlyDictionary<string, TweakDefinition> Catalog { get; } = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["perf-mmcss"] = new(
            TweakId: "perf-mmcss",
            Hive: "HKLM",
            SubKeyPath: @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            ValueName: "SystemResponsiveness",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "10",
            DefaultOriginValue: "20",
            AllowedValues: new[] { "10", "20" },
            Description: "Reserva de CPU do Agendador Multimídia (SystemResponsiveness = 10%)",
            DescriptionEn: "Multimedia Class Scheduler CPU Reservation (SystemResponsiveness = 10%)",
            Category: "Performance",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Reserva de CPU do Agendador Multimídia (MMCSS SystemResponsiveness)",
            FriendlyNameEn: "Multimedia Scheduler CPU Reservation (MMCSS SystemResponsiveness)",
            WhatItDoesPt: "Define o percentual de tempo de CPU reservado pelo Multimedia Class Scheduler Service (MMCSS) para tarefas de segundo plano de baixa prioridade durante cargas multimídia e jogos.",
            WhatItDoesEn: "Sets the percentage of CPU time reserved by the Multimedia Class Scheduler Service (MMCSS) for low-priority background tasks during multimedia and gaming workloads.",
            ProblemSolvedPt: "Reduz a fatia reservada a tarefas em segundo plano de 20% (padrão) para 10% quando threads multimídia estão ativas.",
            ProblemSolvedEn: "Lowers the slice reserved for background tasks from 20% (default) to 10% when multimedia threads are active.",
            ExpectedBenefitPt: "Permite que threads registradas no MMCSS utilizem até 90% dos ciclos de CPU em momentos de contenção intensa (não garante redução automática de DPC latency).",
            ExpectedBenefitEn: "Allows MMCSS-registered threads to utilize up to 90% of CPU cycles during heavy contention (does not guarantee automatic DPC latency reduction).",
            PotentialSideEffectsPt: "Tarefas de segundo plano (como indexação ou sincronização) podem progredir mais lentamente enquanto um jogo estiver aberto.",
            PotentialSideEffectsEn: "Background tasks (such as indexing or file syncing) may progress slightly slower while a game is running.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Requer Administrador para gravar em HKLM).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires Administrator to write to HKLM).",
            DefaultWindowsBehaviorPt: "Padrão documentado pelo Windows Client: 20 (20% reservado para tarefas de fundo).",
            DefaultWindowsBehaviorEn: "Documented Windows Client default: 20 (20% reserved for background tasks).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Condicional ao cenário: só produz efeito perceptível quando há disputa real de CPU (>80% de carga) entre um aplicativo multimídia registrado no MMCSS e processos de segundo plano.",
            BenefitConditionalityEn: "Scenario-dependent: only produces a noticeable effect when there is real CPU contention (>80% load) between an MMCSS-registered app and background tasks."
        ),

        ["perf-game-dvr"] = new(
            TweakId: "perf-game-dvr",
            Hive: "HKCU",
            SubKeyPath: @"System\GameConfigStore",
            ValueName: "GameDVR_Enabled",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Desativar Gravação em Segundo Plano do Xbox Game DVR (GameDVR_Enabled)",
            DescriptionEn: "Disable Xbox Game DVR Background Recording (GameDVR_Enabled)",
            Category: "Gaming",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Captura em Segundo Plano do Xbox Game DVR (GameDVR_Enabled)",
            FriendlyNameEn: "Xbox Game DVR Background Recording (GameDVR_Enabled)",
            WhatItDoesPt: "Desativa a captura contínua de vídeo em segundo plano realizada pelo componente Game DVR do Windows.",
            WhatItDoesEn: "Disables continuous background video capture performed by the Windows Game DVR component.",
            ProblemSolvedPt: "Evita que o codificador de vídeo da GPU mantenha buffers de repetição instantânea ativos durante jogos.",
            ProblemSolvedEn: "Prevents the GPU video encoder from maintaining active instant-replay buffers during gameplay.",
            ExpectedBenefitPt: "Elimina o consumo de VRAM e de codificador de hardware caso a gravação automática em segundo plano estivesse habilitada.",
            ExpectedBenefitEn: "Eliminates VRAM and hardware encoder overhead if automatic background recording was enabled.",
            PotentialSideEffectsPt: "Desativa o recurso de gravar os últimos segundos de jogo via atalho Win+Alt+G no Xbox Game Bar.",
            PotentialSideEffectsEn: "Disables the 'Record last X seconds' shortcut (Win+Alt+G) in Xbox Game Bar.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Escopo do usuário atual HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Current user scope HKCU).",
            DefaultWindowsBehaviorPt: "Dependente da configuração do Xbox Game Bar / perfil do usuário (1 quando a captura em segundo plano está ativa; 0 quando desativada).",
            DefaultWindowsBehaviorEn: "Depends on Xbox Game Bar / user profile configuration (1 when background capture is enabled; 0 when disabled).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Condicional à configuração prévia: só traz ganho de desempenho se a gravação contínua em segundo plano estivesse ativa (1) anteriormente.",
            BenefitConditionalityEn: "Configuration-dependent: only yields a performance gain if continuous background recording was previously active (1)."
        ),

        ["perf-hpet"] = new(
            TweakId: "perf-hpet",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            ValueName: "Win32PrioritySeparation",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "38",
            DefaultOriginValue: "2",
            AllowedValues: new[] { "2", "38", "26" },
            Description: "Prioridade e Quantum de Processos em Primeiro Plano (Win32PrioritySeparation = 38 / 0x26)",
            DescriptionEn: "Foreground Process Priority & Quantum (Win32PrioritySeparation = 38 / 0x26)",
            Category: "Performance",
            RiskLevel: "Médio",
            FriendlyNamePt: "Prioridade e Quantum de Processos em Primeiro Plano (Win32PrioritySeparation)",
            FriendlyNameEn: "Foreground Process Priority & Quantum (Win32PrioritySeparation)",
            WhatItDoesPt: "Controla a duração do quantum de agendamento do processador e o reforço de prioridade atribuído aos threads da janela ativa em primeiro plano (NOTA: altera Win32PrioritySeparation, não o timer HPET).",
            WhatItDoesEn: "Controls processor scheduling quantum length and priority boost given to threads of the active foreground window (NOTE: modifies Win32PrioritySeparation, not the HPET hardware timer).",
            ProblemSolvedPt: "Define intervalos de quantum curtos e fixos com proporção 3:1 a favor do aplicativo em primeiro plano (valor decimal 38 / 0x26 hex).",
            ProblemSolvedEn: "Configures short, fixed-length scheduling quantums with a 3:1 ratio favoring the foreground application (decimal 38 / 0x26 hex).",
            ExpectedBenefitPt: "Pode melhorar a responsividade de entrada da janela em foco quando há processos concorrentes em segundo plano (não garante aumento de FPS médio).",
            ExpectedBenefitEn: "Can improve input responsiveness of the focused window when background processes compete for CPU time (does not guarantee higher average FPS).",
            PotentialSideEffectsPt: "Processos em segundo plano (como codificadores de streaming OBS, Discord ou renderizações paralelas) recebem fatias menores de CPU.",
            PotentialSideEffectsEn: "Background processes (such as OBS streaming encoders, Discord, or background renders) receive shorter CPU time slices.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKLM — requer privilégios de Administrador).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKLM — requires Administrator privileges).",
            DefaultWindowsBehaviorPt: "Padrão oficial documentado do Windows Client: 2 (quantum variável curto com reforço padrão para primeiro plano).",
            DefaultWindowsBehaviorEn: "Official documented Windows Client default: 2 (short variable quantum with standard foreground boost).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Condicional ao tipo de uso: o efeito depende do processador e da carga multitarefa; não garante ganho de FPS e pode prejudicar streaming em PC único.",
            BenefitConditionalityEn: "Workload-dependent: effect depends on CPU and multitasking load; does not guarantee FPS gains and may hinder single-PC streaming."
        ),

        ["perf-tcp-nagle"] = new(
            TweakId: "perf-tcp-nagle",
            Hive: "HKLM",
            SubKeyPath: @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            ValueName: "NetworkThrottlingIndex",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "-1",
            DefaultOriginValue: "10",
            AllowedValues: new[] { "-1", "10", "1", "4294967295" },
            Description: "Controle de Limitação de Rede Multimídia (NetworkThrottlingIndex = 0xFFFFFFFF)",
            DescriptionEn: "Multimedia Network Throttling Control (NetworkThrottlingIndex = 0xFFFFFFFF)",
            Category: "Network",
            RiskLevel: "Médio",
            FriendlyNamePt: "Controle de Limitação de Rede Multimídia (NetworkThrottlingIndex)",
            FriendlyNameEn: "Multimedia Network Throttling Control (NetworkThrottlingIndex)",
            WhatItDoesPt: "Controla o limite de pacotes de rede por milissegundo processados pelo driver NDIS enquanto aplicativos multimídia estão ativos (NOTA: modifica NetworkThrottlingIndex do MMCSS, não o algoritmo TCP Nagle).",
            WhatItDoesEn: "Controls the maximum network packets per millisecond processed by NDIS while multimedia apps are active (NOTE: modifies MMCSS NetworkThrottlingIndex, not the TCP Nagle algorithm).",
            ProblemSolvedPt: "Remove o teto padrão de 10 pacotes/ms imposto pelo MMCSS definindo 0xFFFFFFFF (-1 em DWORD com sinal).",
            ProblemSolvedEn: "Removes the default 10 packets/ms cap enforced by MMCSS by setting 0xFFFFFFFF (-1 in signed DWORD).",
            ExpectedBenefitPt: "Evita limitação artificial de pacotes de rede quando jogos e fluxos de áudio multimídia rodam simultaneamente em conexões de alta taxa de pacotes.",
            ExpectedBenefitEn: "Prevents artificial network packet throttling when games and multimedia audio streams run simultaneously on high packet-rate links.",
            PotentialSideEffectsPt: "Segundo a documentação da Microsoft, desativar o throttling em redes Gigabit saturadas pode causar falhas em reprodução de áudio/vídeo de tempo real em CPUs antigas.",
            PotentialSideEffectsEn: "Per Microsoft documentation, disabling throttling on saturated Gigabit networks may cause real-time audio/video playback glitches on older CPUs.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Requer reinicialização para recarregar o perfil do MMCSS).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires reboot to reload the MMCSS profile).",
            DefaultWindowsBehaviorPt: "Padrão documentado pela Microsoft: 10 (10 pacotes por milissegundo).",
            DefaultWindowsBehaviorEn: "Microsoft documented default: 10 (10 packets per millisecond).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Condicional à rede e carga multimídia: não altera a latência de rota da internet (ping do provedor) e só atua quando o MMCSS está ativo com tráfego acima de 10 pacotes/ms.",
            BenefitConditionalityEn: "Network and workload-dependent: does not change ISP route latency (ping) and only acts when MMCSS is active with traffic exceeding 10 packets/ms."
        ),

        ["start-fast-boot"] = new(
            TweakId: "start-fast-boot",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
            ValueName: "HiberbootEnabled",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Desativar Inicialização Rápida do Windows (HiberbootEnabled = 0)",
            DescriptionEn: "Disable Windows Fast Startup (HiberbootEnabled = 0)",
            Category: "Startup",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Desativar Inicialização Rápida do Windows (Fast Startup / HiberbootEnabled = 0)",
            FriendlyNameEn: "Disable Windows Fast Startup (HiberbootEnabled = 0)",
            WhatItDoesPt: "Define HiberbootEnabled = 0 para DESATIVAR a Inicialização Rápida (hibernação híbrida do kernel ao desligar), forçando um desligamento completo do sistema.",
            WhatItDoesEn: "Sets HiberbootEnabled = 0 to DISABLE Windows Fast Startup (hybrid kernel hibernation on shutdown), enforcing a full system shutdown.",
            ProblemSolvedPt: "Garante que o kernel NT e os drivers de vídeo, áudio e rede sejam reinicializados do zero ao ligar o PC, em vez de restaurar uma imagem hibernada.",
            ProblemSolvedEn: "Ensures the NT kernel and video, audio, and network drivers are freshly initialized when powering on the PC instead of restoring a hibernated image.",
            ExpectedBenefitPt: "Evita estado residual acumulado de drivers e uptime prolongado do kernel entre desligamentos, sem prometer boot mais rápido.",
            ExpectedBenefitEn: "Prevents accumulated residual driver state and prolonged kernel uptime across shutdowns, without promising a faster boot.",
            PotentialSideEffectsPt: "Como desativa a retomada híbrida, o tempo para ligar o PC a frio pode aumentar em alguns segundos.",
            PotentialSideEffectsEn: "Because it disables hybrid resume, cold boot time may increase by a few seconds.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 em sistemas com hibernação habilitada.",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 on systems with hibernation enabled.",
            DefaultWindowsBehaviorPt: "Dependente do suporte a hibernação: vale 1 (Inicialização Rápida ativada) quando a hibernação está habilitada; pode estar ausente ou 0 se a hibernação foi desativada (powercfg /h off).",
            DefaultWindowsBehaviorEn: "Depends on hibernation support: equals 1 (Fast Startup enabled) when hibernation is active; may be absent or 0 if hibernation was disabled (powercfg /h off).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Focado em estabilidade de drivers e reinicialização limpa do kernel (não aumenta FPS nem acelera o tempo de boot).",
            BenefitConditionalityEn: "Focused on driver stability and clean kernel initialization (does not increase FPS or speed up boot time)."
        ),

        ["start-edge-preload"] = new(
            TweakId: "start-edge-preload",
            Hive: "HKLM",
            SubKeyPath: @"SOFTWARE\Policies\Microsoft\Edge",
            ValueName: "StartupBoostEnabled",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Desativar Impulso de Inicialização do Microsoft Edge (StartupBoostEnabled = 0)",
            DescriptionEn: "Disable Microsoft Edge Startup Boost Policy (StartupBoostEnabled = 0)",
            Category: "Startup",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Impulso de Inicialização do Microsoft Edge (StartupBoostEnabled)",
            FriendlyNameEn: "Microsoft Edge Startup Boost (StartupBoostEnabled)",
            WhatItDoesPt: "Define a política do Microsoft Edge para impedir que processos msedge.exe permaneçam pré-carregados em segundo plano após o logon ou fechamento do navegador.",
            WhatItDoesEn: "Sets the Microsoft Edge policy to prevent msedge.exe processes from staying preloaded in the background after logon or browser closure.",
            ProblemSolvedPt: "Evita consumo contínuo de memória RAM por processos em segundo plano do Edge quando o navegador não está em uso.",
            ProblemSolvedEn: "Prevents continuous RAM usage by background Edge processes when the browser is not in use.",
            ExpectedBenefitPt: "Reduz processos residentes em segundo plano e libera memória RAM quando o Edge está fechado.",
            ExpectedBenefitEn: "Reduces resident background processes and frees RAM when Edge is closed.",
            PotentialSideEffectsPt: "O Microsoft Edge exibirá o aviso 'Gerenciado pela sua organização' nas configurações por usar uma chave de política em HKLM\\SOFTWARE\\Policies.",
            PotentialSideEffectsEn: "Microsoft Edge will display 'Managed by your organization' in settings because a policy key under HKLM\\SOFTWARE\\Policies is used.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Sistemas com Microsoft Edge instalado.",
            HardwareOrOsDependencyEn: "Systems with Microsoft Edge installed.",
            DefaultWindowsBehaviorPt: "Ausente por padrão no Registro (a subchave de política não existe em instalações padrão e o comportamento depende das configurações internas do Edge).",
            DefaultWindowsBehaviorEn: "Absent by default in the Registry (the policy subkey does not exist on standard installs and behavior depends on internal Edge settings).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Condicional ao uso do navegador: só reduz processos em segundo plano se o Startup Boost do Edge estivesse ativo.",
            BenefitConditionalityEn: "Browser-dependent: only reduces background processes if Edge Startup Boost was previously active."
        ),

        ["bg-telemetry"] = new(
            TweakId: "bg-telemetry",
            Hive: "HKLM",
            SubKeyPath: @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            ValueName: "AllowTelemetry",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "1",
            DefaultOriginValue: "3",
            AllowedValues: new[] { "1", "3" },
            Description: "Limitar Telemetria Diagnóstica ao Nível Básico Obrigatório (AllowTelemetry = 1)",
            DescriptionEn: "Limit Diagnostic Telemetry to Required Basic Level (AllowTelemetry = 1)",
            Category: "Privacy",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Política de Dados de Diagnóstico do Windows (AllowTelemetry)",
            FriendlyNameEn: "Windows Diagnostic Data Policy (AllowTelemetry)",
            WhatItDoesPt: "Configura a política de coleta de dados de diagnóstico do Windows para o nível 1 (Obrigatório / Básico), sem desativar serviços de segurança ou Windows Update.",
            WhatItDoesEn: "Configures the Windows diagnostic data collection policy to level 1 (Required / Basic), without disabling security services or Windows Update.",
            ProblemSolvedPt: "Reduz o envio de telemetria diagnóstica opcional e relatórios detalhados de uso em segundo plano.",
            ProblemSolvedEn: "Reduces optional diagnostic telemetry uploads and detailed usage reporting in the background.",
            ExpectedBenefitPt: "Menor atividade de coleta diagnóstica opcional em segundo plano mantendo conformidade com o Windows Update.",
            ExpectedBenefitEn: "Lower optional background diagnostic collection activity while maintaining Windows Update compliance.",
            PotentialSideEffectsPt: "A página 'Diagnóstico e comentários' nas Configurações do Windows pode informar que algumas configurações são gerenciadas por política. O programa Windows Insider exige nível 3.",
            PotentialSideEffectsEn: "The 'Diagnostics & feedback' page in Windows Settings may state that some settings are managed by policy. Windows Insider builds require level 3.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Restrição por edição do Windows: em edições Windows 10/11 Home e Pro o nível mínimo respeitado é 1 (Básico); o nível 0 (Segurança) é exclusivo de edições Enterprise, Education e Server.",
            HardwareOrOsDependencyEn: "Edition restriction: on Windows 10/11 Home and Pro the minimum honored level is 1 (Required/Basic); level 0 (Security) applies only to Enterprise, Education, and Server editions.",
            DefaultWindowsBehaviorPt: "Ausente por padrão na subchave de políticas (sem política explícita, o nível efetivo depende da edição do Windows e da escolha feita pelo usuário no OOBE).",
            DefaultWindowsBehaviorEn: "Absent by default under the policy subkey (without an explicit policy, the effective level depends on Windows edition and the user's OOBE choice).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Focado em privacidade e redução de envio diagnóstico opcional; o comportamento depende da edição do Windows (Home/Pro vs. Enterprise/Education).",
            BenefitConditionalityEn: "Focused on privacy and reducing optional diagnostic uploads; behavior depends on Windows edition (Home/Pro vs. Enterprise/Education)."
        ),

        ["bg-cortana-news"] = new(
            TweakId: "bg-cortana-news",
            Hive: "HKCU",
            SubKeyPath: @"Software\Microsoft\Windows\CurrentVersion\Feeds",
            ValueName: "ShellFeedsTaskbarViewMode",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "2",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "1", "2" },
            Description: "Ocultar Feed 'Notícias e Interesses' na Barra de Tarefas do Windows 10 (ShellFeedsTaskbarViewMode = 2)",
            DescriptionEn: "Hide 'News and Interests' Feed on Windows 10 Taskbar (ShellFeedsTaskbarViewMode = 2)",
            Category: "Interface",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Feed de Notícias e Interesses da Barra de Tarefas (Windows 10)",
            FriendlyNameEn: "Taskbar News and Interests Feed (Windows 10)",
            WhatItDoesPt: "Define o modo de exibição do botão 'Notícias e Interesses' na barra de tarefas do Windows 10 para 2 (Desativado). NOTA: não controla a Cortana.",
            WhatItDoesEn: "Sets the display mode of the 'News and Interests' widget on the Windows 10 taskbar to 2 (Turned off). NOTE: does not control Cortana.",
            ProblemSolvedPt: "Evita que o flyout de notícias/clima atualize conteúdo web em segundo plano na barra de tarefas do Windows 10.",
            ProblemSolvedEn: "Prevents the news/weather flyout from refreshing web content in the background on the Windows 10 taskbar.",
            ExpectedBenefitPt: "Remove o painel de notícias da barra de tarefas do Windows 10 e reduz requisições periódicas do feed.",
            ExpectedBenefitEn: "Removes the news flyout from the Windows 10 taskbar and reduces periodic feed requests.",
            PotentialSideEffectsPt: "O ícone de previsão do tempo na barra de tarefas do Windows 10 ficará oculto. No Windows 11 esta chave não tem efeito (substituída por Widgets).",
            PotentialSideEffectsEn: "The weather icon on the Windows 10 taskbar is hidden. On Windows 11 this key has no effect (replaced by Widgets).",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Específico para Windows 10 (21H1/21H2/22H2).",
            HardwareOrOsDependencyEn: "Specific to Windows 10 (21H1/21H2/22H2).",
            DefaultWindowsBehaviorPt: "Dependente da versão do Windows: no Windows 10 o padrão é 0 (Mostrar ícone e texto); no Windows 11 a subchave/valor pode não existir.",
            DefaultWindowsBehaviorEn: "Version-dependent: on Windows 10 the default is 0 (Show icon and text); on Windows 11 the subkey/value may be absent.",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            MinimumWindowsBuild: 19041,
            MaximumWindowsBuild: 21999,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Restrito ao Windows 10: não produz efeito no Windows 11 (que utiliza o componente Widgets em outra chave).",
            BenefitConditionalityEn: "Restricted to Windows 10: has no effect on Windows 11 (which uses the Widgets component under a different key)."
        ),

        ["power-ultimate-plan"] = new(
            TweakId: "power-ultimate-plan",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
            ValueName: "PowerThrottlingOff",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "1",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "1" },
            Description: "Controle de Power Throttling de Processos (PowerThrottlingOff = 1)",
            DescriptionEn: "Process Power Throttling Control (PowerThrottlingOff = 1)",
            Category: "Power",
            RiskLevel: "Médio",
            FriendlyNamePt: "Controle de Power Throttling de Processos (PowerThrottlingOff)",
            FriendlyNameEn: "Process Power Throttling Control (PowerThrottlingOff)",
            WhatItDoesPt: "Desativa globalmente o mecanismo Power Throttling (EcoQoS) do kernel do Windows para processos em segundo plano (NOTA: modifica PowerThrottlingOff; NÃO cria nem ativa o plano de energia Ultimate Performance).",
            WhatItDoesEn: "Globally disables the Windows kernel Power Throttling (EcoQoS) mechanism for background processes (NOTE: modifies PowerThrottlingOff; does NOT create or activate the Ultimate Performance power plan).",
            ProblemSolvedPt: "Impede que o sistema reduza automaticamente a frequência de CPU de tarefas classificadas como segundo plano.",
            ProblemSolvedEn: "Prevents the OS from automatically lowering CPU frequency on tasks classified as background work.",
            ExpectedBenefitPt: "Pode evitar redução de clock em aplicativos auxiliares de áudio/captura em segundo plano em desktops ligados na tomada.",
            ExpectedBenefitEn: "Can prevent clock throttling on auxiliary background audio/capture apps on AC-powered desktops.",
            PotentialSideEffectsPt: "Aumenta o consumo de energia e reduz a autonomia de bateria em notebooks; pode interferir no gerenciamento de eficiência (EcoQoS) em processadores híbridos.",
            PotentialSideEffectsEn: "Increases power consumption and reduces battery life on laptops; may interfere with efficiency management (EcoQoS) on hybrid processors.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Processadores com suporte a Intel Speed Shift / EcoQoS no Windows 10 e 11. Não recomendado para notebooks na bateria.",
            HardwareOrOsDependencyEn: "Processors supporting Intel Speed Shift / EcoQoS on Windows 10 and 11. Not recommended for laptops on battery.",
            DefaultWindowsBehaviorPt: "Ausente por padrão no Registro com comportamento interno conhecido equivalente a 0 (Power Throttling habilitado e gerenciado automaticamente pelo kernel).",
            DefaultWindowsBehaviorEn: "Absent by default in the Registry with known internal behavior equivalent to 0 (Power Throttling enabled and automatically managed by the kernel).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.AbsentWithKnownDefault,
            BenefitConditionalityPt: "Condicional ao hardware e perfil energético: não ativa o plano Ultimate Performance e depende do suporte da CPU a Speed Shift/EcoQoS.",
            BenefitConditionalityEn: "Hardware and power-profile dependent: does not activate the Ultimate Performance plan and depends on CPU Speed Shift/EcoQoS support."
        ),

        ["power-pcie-link"] = new(
            TweakId: "power-pcie-link",
            Hive: "HKLM",
            SubKeyPath: @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
            ValueName: "Scheduling Category",
            ValueKind: RegistryValueKind.String,
            RecommendedValue: "High",
            DefaultOriginValue: "Medium",
            AllowedValues: new[] { "High", "Medium", "Low" },
            Description: "Categoria de Agendamento Multimídia para Jogos (Scheduling Category = High)",
            DescriptionEn: "Multimedia Scheduling Category for Games (Scheduling Category = High)",
            Category: "Gaming",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Categoria de Agendamento Multimídia para Jogos (MMCSS Scheduling Category)",
            FriendlyNameEn: "Multimedia Scheduling Category for Games (MMCSS Scheduling Category)",
            WhatItDoesPt: "Define o valor 'Scheduling Category' (REG_SZ) do perfil MMCSS 'Games' para 'High' (NOTA: altera a categoria do agendador multimídia para jogos, não o gerenciamento de energia PCIe ASPM).",
            WhatItDoesEn: "Sets the 'Scheduling Category' (REG_SZ) value of the MMCSS 'Games' profile to 'High' (NOTE: modifies the multimedia scheduler category for games, not PCIe ASPM power management).",
            ProblemSolvedPt: "Garante que threads de jogos registradas na tarefa MMCSS 'Games' recebam a categoria de prioridade 'High' em vez de 'Medium'.",
            ProblemSolvedEn: "Ensures game threads registered under the MMCSS 'Games' task receive the 'High' priority category instead of 'Medium'.",
            ExpectedBenefitPt: "Prioriza o escalonamento de threads de jogos que utilizam a API AvSetMmThreadCharacteristics(\"Games\").",
            ExpectedBenefitEn: "Prioritizes thread scheduling for games that invoke the AvSetMmThreadCharacteristics(\"Games\") API.",
            PotentialSideEffectsPt: "Impacto restrito a jogos e motores gráficos que se registram explicitamente no serviço MMCSS.",
            PotentialSideEffectsEn: "Effect is limited to games and engines that explicitly register with the MMCSS service.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKLM).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKLM).",
            DefaultWindowsBehaviorPt: "Padrão documentado de instalação do Windows: 'Medium' (REG_SZ).",
            DefaultWindowsBehaviorEn: "Documented Windows installation default: 'Medium' (REG_SZ).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Condicional à engine do jogo: atua apenas em títulos que solicitam o perfil 'Games' ao serviço MMCSS sob disputa de CPU.",
            BenefitConditionalityEn: "Game-engine dependent: only affects titles that request the 'Games' profile from the MMCSS service during CPU contention."
        ),

        ["serv-sysmain"] = new(
            TweakId: "serv-sysmain",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Services\SysMain",
            ValueName: "Start",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "3",
            DefaultOriginValue: "2",
            AllowedValues: new[] { "2", "3" },
            Description: "Configurar Serviço SysMain (Superfetch) para Inicialização Manual (Start = 3)",
            DescriptionEn: "Configure SysMain (Superfetch) Service for Manual Startup (Start = 3)",
            Category: "Services",
            RiskLevel: "Médio",
            FriendlyNamePt: "Modo de Inicialização do Serviço SysMain / Superfetch (Start)",
            FriendlyNameEn: "SysMain / Superfetch Service Startup Mode (Start)",
            WhatItDoesPt: "Altera o tipo de inicialização do serviço SysMain de Automático (2) para Manual (3).",
            WhatItDoesEn: "Changes the startup type of the SysMain service from Automatic (2) to Manual (3).",
            ProblemSolvedPt: "Impede o pré-carregamento preditivo automático de aplicativos no boot em sistemas que desejam controlar o serviço manualmente.",
            ProblemSolvedEn: "Prevents automatic predictive preloading of applications at boot on systems where manual control is desired.",
            ExpectedBenefitPt: "Pode reduzir leituras de pré-busca em segundo plano logo após o boot em cenários específicos (NÃO melhora automaticamente o desempenho em SSDs NVMe).",
            ExpectedBenefitEn: "Can reduce background prefetch reads right after boot in specific scenarios (does NOT automatically improve performance on NVMe SSDs).",
            PotentialSideEffectsPt: "Desativa a pré-busca adaptativa do SysMain, o que pode aumentar o tempo de abertura de aplicativos cotidianos mesmo em SSDs NVMe.",
            PotentialSideEffectsEn: "Disables SysMain adaptive prefetching, which may slightly increase launch times for everyday applications even on NVMe SSDs.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Exige reinicialização para alterar o estado efetivo do serviço).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires reboot to change the effective service state).",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows: 2 (Inicialização Automática).",
            DefaultWindowsBehaviorEn: "Official Windows default: 2 (Automatic startup).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Altamente dependente do cenário: desativar ou colocar o SysMain em manual NÃO garante melhoria em SSDs NVMe e pode deixar o carregamento de aplicativos frequentes mais lento.",
            BenefitConditionalityEn: "Highly scenario-dependent: disabling or setting SysMain to manual does NOT guarantee improvements on NVMe SSDs and may slow down frequent app launches."
        ),

        ["serv-spooler"] = new(
            TweakId: "serv-spooler",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Services\Spooler",
            ValueName: "Start",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "3",
            DefaultOriginValue: "2",
            AllowedValues: new[] { "2", "3" },
            Description: "Spooler de Impressão em Inicialização Manual (Start = 3 — Apenas Sem Impressora)",
            DescriptionEn: "Print Spooler Manual Startup (Start = 3 — Only Without Printers)",
            Category: "Services",
            RiskLevel: "Médio",
            FriendlyNamePt: "Modo de Inicialização do Spooler de Impressão (Spooler Start)",
            FriendlyNameEn: "Print Spooler Service Startup Mode (Spooler Start)",
            WhatItDoesPt: "Altera a inicialização do serviço Spooler de Impressão (spoolsv.exe) de Automático (2) para Manual (3).",
            WhatItDoesEn: "Changes the Print Spooler service (spoolsv.exe) startup type from Automatic (2) to Manual (3).",
            ProblemSolvedPt: "Evita que o serviço de fila de impressão inicie automaticamente no boot em computadores que nunca utilizam impressoras.",
            ProblemSolvedEn: "Prevents the print queue service from starting automatically at boot on PCs that never use printers.",
            ExpectedBenefitPt: "Evita a execução automática do processo spoolsv.exe em máquinas que não utilizam impressão física nem PDF via Spooler.",
            ExpectedBenefitEn: "Prevents automatic execution of spoolsv.exe on machines that do not use physical printing or Spooler-based PDF printing.",
            PotentialSideEffectsPt: "ATENÇÃO: Impressoras físicas, impressoras de rede e 'Microsoft Print to PDF' deixarão de funcionar até que o serviço seja iniciado manualmente ou revertido para 2.",
            PotentialSideEffectsEn: "WARNING: Physical printers, network printers, and 'Microsoft Print to PDF' will stop working until the service is started manually or reverted to 2.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Não recomendado se você utiliza impressoras físicas ou geração de PDF via Spooler.",
            HardwareOrOsDependencyEn: "Not recommended if you use physical printers or Spooler-based PDF generation.",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows: 2 (Automático).",
            DefaultWindowsBehaviorEn: "Official Windows default: 2 (Automatic).",
            RecommendationStatus: TweakRecommendationStatus.NotRecommendedOrDefault,
            IsRecommendedForApply: false,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Não gera ganho de FPS em jogos; apenas evita um serviço residente em computadores sem impressora.",
            BenefitConditionalityEn: "Does not generate gaming FPS gains; merely avoids one resident service on printerless PCs."
        ),

        ["mem-standby-clean"] = new(
            TweakId: "mem-standby-clean",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            ValueName: "LargeSystemCache",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "1" },
            Description: "Configuração do Cache de Sistema de Arquivos (LargeSystemCache = 0)",
            DescriptionEn: "File System Cache Configuration (LargeSystemCache = 0)",
            Category: "Memory",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Configuração do Cache de Sistema de Arquivos (LargeSystemCache)",
            FriendlyNameEn: "File System Cache Configuration (LargeSystemCache)",
            WhatItDoesPt: "Define a política do cache de sistema de arquivos na memória RAM (0 = prioriza aplicativos em estações de trabalho; 1 = prioriza cache de arquivos em servidores). NOTA: altera LargeSystemCache e NÃO executa limpeza da lista de memória Standby.",
            WhatItDoesEn: "Configures the file system memory cache policy (0 = prioritizes applications on workstations; 1 = prioritizes file cache on servers). NOTE: modifies LargeSystemCache and does NOT clean or purge Standby memory.",
            ProblemSolvedPt: "Garante que edições Windows Client mantenham o valor padrão 0 caso algum utilitário externo tenha ativado o modo de servidor de arquivos (1).",
            ProblemSolvedEn: "Ensures Windows Client editions keep the default value 0 if an external utility previously enabled file-server cache mode (1).",
            ExpectedBenefitPt: "Como 0 já é o valor padrão oficial do Windows 10/11 Client, só possui efeito corretivo se a chave tiver sido alterada para 1 anteriormente.",
            ExpectedBenefitEn: "Because 0 is already the official Windows 10/11 Client default, it only has a corrective effect if the key was previously changed to 1.",
            PotentialSideEffectsPt: "Nenhum efeito negativo quando definido como 0 (padrão oficial do Windows Client).",
            PotentialSideEffectsEn: "No negative effect when set to 0 (official Windows Client default).",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Conformidade do padrão Client).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Client default compliance).",
            DefaultWindowsBehaviorPt: "Padrão oficial documentado do Windows Client: 0 (prioridade de memória para aplicativos).",
            DefaultWindowsBehaviorEn: "Official documented Windows Client default: 0 (memory priority for applications).",
            RecommendationStatus: TweakRecommendationStatus.NotRecommendedOrDefault,
            IsRecommendedForApply: false,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Apenas corretivo: como 0 já é o padrão do Windows Client, aplicá-lo em um sistema íntegro mantém o comportamento original sem ganho adicional.",
            BenefitConditionalityEn: "Corrective only: since 0 is already the Windows Client default, applying it on an unmodified system keeps original behavior with no extra gain."
        ),

        ["mem-lock-executive"] = new(
            TweakId: "mem-lock-executive",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            ValueName: "DisablePagingExecutive",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "1",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "1" },
            Description: "Manter Executivo Paginável na Memória RAM Física (DisablePagingExecutive = 1)",
            DescriptionEn: "Keep Pageable Kernel Executive in Physical RAM (DisablePagingExecutive = 1)",
            Category: "Memory",
            RiskLevel: "Médio",
            FriendlyNamePt: "Retenção do Executivo do Kernel em RAM (DisablePagingExecutive)",
            FriendlyNameEn: "Kernel Executive RAM Retention (DisablePagingExecutive)",
            WhatItDoesPt: "Orienta o gerenciador de memória a manter seções pagináveis do executivo do Windows NT e de drivers residentes na memória RAM física em vez de paginá-las para o disco.",
            WhatItDoesEn: "Instructs the memory manager to keep pageable sections of the Windows NT executive and drivers resident in physical RAM instead of paging them to disk.",
            ProblemSolvedPt: "Evita que rotinas pagináveis de drivers e do kernel sejam transferidas ao arquivo de paginação (pagefile.sys) durante períodos de inatividade.",
            ProblemSolvedEn: "Prevents pageable driver and kernel routines from being swapped out to pagefile.sys during idle periods.",
            ExpectedBenefitPt: "Em sistemas modernos com SSD NVMe o ganho prático em jogos é mínimo ou imperceptível; historicamente utilizado para análise e depuração de kernel.",
            ExpectedBenefitEn: "On modern systems with NVMe SSDs, practical gaming gains are minimal or unnoticeable; historically used for kernel analysis and debugging.",
            PotentialSideEffectsPt: "Aumenta permanentemente o consumo de memória RAM física não-paginável pelo sistema operacional, reduzindo a margem de RAM livre disponível para jogos e aplicativos sob alta pressão de memória.",
            PotentialSideEffectsEn: "Permanently increases physical RAM consumption by the OS, reducing the free RAM margin available for games and applications under heavy memory pressure.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Requer reinicialização; o impacto no consumo de RAM depende da quantidade de drivers carregados e da folga de memória do sistema).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires reboot; RAM consumption impact depends on loaded drivers and available system memory headroom).",
            DefaultWindowsBehaviorPt: "Padrão oficial documentado do Windows: 0 (permite paginação normal de seções inativas do executivo quando necessário).",
            DefaultWindowsBehaviorEn: "Official documented Windows default: 0 (allows normal paging of inactive executive sections when needed).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: false,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Benefício não garantido: depende da folga de RAM física e da velocidade do armazenamento; aumenta o consumo fixo de memória sem ganho garantido de FPS.",
            BenefitConditionalityEn: "Benefit not guaranteed: depends on physical RAM headroom and storage speed; increases fixed memory consumption without guaranteed FPS gains."
        ),

        ["game-hags"] = new(
            TweakId: "game-hags",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
            ValueName: "HwSchMode",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "2",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "1", "2" },
            Description: "Agendamento de GPU Acelerado por Hardware — HAGS (HwSchMode = 2)",
            DescriptionEn: "Hardware-Accelerated GPU Scheduling — HAGS (HwSchMode = 2)",
            Category: "Gaming",
            RiskLevel: "Médio",
            FriendlyNamePt: "Agendamento de GPU Acelerado por Hardware (HAGS / HwSchMode)",
            FriendlyNameEn: "Hardware-Accelerated GPU Scheduling (HAGS / HwSchMode)",
            WhatItDoesPt: "Habilita o Hardware-Accelerated GPU Scheduling (HwSchMode = 2), permitindo que o processador interno da GPU gerencie diretamente sua fila de agendamento de VRAM.",
            WhatItDoesEn: "Enables Hardware-Accelerated GPU Scheduling (HwSchMode = 2), allowing the GPU's internal processor to directly manage its VRAM scheduling queue.",
            ProblemSolvedPt: "Ativa o requisito de sistema operacional para tecnologias como NVIDIA DLSS 3 Frame Generation e pode reduzir a sobrecarga de submissão de quadros da CPU.",
            ProblemSolvedEn: "Enables the OS requirement for technologies like NVIDIA DLSS 3 Frame Generation and can reduce CPU frame submission overhead.",
            ExpectedBenefitPt: "Necessário para Frame Generation (DLSS 3) e pode reduzir latência de agendamento em títulos DirectX 12 compatíveis, mas o resultado varia conforme GPU, driver e jogo.",
            ExpectedBenefitEn: "Required for Frame Generation (DLSS 3) and can reduce scheduling latency in compatible DirectX 12 titles, though results vary by GPU, driver, and game.",
            PotentialSideEffectsPt: "O comportamento depende da GPU, do driver WDDM 2.7+ e da versão do Windows. Em alguns motores gráficos ou com captura OBS sob GPU em 99%, pode causar engasgos (stutter). Exige reinicialização.",
            PotentialSideEffectsEn: "Behavior depends on the GPU, WDDM 2.7+ driver, and Windows version. In some game engines or with OBS capture at 99% GPU load, it may cause stutter. Requires reboot.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Depende de GPU compatível (NVIDIA GTX 1000+/RTX, AMD RX 5600+/7000+, Intel Arc), driver WDDM 2.7+ e versão do Windows 10 (2004+) ou Windows 11.",
            HardwareOrOsDependencyEn: "Depends on a supported GPU (NVIDIA GTX 1000+/RTX, AMD RX 5600+/7000+, Intel Arc), WDDM 2.7+ driver, and Windows 10 (2004+) or Windows 11.",
            DefaultWindowsBehaviorPt: "Dependente do hardware, driver de vídeo e versão do Windows: pode vir como 2 por padrão no Windows 11 com GPUs recentes, 1 em instalações anteriores, ou ausente se o driver não suportar WDDM 2.7+.",
            DefaultWindowsBehaviorEn: "Depends on hardware, video driver, and Windows version: may default to 2 on Windows 11 with modern GPUs, 1 on older setups, or be absent if the driver lacks WDDM 2.7+ support.",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Estritamente dependente de GPU, driver WDDM 2.7+ e versão do Windows: se o hardware/driver não suportar HAGS, o kernel ignora o valor no Registro.",
            BenefitConditionalityEn: "Strictly dependent on GPU, WDDM 2.7+ driver, and Windows version: if hardware/driver lacks HAGS support, the kernel ignores the Registry value."
        ),

        ["game-fso-override"] = new(
            TweakId: "game-fso-override",
            Hive: "HKCU",
            SubKeyPath: @"System\GameConfigStore",
            ValueName: "GameDVR_FSEBehaviorMode",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "2",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "2" },
            Description: "Modo de Comportamento de Tela Inteira no GameConfigStore (GameDVR_FSEBehaviorMode = 2)",
            DescriptionEn: "Fullscreen Behavior Mode in GameConfigStore (GameDVR_FSEBehaviorMode = 2)",
            Category: "Gaming",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Política de Comportamento de Tela Inteira (GameDVR_FSEBehaviorMode)",
            FriendlyNameEn: "Fullscreen Behavior Policy (GameDVR_FSEBehaviorMode)",
            WhatItDoesPt: "Configura a preferência global de comportamento de tela inteira (Fullscreen Optimizations / FSE) no repositório GameConfigStore do usuário.",
            WhatItDoesEn: "Configures the user's global Fullscreen Optimizations (FSO / FSE) behavior preference in GameConfigStore.",
            ProblemSolvedPt: "Permite ajustar como o Windows trata a transição entre Fullscreen Otimizado (Flip Model) e Fullscreen Exclusivo em títulos legados.",
            ProblemSolvedEn: "Controls how Windows handles the transition between Optimized Fullscreen (Flip Model) and Exclusive Fullscreen in legacy titles.",
            ExpectedBenefitPt: "Pode auxiliar em jogos DirectX 9/11 antigos que apresentem conflito de cadência com overlays do sistema.",
            ExpectedBenefitEn: "Can help in older DirectX 9/11 games that exhibit frame-pacing conflicts with system overlays.",
            PotentialSideEffectsPt: "Jogos DirectX 12 nativos utilizam Flip Model independentemente desta chave; pode tornar a troca de janela (Alt+Tab) mais lenta em jogos DX9/DX11.",
            PotentialSideEffectsEn: "Native DirectX 12 games use Flip Model regardless of this key; may make window switching (Alt+Tab) slower in DX9/DX11 games.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKCU).",
            DefaultWindowsBehaviorPt: "Normalmente 0 no perfil do usuário (Otimizações de Tela Inteira padrão habilitadas).",
            DefaultWindowsBehaviorEn: "Typically 0 in the user profile (Standard Fullscreen Optimizations enabled).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Condicional à API gráfica: afeta apenas jogos DirectX 9/10/11 que usam Fullscreen Exclusivo legado; não altera jogos DirectX 12 ou Vulkan.",
            BenefitConditionalityEn: "Graphics-API dependent: only affects DirectX 9/10/11 games using legacy Exclusive Fullscreen; does not alter DirectX 12 or Vulkan games."
        ),

        ["perf-fso-flags"] = new(
            TweakId: "perf-fso-flags",
            Hive: "HKCU",
            SubKeyPath: @"System\GameConfigStore",
            ValueName: "GameDVR_FSEBehavior",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "2",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "2" },
            Description: "Flag Auxiliar de Tela Inteira Exclusiva (GameDVR_FSEBehavior = 2)",
            DescriptionEn: "Auxiliary Exclusive Fullscreen Flag (GameDVR_FSEBehavior = 2)",
            Category: "Gaming",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Flag Auxiliar de Comportamento Fullscreen (GameDVR_FSEBehavior)",
            FriendlyNameEn: "Auxiliary Fullscreen Behavior Flag (GameDVR_FSEBehavior)",
            WhatItDoesPt: "Define o valor auxiliar GameDVR_FSEBehavior utilizado em conjunto com GameDVR_FSEBehaviorMode e GameDVR_HonorUserFSEBehaviorMode no perfil do usuário.",
            WhatItDoesEn: "Sets the auxiliary GameDVR_FSEBehavior value used alongside GameDVR_FSEBehaviorMode and GameDVR_HonorUserFSEBehaviorMode in the user profile.",
            ProblemSolvedPt: "Complementa a configuração de preferência de tela inteira para jogos legados DirectX 9/11.",
            ProblemSolvedEn: "Complements the fullscreen preference configuration for legacy DirectX 9/11 games.",
            ExpectedBenefitPt: "Sincroniza o estado do flag secundário do GameConfigStore quando o usuário opta por sobrescrever FSO.",
            ExpectedBenefitEn: "Synchronizes the secondary GameConfigStore flag when the user opts to override FSO.",
            PotentialSideEffectsPt: "Sozinho não força Fullscreen Exclusivo em todos os títulos se o jogo usar Flip Model moderno.",
            PotentialSideEffectsEn: "By itself does not force Exclusive Fullscreen in all titles if the game uses modern Flip Model.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKCU).",
            DefaultWindowsBehaviorPt: "Ausente por padrão na maioria das instalações limpas do Windows 10/11 sem comportamento isolado suficientemente determinado quando ausente.",
            DefaultWindowsBehaviorEn: "Absent by default on most clean Windows 10/11 installations without a sufficiently determined standalone behavior when absent.",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.UndeterminedWhenAbsent,
            BenefitConditionalityPt: "Condicional: valor auxiliar que depende de outros parâmetros do GameConfigStore e da API gráfica do jogo.",
            BenefitConditionalityEn: "Conditional: auxiliary value that depends on other GameConfigStore parameters and the game's graphics API."
        ),

        ["sys-delivery-p2p"] = new(
            TweakId: "sys-delivery-p2p",
            Hive: "HKLM",
            SubKeyPath: @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
            ValueName: "DODownloadMode",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Desativar Transferência P2P na Otimização de Entrega (DODownloadMode = 0)",
            DescriptionEn: "Disable P2P Transfers in Delivery Optimization (DODownloadMode = 0)",
            Category: "Network",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Modo de Download da Otimização de Entrega (DODownloadMode)",
            FriendlyNameEn: "Delivery Optimization Download Mode (DODownloadMode)",
            WhatItDoesPt: "Configura a política da Otimização de Entrega do Windows Update para 0 (somente HTTP direto dos servidores da Microsoft, sem peer-to-peer).",
            WhatItDoesEn: "Configures the Windows Update Delivery Optimization policy to 0 (HTTP only from Microsoft servers, no peer-to-peer).",
            ProblemSolvedPt: "Impede que o serviço Delivery Optimization utilize a banda de upload para enviar blocos de atualização a outros PCs na rede ou internet.",
            ProblemSolvedEn: "Prevents the Delivery Optimization service from using upload bandwidth to serve update blocks to other PCs on the LAN or internet.",
            ExpectedBenefitPt: "Evita consumo inesperado de upload por compartilhamento P2P de atualizações durante partidas online.",
            ExpectedBenefitEn: "Prevents unexpected background upload usage from P2P update sharing during online matches.",
            PotentialSideEffectsPt: "Se você tiver vários PCs na mesma rede doméstica, cada um baixará atualizações diretamente da Microsoft em vez de compartilhar via LAN.",
            PotentialSideEffectsEn: "If you have multiple PCs on the same home LAN, each will download updates directly from Microsoft instead of sharing over LAN.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKLM).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKLM).",
            DefaultWindowsBehaviorPt: "Ausente por padrão na subchave de políticas; sem política, o modo padrão depende da edição do Windows (geralmente 1 = P2P na rede local em Home/Pro).",
            DefaultWindowsBehaviorEn: "Absent by default under the policy subkey; without policy, the default mode depends on Windows edition (typically 1 = LAN peering on Home/Pro).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Condicional ao tráfego de atualização: evita picos de upload por P2P de atualizações, mas não reduz a latência base da conexão quando não há transferências ativas.",
            BenefitConditionalityEn: "Update-traffic dependent: prevents P2P upload spikes during updates, but does not lower base connection latency when no transfers are active."
        ),

        ["sys-transparency"] = new(
            TweakId: "sys-transparency",
            Hive: "HKCU",
            SubKeyPath: @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            ValueName: "EnableTransparency",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Desativar Efeitos de Transparência da Interface (EnableTransparency = 0)",
            DescriptionEn: "Disable Shell Transparency Effects (EnableTransparency = 0)",
            Category: "Interface",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Efeitos de Transparência do Windows (EnableTransparency)",
            FriendlyNameEn: "Windows Shell Transparency Effects (EnableTransparency)",
            WhatItDoesPt: "Desativa os efeitos visuais de transparência (Acrylic / Mica) na barra de tarefas, menu Iniciar e janelas do sistema.",
            WhatItDoesEn: "Disables visual transparency effects (Acrylic / Mica) on the taskbar, Start menu, and system windows.",
            ProblemSolvedPt: "Reduz o processamento de shaders de desfoque pelo Desktop Window Manager (dwm.exe) na área de trabalho.",
            ProblemSolvedEn: "Reduces blur shader compositing work by the Desktop Window Manager (dwm.exe) on the desktop.",
            ExpectedBenefitPt: "Interface com superfícies opacas e leve redução de composição do DWM em GPUs integradas (não proporciona ganho perceptível de FPS em GPUs dedicadas modernas).",
            ExpectedBenefitEn: "Opaque surface UI with slightly lower DWM compositing overhead on integrated GPUs (does not provide noticeable FPS gains on modern dedicated GPUs).",
            PotentialSideEffectsPt: "Altera a aparência visual do tema escuro/claro do Windows (mesma opção disponível em Configurações > Personalização > Cores).",
            PotentialSideEffectsEn: "Changes the visual look of the Windows dark/light theme (same toggle found in Settings > Personalization > Colors).",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Efeito imediato no usuário atual HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Immediate effect on current user HKCU).",
            DefaultWindowsBehaviorPt: "Padrão documentado do Windows: 1 (Efeitos de transparência ativados).",
            DefaultWindowsBehaviorEn: "Documented Windows default: 1 (Transparency effects enabled).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.KnownDocumentedValue,
            BenefitConditionalityPt: "Efeito estético/leve no DWM: em placas de vídeo dedicadas modernas não gera aumento mensurável de FPS em jogos.",
            BenefitConditionalityEn: "Cosmetic/light DWM effect: on modern dedicated GPUs it does not produce measurable gaming FPS increases."
        ),

        ["storage-ntfs-lastaccess"] = new(
            TweakId: "storage-ntfs-lastaccess",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\FileSystem",
            ValueName: "NtfsDisableLastAccessUpdate",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "1",
            DefaultOriginValue: "0",
            AllowedValues: new[] { "0", "1", "-2147483647", "-2147483646" },
            Description: "Desativar Atualização de Carimbo de Último Acesso NTFS (NtfsDisableLastAccessUpdate = 1)",
            DescriptionEn: "Disable NTFS Last Access Timestamp Updates (NtfsDisableLastAccessUpdate = 1)",
            Category: "Storage",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Atualização de Timestamp de Último Acesso NTFS (NtfsDisableLastAccessUpdate)",
            FriendlyNameEn: "NTFS Last Access Timestamp Update (NtfsDisableLastAccessUpdate)",
            WhatItDoesPt: "Define explicitamente a configuração gerenciada pelo usuário (1) para desativar a gravação do atributo LastAccessTime ao apenas ler arquivos em volumes NTFS.",
            WhatItDoesEn: "Explicitly sets the user-managed setting (1) to disable writing the LastAccessTime attribute when merely reading files on NTFS volumes.",
            ProblemSolvedPt: "Evita operações extras de atualização de metadados NTFS caso a atualização de carimbo de último acesso tenha sido habilitada.",
            ProblemSolvedEn: "Avoids extra NTFS metadata update operations if last-access timestamp updates were enabled.",
            ExpectedBenefitPt: "No Windows 10/11 moderno, o sistema já costuma desativar essa atualização em modo gerenciado pelo sistema (0x80000001 / -2147483647); aplicar 1 fixa a preferência manual.",
            ExpectedBenefitEn: "On modern Windows 10/11, the OS typically already disables this in system-managed mode (0x80000001 / -2147483647); setting 1 locks the user-managed preference.",
            PotentialSideEffectsPt: "Ferramentas de backup ou auditoria que dependam da data de último acesso de leitura não verão essa data atualizada.",
            PotentialSideEffectsEn: "Backup or auditing tools that rely on file last-read timestamps will not see updated access times.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Volumes formatados em NTFS no Windows 10 e 11.",
            HardwareOrOsDependencyEn: "NTFS-formatted volumes on Windows 10 and 11.",
            DefaultWindowsBehaviorPt: "Dependente da versão/build do Windows e tamanho do volume: em builds recentes costuma ser 0x80000001 (-2147483647, System Managed Disabled) ou 0x80000002 (-2147483646).",
            DefaultWindowsBehaviorEn: "Depends on Windows build and volume size: on recent builds it is typically 0x80000001 (-2147483647, System Managed Disabled) or 0x80000002 (-2147483646).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Condicional ao estado prévio do NTFS: se o Windows já estiver em 0x80000001 (System Managed, Last Access desativado), mudar para 1 não altera o desempenho prático.",
            BenefitConditionalityEn: "Depends on prior NTFS state: if Windows is already at 0x80000001 (System Managed, Last Access disabled), changing to 1 does not alter practical performance."
        ),

        ["net-tcp-autotuning"] = new(
            TweakId: "net-tcp-autotuning",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            ValueName: "TcpMaxDataRetransmissions",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "5",
            DefaultOriginValue: "5",
            AllowedValues: new[] { "3", "5", "7" },
            Description: "Limite de Retransmissões de Dados TCP (TcpMaxDataRetransmissions = 5)",
            DescriptionEn: "TCP Data Retransmissions Limit (TcpMaxDataRetransmissions = 5)",
            Category: "Network",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Limite de Retransmissões de Dados TCP (TcpMaxDataRetransmissions)",
            FriendlyNameEn: "TCP Data Retransmissions Limit (TcpMaxDataRetransmissions)",
            WhatItDoesPt: "Define quantas vezes a pilha TCP/IP retransmite um segmento de dados não confirmado (ACK) antes de abortar a conexão (NOTA: modifica TcpMaxDataRetransmissions, NÃO o TCP Receive Window Auto-Tuning).",
            WhatItDoesEn: "Specifies how many times the TCP/IP stack retransmits an unacknowledged data segment (ACK) before aborting the connection (NOTE: modifies TcpMaxDataRetransmissions, NOT TCP Receive Window Auto-Tuning).",
            ProblemSolvedPt: "Garante o valor padrão seguro da RFC/Microsoft (5 retransmissões) caso utilitários de terceiros tenham reduzido o limite para 3 (o que provoca quedas prematuras de conexão em oscilações de rede).",
            ProblemSolvedEn: "Ensures the safe RFC/Microsoft default (5 retransmissions) if third-party tools previously lowered the limit to 3 (which causes premature connection drops during brief packet loss).",
            ExpectedBenefitPt: "Preserva a estabilidade de conexões TCP sob perda momentânea de pacotes; como 5 já é o padrão interno da pilha TCP do Windows, atua como verificação/restauração de integridade.",
            ExpectedBenefitEn: "Preserves TCP connection stability under momentary packet loss; because 5 is already the internal Windows TCP stack default, it acts as an integrity check/restoration.",
            PotentialSideEffectsPt: "Nenhum efeito colateral quando definido em 5 (comportamento padrão oficial da pilha TCP/IP do Windows). Valores menores (como 3) aumentam o risco de desconexões.",
            PotentialSideEffectsEn: "No side effects when set to 5 (official default behavior of the Windows TCP/IP stack). Lower values (such as 3) increase disconnection risk.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Pilha TCP/IPv4 e TCP/IPv6 do Windows 10 e 11.",
            HardwareOrOsDependencyEn: "Windows 10 and 11 TCP/IPv4 and TCP/IPv6 stack.",
            DefaultWindowsBehaviorPt: "Ausente por padrão no Registro com padrão interno documentado pela Microsoft igual a 5 retransmissões.",
            DefaultWindowsBehaviorEn: "Absent by default in the Registry with a Microsoft-documented internal default of 5 retransmissions.",
            RecommendationStatus: TweakRecommendationStatus.NotRecommendedOrDefault,
            IsRecommendedForApply: false,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.AbsentWithKnownDefault,
            BenefitConditionalityPt: "Focado em estabilidade de conexão (não reduz ping): só produz mudança real se o valor tiver sido alterado manualmente antes para um número diferente de 5.",
            BenefitConditionalityEn: "Focused on connection stability (does not lower ping): only produces a real change if the value was previously altered away from 5."
        ),

        ["priv-advertising-id"] = new(
            TweakId: "priv-advertising-id",
            Hive: "HKCU",
            SubKeyPath: @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            ValueName: "Enabled",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Desativar Identificador de Anúncios do Perfil (AdvertisingInfo Enabled = 0)",
            DescriptionEn: "Disable User Profile Advertising ID (AdvertisingInfo Enabled = 0)",
            Category: "Privacy",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Identificador de Anúncios do Usuário (Advertising ID)",
            FriendlyNameEn: "User Advertising ID (AdvertisingInfo Enabled)",
            WhatItDoesPt: "Desativa o uso do identificador de publicidade (Advertising ID) por aplicativos da Microsoft Store para exibir anúncios personalizados.",
            WhatItDoesEn: "Disables the use of the Advertising ID by Microsoft Store apps for cross-app personalized advertising.",
            ProblemSolvedPt: "Impede o rastreamento de interesses por ID de anúncio no perfil de usuário atual.",
            ProblemSolvedEn: "Prevents interest tracking via Advertising ID in the current user profile.",
            ExpectedBenefitPt: "Melhora a privacidade do usuário (equivalente à opção em Configurações > Privacidade e segurança > Geral), sem impacto em desempenho ou FPS.",
            ExpectedBenefitEn: "Improves user privacy (equivalent to the toggle in Settings > Privacy & security > General), with no impact on performance or FPS.",
            PotentialSideEffectsPt: "Nenhum impacto em desempenho ou estabilidade do sistema.",
            PotentialSideEffectsEn: "No impact on system performance or stability.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKCU).",
            DefaultWindowsBehaviorPt: "Dependente da escolha de privacidade feita pelo usuário na configuração inicial do Windows (OOBE): 1 se permitido, 0 se recusado.",
            DefaultWindowsBehaviorEn: "Depends on the privacy choice made by the user during initial Windows setup (OOBE): 1 if allowed, 0 if declined.",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.EditionOrConfigDependent,
            BenefitConditionalityPt: "Ajuste exclusivamente de privacidade: não altera desempenho de CPU, memória, disco ou rede.",
            BenefitConditionalityEn: "Strictly a privacy setting: does not alter CPU, memory, disk, or network performance."
        ),

        ["maint-startup-delay"] = new(
            TweakId: "maint-startup-delay",
            Hive: "HKCU",
            SubKeyPath: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
            ValueName: "StartupDelayInMSec",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1000",
            AllowedValues: new[] { "0", "1000" },
            Description: "Remover Atraso de Inicialização de Apps do Explorer (StartupDelayInMSec = 0)",
            DescriptionEn: "Remove Explorer Startup App Delay (StartupDelayInMSec = 0)",
            Category: "Startup",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Atraso de Inicialização de Aplicativos do Explorer (StartupDelayInMSec)",
            FriendlyNameEn: "Explorer Startup Application Delay (StartupDelayInMSec)",
            WhatItDoesPt: "Define StartupDelayInMSec = 0 na subchave Serialize do Windows Explorer para reduzir a espera antes do disparo de aplicativos de inicialização da bandeja após o logon.",
            WhatItDoesEn: "Sets StartupDelayInMSec = 0 under the Windows Explorer Serialize subkey to reduce the wait before launching tray startup apps after logon.",
            ProblemSolvedPt: "Reduz a espera artificial do shell antes de disparar os programas de inicialização do usuário.",
            ProblemSolvedEn: "Reduces artificial shell wait time before launching user startup programs.",
            ExpectedBenefitPt: "Pode antecipar a abertura de utilitários de inicialização após o logon em computadores equipados com SSD.",
            ExpectedBenefitEn: "Can speed up the launch of startup utilities after logon on SSD-equipped computers.",
            PotentialSideEffectsPt: "Em discos rígidos mecânicos (HDD), iniciar todos os programas ao mesmo tempo pode causar concorrência de leitura no logon.",
            PotentialSideEffectsEn: "On mechanical hard drives (HDD), launching all startup apps simultaneously may cause disk read contention at logon.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Recomendado apenas para sistemas instalados em SSD SATA ou NVMe.",
            HardwareOrOsDependencyEn: "Recommended only for systems installed on SATA or NVMe SSDs.",
            DefaultWindowsBehaviorPt: "Ausente por padrão no Registro (a subchave Serialize não existe em instalações limpas e o atraso interno é gerenciado dinamicamente pelo Explorer sem valor fixo documentado).",
            DefaultWindowsBehaviorEn: "Absent by default in the Registry (the Serialize subkey does not exist on clean installs and internal delay is dynamically managed by Explorer without a documented fixed value).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true,
            DefaultBehaviorKind: TweakDefaultBehaviorKind.UndeterminedWhenAbsent,
            BenefitConditionalityPt: "Condicional ao armazenamento e quantidade de apps no logon: afeta apenas o momento de abertura de programas após entrar na área de trabalho.",
            BenefitConditionalityEn: "Storage and startup-app dependent: only affects the timing of startup program launches after entering the desktop."
        )
    };

    /// <summary>
    /// Strict exact-key whitelist check.
    /// Does NOT allow arbitrary child subkeys under an allowed key.
    /// </summary>
    public static bool IsSubkeyAllowed(string subKeyPath) => IsPathAllowed(subKeyPath);

    public static bool IsPathAllowed(string subKeyPath)
    {
        if (string.IsNullOrWhiteSpace(subKeyPath)) return false;

        string normalized = NormalizeSubKeyPath(subKeyPath);
        if (normalized.Contains("..") || normalized.Contains("/")) return false;

        foreach (var forbidden in ForbiddenPathFragments)
        {
            if (normalized.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Exact match only — prevents an allowed parent key from granting access to child subkeys
        return _allowedSubkeys.Contains(normalized);
    }

    /// <summary>
    /// Resolves a TweakDefinition strictly when Hive (if specified), SubKeyPath, and ValueName match an authorized catalog entry.
    /// Prevents modifying unapproved values inside an otherwise allowed Registry subkey.
    /// </summary>
    public static TweakDefinition? FindAuthorizedCatalogEntry(string keyPath, string valueName)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName))
            return null;

        string normalizedSubKey = NormalizeSubKeyPath(keyPath);
        string? explicitHive = ExtractExplicitHive(keyPath);

        foreach (var def in Catalog.Values)
        {
            if (string.Equals(def.SubKeyPath, normalizedSubKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(def.ValueName, valueName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (explicitHive == null || string.Equals(def.Hive, explicitHive, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }
        }

        return null;
    }

    public static string? ExtractExplicitHive(string fullPath)
    {
        string trimmed = fullPath.Trim();
        if (trimmed.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            return "HKLM";
        if (trimmed.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            return "HKCU";
        return null;
    }

    public static string NormalizeSubKeyPath(string fullPath)
    {
        string trimmed = fullPath.Trim().TrimStart('\\');
        if (trimmed.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKEY_LOCAL_MACHINE\".Length).TrimEnd('\\');
        if (trimmed.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKLM\".Length).TrimEnd('\\');
        if (trimmed.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKEY_CURRENT_USER\".Length).TrimEnd('\\');
        if (trimmed.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKCU\".Length).TrimEnd('\\');
        return trimmed.TrimEnd('\\');
    }

    public static RegistryHive ParseRegistryHive(string fullPathOrHive)
    {
        if (fullPathOrHive.StartsWith(@"HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
            fullPathOrHive.StartsWith(@"HKCU", StringComparison.OrdinalIgnoreCase))
        {
            return RegistryHive.CurrentUser;
        }
        return RegistryHive.LocalMachine;
    }

    public static RegistryKey GetRootKey(string fullPathOrHive)
    {
        return ParseRegistryHive(fullPathOrHive) == RegistryHive.CurrentUser
            ? Registry.CurrentUser
            : Registry.LocalMachine;
    }

    public static string? SerializeValue(object? rawValue, RegistryValueKind kind) =>
        SerializeRegistryValue(rawValue, kind);

    public static object? DeserializeValue(string? serialized, RegistryValueKind kind) =>
        DeserializeRegistryValue(serialized, kind);

    public static string? SerializeRegistryValue(object? rawValue, RegistryValueKind kind)
    {
        if (rawValue == null) return null;

        try
        {
            return kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(rawValue).ToString(),
                RegistryValueKind.QWord => Convert.ToInt64(rawValue).ToString(),
                RegistryValueKind.String => rawValue.ToString() ?? string.Empty,
                RegistryValueKind.ExpandString => rawValue.ToString() ?? string.Empty,
                RegistryValueKind.MultiString => rawValue is string[] arr
                    ? JsonSerializer.Serialize(arr)
                    : JsonSerializer.Serialize(new[] { rawValue.ToString() ?? string.Empty }),
                RegistryValueKind.Binary => rawValue is byte[] bytes
                    ? Convert.ToBase64String(bytes)
                    : rawValue.ToString() ?? string.Empty,
                _ => rawValue.ToString()
            };
        }
        catch
        {
            return rawValue.ToString();
        }
    }

    public static bool TryDeserializeRegistryValue(
        string? serialized,
        RegistryValueKind kind,
        out object? typedValue,
        out string? errorMessage)
    {
        typedValue = null;
        errorMessage = null;

        if (serialized == null)
        {
            errorMessage = "Valor serializado nulo.";
            return false;
        }

        try
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                {
                    string s = serialized.Trim();
                    if (int.TryParse(s, out int signedVal))
                    {
                        typedValue = signedVal;
                        return true;
                    }
                    if (uint.TryParse(s, out uint unsignedVal))
                    {
                        typedValue = unchecked((int)unsignedVal);
                        return true;
                    }
                    errorMessage = $"Valor '{serialized}' não é um inteiro de 32 bits válido para REG_DWORD.";
                    return false;
                }
                case RegistryValueKind.QWord:
                {
                    string s = serialized.Trim();
                    if (long.TryParse(s, out long signedQ))
                    {
                        typedValue = signedQ;
                        return true;
                    }
                    if (ulong.TryParse(s, out ulong unsignedQ))
                    {
                        typedValue = unchecked((long)unsignedQ);
                        return true;
                    }
                    errorMessage = $"Valor '{serialized}' não é um inteiro de 64 bits válido para REG_QWORD.";
                    return false;
                }
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    typedValue = serialized;
                    return true;
                case RegistryValueKind.MultiString:
                {
                    string trimmed = serialized.Trim();
                    if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.EndsWith("]", StringComparison.Ordinal))
                    {
                        errorMessage = "Valor para REG_MULTI_SZ deve ser um array JSON válido.";
                        return false;
                    }
                    string[]? arr = JsonSerializer.Deserialize<string[]>(trimmed);
                    if (arr == null)
                    {
                        errorMessage = "Array JSON inválido para REG_MULTI_SZ.";
                        return false;
                    }
                    typedValue = arr;
                    return true;
                }
                case RegistryValueKind.Binary:
                {
                    typedValue = Convert.FromBase64String(serialized.Trim());
                    return true;
                }
                default:
                    errorMessage = $"Tipo de Registro '{kind}' não suportado.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Falha ao converter valor para '{kind}': {ex.Message}";
            return false;
        }
    }

    public static object DeserializeRegistryValue(string? serialized, RegistryValueKind kind)
    {
        if (TryDeserializeRegistryValue(serialized, kind, out object? val, out _) && val != null)
        {
            return val;
        }

        return kind switch
        {
            RegistryValueKind.DWord => ParseDWordString(serialized),
            RegistryValueKind.QWord => long.TryParse(serialized, out long q) ? q : 0L,
            RegistryValueKind.String => serialized ?? string.Empty,
            RegistryValueKind.ExpandString => serialized ?? string.Empty,
            RegistryValueKind.MultiString => Array.Empty<string>(),
            RegistryValueKind.Binary => Array.Empty<byte>(),
            _ => serialized ?? string.Empty
        };
    }

    private static int ParseDWordString(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)) return 0;
        string s = serialized.Trim();
        if (int.TryParse(s, out int signedVal)) return signedVal;
        if (uint.TryParse(s, out uint unsignedVal)) return unchecked((int)unsignedVal);
        return 0;
    }

    public static bool ValuesMatch(object? readBack, object expectedTyped, RegistryValueKind kind)
    {
        if (readBack == null) return false;

        try
        {
            return kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(readBack) == Convert.ToInt32(expectedTyped),
                RegistryValueKind.QWord => Convert.ToInt64(readBack) == Convert.ToInt64(expectedTyped),
                RegistryValueKind.String or RegistryValueKind.ExpandString =>
                    string.Equals(readBack.ToString(), expectedTyped.ToString(), StringComparison.Ordinal),
                RegistryValueKind.MultiString =>
                    readBack is string[] rArr && expectedTyped is string[] eArr && rArr.SequenceEqual(eArr),
                RegistryValueKind.Binary =>
                    readBack is byte[] rBytes && expectedTyped is byte[] eBytes && rBytes.SequenceEqual(eBytes),
                _ => Equals(readBack, expectedTyped)
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Compares two serialized values according to the expected RegistryValueKind (handling signed/unsigned 32-bit DWORD equivalence like -1 == 4294967295).
    /// </summary>
    public static bool SerializedValuesEqualForKind(string? actualSerialized, string? expectedSerialized, RegistryValueKind kind)
    {
        if (actualSerialized == null || expectedSerialized == null) return false;
        if (kind == RegistryValueKind.DWord)
        {
            return ParseDWordString(actualSerialized) == ParseDWordString(expectedSerialized);
        }
        if (kind == RegistryValueKind.QWord)
        {
            return long.TryParse(actualSerialized.Trim(), out long a) &&
                   long.TryParse(expectedSerialized.Trim(), out long b) &&
                   a == b;
        }
        return string.Equals(actualSerialized.Trim(), expectedSerialized.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Primary catalog-enforced entry point.
    /// All Registry changes must go through a valid TweakId in Catalog and an allowed value for that tweak.
    /// </summary>
    public static RegistryOperationResult ApplyCatalogTweak(string tweakId, string? requestedValue = null)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            RecordAudit("APPLY_REJECTED", tweakId ?? "NULL", "N/A", "N/A", "Unknown", null, requestedValue, null, false,
                $"Ajuste '{tweakId}' não encontrado no catálogo fechado autorizado.");
            return new RegistryOperationResult(
                false, null, null, "Unknown", false, false,
                $"Ajuste '{tweakId}' não encontrado no catálogo fechado autorizado.",
                tweakId
            );
        }

        string targetVal = string.IsNullOrWhiteSpace(requestedValue) ? def.RecommendedValue : requestedValue!.Trim();
        if (!def.AllowedValues.Contains(targetVal, StringComparer.OrdinalIgnoreCase))
        {
            string fullKey = $"{def.Hive}\\{def.SubKeyPath}";
            RecordAudit("APPLY_REJECTED_VALUE", def.TweakId, fullKey, def.ValueName, def.ValueKind.ToString(),
                null, targetVal, null, false, $"Valor '{targetVal}' não está na lista de valores permitidos para o ajuste '{def.TweakId}'.");
            return new RegistryOperationResult(
                false, null, null, def.ValueKind.ToString(), false, false,
                $"Valor '{targetVal}' não está na lista de valores permitidos para o ajuste '{def.TweakId}'.",
                def.TweakId
            );
        }

        return ExecuteAuthorizedWrite(def, targetVal, ActiveProbe);
    }

    public static RegistryOperationResult TryApplyTypedValue(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        RegistryValueKind targetKind,
        string serializedTargetValue)
    {
        string prefix = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
        return TryApplyTypedValue($"{prefix}\\{subKeyPath.TrimStart('\\')}", valueName, targetKind, serializedTargetValue);
    }

    /// <summary>
    /// Enforces the same closed-catalog security policy on TryApplyTypedValue:
    /// Rejects any keyPath, valueName, valueKind, or value not matching an authorized TweakDefinition in Catalog.
    /// </summary>
    public static RegistryOperationResult TryApplyTypedValue(
        string keyPath,
        string valueName,
        RegistryValueKind targetKind,
        string serializedTargetValue)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName))
        {
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                "Caminho da chave ou nome do valor inválido.");
        }

        string normalizedSubKey = NormalizeSubKeyPath(keyPath);
        if (!IsPathAllowed(normalizedSubKey))
        {
            RecordAudit("APPLY_BLOCKED_PATH", "UNCATALOGED", keyPath, valueName, targetKind.ToString(),
                null, serializedTargetValue, null, false, $"Acesso bloqueado: '{keyPath}' não pertence ao catálogo fechado.");
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                $"Acesso bloqueado: O caminho '{keyPath}' não pertence ao catálogo fechado de Registro.");
        }

        var def = FindAuthorizedCatalogEntry(keyPath, valueName);
        if (def == null)
        {
            RecordAudit("APPLY_BLOCKED_UNCATALOGED", "UNCATALOGED", keyPath, valueName, targetKind.ToString(),
                null, serializedTargetValue, null, false,
                $"Bloqueado: O par '{keyPath}\\{valueName}' não consta no catálogo fechado de ajustes.");
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                $"Operação rejeitada: '{keyPath}\\{valueName}' não pertence ao catálogo fechado de ajustes.");
        }

        if (def.ValueKind != targetKind)
        {
            RecordAudit("APPLY_BLOCKED_TYPE", def.TweakId, keyPath, valueName, targetKind.ToString(),
                null, serializedTargetValue, null, false,
                $"Tipo '{targetKind}' inválido para o ajuste '{def.TweakId}' (esperado: '{def.ValueKind}').");
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                $"Tipo '{targetKind}' inválido para o ajuste '{def.TweakId}' (esperado: '{def.ValueKind}').",
                def.TweakId);
        }

        if (!def.AllowedValues.Contains(serializedTargetValue, StringComparer.OrdinalIgnoreCase))
        {
            RecordAudit("APPLY_BLOCKED_VALUE", def.TweakId, keyPath, valueName, targetKind.ToString(),
                null, serializedTargetValue, null, false,
                $"Valor '{serializedTargetValue}' não está na lista de valores permitidos para '{def.TweakId}'.");
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                $"Valor '{serializedTargetValue}' não está na lista de valores permitidos para o ajuste '{def.TweakId}'.",
                def.TweakId);
        }

        return ExecuteAuthorizedWrite(def, serializedTargetValue, ActiveProbe);
    }

    public static bool TryApplyDword(string keyPath, string valueName, int targetValue)
    {
        return TryApplyTypedValue(keyPath, valueName, RegistryValueKind.DWord, targetValue.ToString()).Success;
    }

    public static bool TryApplyDword(string keyPath, string valueName, int targetValue, out object? previousValue)
    {
        var result = TryApplyTypedValue(keyPath, valueName, RegistryValueKind.DWord, targetValue.ToString());
        previousValue = result.PreviousValue;
        return result.Success;
    }

    private static RegistryOperationResult ExecuteAuthorizedWrite(
        TweakDefinition def,
        string serializedTargetValue,
        IRegistryKeyProbe probe)
    {
        string fullKeyPath = $"{def.Hive}\\{def.SubKeyPath}";
        RegistryValueKind targetKind = def.ValueKind;

        if (!probe.IsSupportedPlatform)
        {
            RecordAudit("APPLY_UNSUPPORTED_OS", def.TweakId, fullKeyPath, def.ValueName, targetKind.ToString(),
                null, serializedTargetValue, null, false, "Operação de Registro requer plataforma Windows nativa.");
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                "Operação de Registro requer plataforma Windows nativa.", def.TweakId);
        }

        string? previousSerialized = null;
        RegistryValueKind previousKind = targetKind;
        bool wasInitiallyAbsent = false;
        bool keyCreatedByApp = false;

        // 1. Pre-read original value & type before modification
        var preRead = probe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);
        if (preRead.Outcome == RegistryProbeReadOutcome.AccessDenied)
        {
            string msg = preRead.ErrorMessage ?? $"Permissão negada ao consultar {fullKeyPath} antes da escrita.";
            RecordAudit("APPLY_SECURITY_DENIED", def.TweakId, fullKeyPath, def.ValueName, targetKind.ToString(),
                null, serializedTargetValue, null, false, msg);
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false, msg, def.TweakId, null, "ACCESS_DENIED");
        }

        if (preRead.Outcome == RegistryProbeReadOutcome.KeyNotFound)
        {
            wasInitiallyAbsent = true;
            keyCreatedByApp = true;
        }
        else if (preRead.Outcome == RegistryProbeReadOutcome.ValueNotFound)
        {
            wasInitiallyAbsent = true;
        }
        else if (preRead.Outcome == RegistryProbeReadOutcome.ValueFound)
        {
            previousKind = preRead.ActualValueKind;
            previousSerialized = preRead.SerializedValue;
        }

        object typedTarget = DeserializeRegistryValue(serializedTargetValue, targetKind);

        // 2. Write value with explicit RegistryValueKind
        if (!probe.TryWriteValue(def.Hive, def.SubKeyPath, def.ValueName, typedTarget, targetKind, out bool createdNow, out string? writeErr, out bool writeDenied))
        {
            string opName = writeDenied ? "APPLY_UAC_REQUIRED" : "APPLY_ERROR";
            RecordAudit(opName, def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                previousSerialized, serializedTargetValue, null, false, writeErr);
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, writeErr, def.TweakId, null, writeDenied ? "ACCESS_DENIED" : "WRITE_FAILED");
        }

        if (createdNow) keyCreatedByApp = true;

        // 3. Re-read and verify both value and kind immediately after write (§4)
        var verifyRead = probe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);
        if (verifyRead.Outcome != RegistryProbeReadOutcome.ValueFound)
        {
            string err = "Falha na verificação: o valor não foi encontrado no Registro após a tentativa de gravação.";
            RecordAudit("APPLY_VERIFY_FAIL", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                previousSerialized, serializedTargetValue, null, false, err);
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, err, def.TweakId, null, "VERIFY_NOT_FOUND");
        }

        RegistryValueKind verifiedKind = verifyRead.ActualValueKind;
        string? verifiedStr = verifyRead.SerializedValue;

        if (verifiedKind == targetKind && ValuesMatch(verifyRead.RawValue, typedTarget, targetKind))
        {
            // Record trusted pre-modification state in the DPAPI + HMAC-SHA256 backend ledger ONLY when verified
            RegistryLedgerEntry ledgerEntry;
            try
            {
                ledgerEntry = RegistryStateLedger.RecordAppliedTweakState(
                    def,
                    previousSerialized,
                    previousKind,
                    wasInitiallyAbsent,
                    keyCreatedByApp,
                    serializedTargetValue
                );
            }
            catch (Exception ex)
            {
                string persistErr = $"Falha ao persistir estado original no Ledger: {ex.Message}";
                RecordAudit("APPLY_LEDGER_PERSIST_FAIL", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                    wasInitiallyAbsent ? "<ABSENT>" : previousSerialized, serializedTargetValue, verifiedStr, false, persistErr);
                return new RegistryOperationResult(
                    false,
                    wasInitiallyAbsent ? null : previousSerialized,
                    verifiedStr,
                    verifiedKind.ToString(),
                    wasInitiallyAbsent,
                    keyCreatedByApp,
                    persistErr,
                    def.TweakId,
                    null,
                    ErrorCode: "LEDGER_PERSIST_FAILED",
                    PreviousKind: wasInitiallyAbsent ? null : previousKind
                );
            }

            RecordAudit("APPLY_SUCCESS", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                wasInitiallyAbsent ? "<ABSENT>" : previousSerialized, serializedTargetValue, verifiedStr, true, null);
            return new RegistryOperationResult(
                true,
                wasInitiallyAbsent ? null : previousSerialized,
                verifiedStr,
                verifiedKind.ToString(),
                wasInitiallyAbsent,
                keyCreatedByApp,
                null,
                def.TweakId,
                ledgerEntry.OperationId,
                ErrorCode: null,
                PreviousKind: wasInitiallyAbsent ? null : previousKind
            );
        }

        string mismatchErr = $"Verificação pós-escrita falhou: lido '{verifiedStr}' ({verifiedKind}), esperado '{serializedTargetValue}' ({targetKind}).";
        RecordAudit("APPLY_VERIFY_MISMATCH", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
            previousSerialized, serializedTargetValue, verifiedStr, false, mismatchErr);
        return new RegistryOperationResult(false, previousSerialized, verifiedStr,
            verifiedKind.ToString(), wasInitiallyAbsent, keyCreatedByApp, mismatchErr, def.TweakId, null, "VERIFY_MISMATCH");
    }

    /// <summary>
    /// Secure backend-only restoration entry point (Etapa 2).
    /// NEVER accepts client-provided originalValue, valueKind, wasInitiallyAbsent, or keyCreatedByApp.
    /// Restores exclusively from:
    /// 1) A verified snapshot file (when snapshotIdOrPath is provided), OR
    /// 2) The backend's DPAPI + HMAC-SHA256 RegistryStateLedger (by operationId or latest un-restored entry for tweakId).
    /// </summary>
    public static RegistryOperationResult RestoreCatalogTweakFromTrustedSource(
        string tweakId,
        string? operationId = null,
        string? snapshotIdOrPath = null)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            RecordAudit("RESTORE_REJECTED_ID", tweakId ?? "NULL", "N/A", "N/A", "Unknown",
                null, null, null, false, $"UNCATALOGED_TWEAK: Ajuste '{tweakId}' não pertence ao catálogo fechado.");
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                $"UNCATALOGED_TWEAK: Ajuste '{tweakId}' não pertence ao catálogo fechado.", tweakId, null, "UNCATALOGED_TWEAK");
        }

        // Source A: Restore from a verified backend snapshot
        if (!string.IsNullOrWhiteSpace(snapshotIdOrPath))
        {
            var snapReport = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                snapshotIdOrPath,
                confirmIncompleteBackup: true,
                singleTweakId: def.TweakId,
                rollbackOnPartialFailure: false
            );

            var itemReport = snapReport.ItemReports?.FirstOrDefault();
            if (!snapReport.Success)
            {
                string err = itemReport?.ErrorMessage ?? snapReport.ErrorMessage ?? "Falha ao restaurar a partir do snapshot.";
                RecordAudit("RESTORE_SNAPSHOT_REJECTED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                    def.ValueKind.ToString(), null, snapshotIdOrPath, null, false, err);
                return new RegistryOperationResult(false, null, null, def.ValueKind.ToString(), false, false, err, def.TweakId, null, "SNAPSHOT_RESTORE_FAILED");
            }

            return new RegistryOperationResult(
                true,
                null,
                itemReport?.RestoredValue,
                def.ValueKind.ToString(),
                false,
                false,
                null,
                def.TweakId
            );
        }

        // Source B: Restore from the backend's DPAPI + HMAC-SHA256 RegistryStateLedger
        var ledgerValidation = RegistryStateLedger.ResolveTrustedRestoreEntry(def.TweakId, operationId);
        if (!ledgerValidation.IsValid || ledgerValidation.Entry == null)
        {
            string errCode = ledgerValidation.ErrorCode ?? "NO_TRUSTED_BACKEND_HISTORY";
            string err = !string.IsNullOrWhiteSpace(ledgerValidation.ErrorReason)
                ? ledgerValidation.ErrorReason
                : $"{errCode}: Estado original confiável não encontrado no backend.";
            RecordAudit("RESTORE_LEDGER_BLOCKED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                def.ValueKind.ToString(), null, operationId, null, false, err);
            return new RegistryOperationResult(
                false, null, null, def.ValueKind.ToString(), false, false, err, def.TweakId, operationId, errCode
            );
        }

        var entry = ledgerValidation.Entry;
        var restoreRes = ValidateAndExecuteRestore(
            def,
            entry.OriginalRawValue,
            entry.OriginalValueKind,
            entry.WasInitiallyAbsent,
            entry.KeyCreatedByApp,
            entry.OperationId,
            ActiveProbe,
            isAuthenticatedLedgerEntry: true
        );

        if (restoreRes.Success)
        {
            bool persisted = RegistryStateLedger.MarkOperationRestored(entry.OperationId);
            if (!persisted)
            {
                string persistErr = "LEDGER_PERSIST_RESTORED_FAILED: O valor original foi restaurado no Registro, mas falhou ao persistir o estado restaurado no Ledger.";
                RecordAudit("RESTORE_LEDGER_PERSIST_FAIL", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                    restoreRes.ValueKind, restoreRes.PreviousValue, entry.OriginalRawValue, restoreRes.VerifiedValue, false, persistErr);
                return restoreRes with
                {
                    Success = false,
                    ErrorCode = "LEDGER_PERSIST_RESTORED_FAILED",
                    ErrorMessage = persistErr
                };
            }
        }

        return restoreRes;
    }

    public static RegistryOperationResult RestoreCatalogTweak(
        string tweakId,
        bool wasInitiallyAbsent,
        string? originalRawValue,
        RegistryValueKind originalKind,
        bool keyCreatedByApp = false)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            RecordAudit("RESTORE_REJECTED_ID", tweakId ?? "NULL", "N/A", "N/A", "Unknown",
                null, originalRawValue, null, false, $"Ajuste '{tweakId}' não pertence ao catálogo fechado.");
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                $"Ajuste '{tweakId}' não pertence ao catálogo fechado.", tweakId);
        }

        return RestoreCatalogTweakFromTrustedSource(def.TweakId);
    }

    public static RegistryOperationResult RestoreCatalogTweak(
        string tweakId,
        string? untrustedOriginalValue = null,
        string? untrustedValueKindStr = null,
        bool untrustedWasInitiallyAbsent = false,
        bool untrustedKeyCreatedByApp = false)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            RecordAudit("RESTORE_REJECTED_ID", tweakId ?? "NULL", "N/A", "N/A", "Unknown",
                null, untrustedOriginalValue, null, false, $"Ajuste '{tweakId}' não pertence ao catálogo fechado.");
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                $"Ajuste '{tweakId}' não pertence ao catálogo fechado.", tweakId);
        }

        var preCheck = ValidateRestoreParametersAgainstCatalog(def, untrustedOriginalValue, untrustedValueKindStr, untrustedWasInitiallyAbsent);
        if (!preCheck.isValid)
        {
            RecordAudit("RESTORE_REJECTED_RULES", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                untrustedValueKindStr ?? "Unknown", null, untrustedOriginalValue, null, false, preCheck.errorMessage);
            return new RegistryOperationResult(false, null, null, untrustedValueKindStr ?? "Unknown",
                untrustedWasInitiallyAbsent, untrustedKeyCreatedByApp, preCheck.errorMessage, def.TweakId);
        }

        return RestoreCatalogTweakFromTrustedSource(def.TweakId);
    }

    internal static RegistryOperationResult RestoreFromVerifiedSnapshotEntry(
        TweakDefinition def,
        RegistryValueEntry verifiedEntry)
    {
        var validation = ValidateRestoreParametersAgainstCatalog(
            def,
            verifiedEntry.RawValue,
            verifiedEntry.ValueKind,
            verifiedEntry.WasInitiallyAbsent
        );

        if (!validation.isValid)
        {
            RecordAudit("SNAPSHOT_ENTRY_REJECTED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                verifiedEntry.ValueKind, null, verifiedEntry.RawValue, null, false, validation.errorMessage);
            return new RegistryOperationResult(
                false, null, null, verifiedEntry.ValueKind,
                verifiedEntry.WasInitiallyAbsent, verifiedEntry.KeyCreatedByApp,
                validation.errorMessage, def.TweakId
            );
        }

        return ValidateAndExecuteRestore(
            def,
            verifiedEntry.RawValue,
            verifiedEntry.ValueKind,
            verifiedEntry.WasInitiallyAbsent,
            verifiedEntry.KeyCreatedByApp,
            null,
            ActiveProbe,
            isAuthenticatedLedgerEntry: false
        );
    }

    public static (bool isValid, string errorReason) ValidateRestoreParametersAgainstCatalog(
        string tweakId,
        string? originalRawValue,
        RegistryValueKind originalKind,
        bool wasInitiallyAbsent)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            return (false, $"UNCATALOGED_TWEAK: Ajuste '{tweakId}' não pertence ao catálogo fechado.");
        }

        var (isValid, _, errorMessage) = ValidateRestoreParametersAgainstCatalog(
            def,
            originalRawValue,
            originalKind.ToString(),
            wasInitiallyAbsent
        );

        return (isValid, errorMessage ?? string.Empty);
    }

    public static (bool isValid, RegistryValueKind parsedKind, string? errorMessage) ValidateRestoreParametersAgainstCatalog(
        TweakDefinition def,
        string? candidateRawValue,
        string? candidateKindStr,
        bool wasInitiallyAbsent)
    {
        RegistryValueKind parsedKind = def.ValueKind;
        if (!string.IsNullOrWhiteSpace(candidateKindStr))
        {
            if (!Enum.TryParse<RegistryValueKind>(candidateKindStr.Trim(), true, out parsedKind) || parsedKind != def.ValueKind)
            {
                return (false, RegistryValueKind.Unknown,
                    $"INCOMPATIBLE_VALUE_KIND: Tipo de Registro '{candidateKindStr}' é incompatível com o catálogo para '{def.TweakId}' (esperado: '{def.ValueKind}').");
            }
        }

        if (!wasInitiallyAbsent)
        {
            if (string.IsNullOrWhiteSpace(candidateRawValue))
            {
                return (false, parsedKind,
                    $"RESTORE_VALUE_NOT_ALLOWED: Valor de restauração ausente para o ajuste '{def.TweakId}' que não era originalmente inexistente.");
            }

            if (!def.AllowedValues.Contains(candidateRawValue.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return (false, parsedKind,
                    $"RESTORE_VALUE_NOT_ALLOWED: Valor restaurado '{candidateRawValue}' está fora de AllowedValues para o ajuste '{def.TweakId}'.");
            }
        }

        return (true, parsedKind, null);
    }

    public static RegistryOperationResult TryRestoreValue(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        bool wasInitiallyAbsent,
        string? originalRawValue,
        RegistryValueKind originalKind,
        bool keyCreatedByApp = false)
    {
        string prefix = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
        return TryRestoreValue(
            $"{prefix}\\{subKeyPath.TrimStart('\\')}",
            valueName,
            originalRawValue,
            originalKind.ToString(),
            wasInitiallyAbsent,
            keyCreatedByApp
        );
    }

    public static RegistryOperationResult TryRestoreValue(
        string keyPath,
        string valueName,
        string? originalSerializedValue,
        string? originalValueKindStr,
        bool wasInitiallyAbsent,
        bool keyCreatedByApp = false)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName))
        {
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                "Parâmetros de restauração inválidos.");
        }

        string normalizedSubKey = NormalizeSubKeyPath(keyPath);
        if (!IsPathAllowed(normalizedSubKey))
        {
            RecordAudit("RESTORE_BLOCKED_PATH", "UNCATALOGED", keyPath, valueName, originalValueKindStr ?? "Unknown",
                null, originalSerializedValue, null, false, $"Caminho '{keyPath}' bloqueado fora do catálogo fechado.");
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                $"Acesso bloqueado: A subchave '{keyPath}' está bloqueada fora do catálogo fechado.");
        }

        var def = FindAuthorizedCatalogEntry(keyPath, valueName);
        if (def == null)
        {
            RecordAudit("RESTORE_BLOCKED_UNCATALOGED", "UNCATALOGED", keyPath, valueName, originalValueKindStr ?? "Unknown",
                null, originalSerializedValue, null, false,
                $"Bloqueado: '{keyPath}\\{valueName}' não pertence ao catálogo fechado.");
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                $"Operação bloqueada: '{keyPath}\\{valueName}' não pertence ao catálogo fechado de ajustes.");
        }

        return RestoreCatalogTweak(def.TweakId, originalSerializedValue, originalValueKindStr, wasInitiallyAbsent, keyCreatedByApp);
    }

    private static RegistryOperationResult ValidateAndExecuteRestore(
        TweakDefinition def,
        string? originalSerializedValue,
        string? originalValueKindStr,
        bool wasInitiallyAbsent,
        bool keyCreatedByApp,
        string? operationId = null,
        IRegistryKeyProbe? probeOverride = null,
        bool isAuthenticatedLedgerEntry = false)
    {
        RegistryValueKind originalKind = def.ValueKind;
        object? typedOriginal = null;

        if (isAuthenticatedLedgerEntry)
        {
            if (!string.IsNullOrWhiteSpace(originalValueKindStr))
            {
                if (!Enum.TryParse<RegistryValueKind>(originalValueKindStr.Trim(), true, out originalKind) ||
                    !RegistryStateLedger.IsSupportedRegistryValueKind(originalKind))
                {
                    string err = $"INCOMPATIBLE_VALUE_KIND: Tipo de Registro '{originalValueKindStr}' inválido para '{def.TweakId}'.";
                    RecordAudit("RESTORE_VALIDATION_FAILED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                        originalValueKindStr ?? "Unknown", null, originalSerializedValue, null, false, err);
                    return new RegistryOperationResult(false, null, null, originalValueKindStr ?? "Unknown",
                        wasInitiallyAbsent, keyCreatedByApp, err, def.TweakId, operationId, "INCOMPATIBLE_VALUE_KIND");
                }
            }

            if (!wasInitiallyAbsent)
            {
                if (!TryDeserializeRegistryValue(originalSerializedValue, originalKind, out typedOriginal, out string? convErr) || typedOriginal == null)
                {
                    string err = $"INVALID_ORIGINAL_VALUE_FORMAT: {convErr}";
                    RecordAudit("RESTORE_VALIDATION_FAILED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                        originalKind.ToString(), null, originalSerializedValue, null, false, err);
                    return new RegistryOperationResult(false, null, null, originalKind.ToString(),
                        wasInitiallyAbsent, keyCreatedByApp, err, def.TweakId, operationId, "INVALID_ORIGINAL_VALUE_FORMAT");
                }
            }
        }
        else
        {
            var check = ValidateRestoreParametersAgainstCatalog(def, originalSerializedValue, originalValueKindStr, wasInitiallyAbsent);
            if (!check.isValid)
            {
                RecordAudit("RESTORE_VALIDATION_FAILED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                    originalValueKindStr ?? "Unknown", null, originalSerializedValue, null, false, check.errorMessage);
                return new RegistryOperationResult(false, null, null, originalValueKindStr ?? "Unknown",
                    wasInitiallyAbsent, keyCreatedByApp, check.errorMessage, def.TweakId, operationId);
            }
            originalKind = check.parsedKind;
            if (!wasInitiallyAbsent)
            {
                typedOriginal = DeserializeRegistryValue(originalSerializedValue, originalKind);
            }
        }

        string fullKeyPath = $"{def.Hive}\\{def.SubKeyPath}";
        var probe = probeOverride ?? ActiveProbe;

        if (!probe.IsSupportedPlatform)
        {
            string unsupportedErr = "Plataforma atual não suporta acesso nativo ao Registro do Windows para confirmar a restauração.";
            RecordAudit("RESTORE_UNSUPPORTED_OS", def.TweakId, fullKeyPath, def.ValueName,
                wasInitiallyAbsent ? "Deleted" : originalKind.ToString(), null, originalSerializedValue, null, false, unsupportedErr);
            return new RegistryOperationResult(
                false,
                null,
                null,
                wasInitiallyAbsent ? "Deleted" : originalKind.ToString(),
                wasInitiallyAbsent,
                keyCreatedByApp,
                unsupportedErr,
                def.TweakId,
                operationId,
                "UNSUPPORTED_PLATFORM"
            );
        }

        var preRead = probe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);
        string? currentBeforeRestore = preRead.Outcome == RegistryProbeReadOutcome.ValueFound
            ? preRead.SerializedValue
            : null;

        if (wasInitiallyAbsent || originalSerializedValue == null)
        {
            if (!probe.TryDeleteValue(def.Hive, def.SubKeyPath, def.ValueName, keyCreatedByApp, out string? delErr, out bool delDenied))
            {
                RecordAudit("RESTORE_ERROR", def.TweakId, fullKeyPath, def.ValueName, "Deleted",
                    currentBeforeRestore, "<DELETE>", null, false, delErr);
                return new RegistryOperationResult(false, originalSerializedValue, null, "Deleted",
                    true, keyCreatedByApp, delErr, def.TweakId, operationId, delDenied ? "ACCESS_DENIED" : "RESTORE_DELETE_FAILED");
            }

            // Re-read to verify that the value no longer exists (§4)
            // ONLY KeyNotFound or ValueNotFound can confirm absence. AccessDenied or ReadError must NEVER be treated as absence.
            var postDelRead = probe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);
            if (postDelRead.Outcome == RegistryProbeReadOutcome.ValueFound)
            {
                string err = $"Restauração não confirmada: o valor '{postDelRead.SerializedValue}' permaneceu presente no Registro após tentativa de remoção.";
                RecordAudit("RESTORE_VERIFY_FAIL", def.TweakId, fullKeyPath, def.ValueName, "Deleted",
                    currentBeforeRestore, "<DELETE>", postDelRead.SerializedValue, false, err);
                return new RegistryOperationResult(false, originalSerializedValue, postDelRead.SerializedValue, "Deleted",
                    true, keyCreatedByApp, err, def.TweakId, operationId, "RESTORE_NOT_CONFIRMED");
            }

            if (postDelRead.Outcome != RegistryProbeReadOutcome.KeyNotFound &&
                postDelRead.Outcome != RegistryProbeReadOutcome.ValueNotFound)
            {
                string err = $"Falha ao verificar ausência pós-restauração ({postDelRead.Outcome}): {postDelRead.ErrorMessage ?? "Não foi possível confirmar a remoção no Registro."}";
                RecordAudit("RESTORE_READ_VERIFY_FAIL", def.TweakId, fullKeyPath, def.ValueName, "Deleted",
                    currentBeforeRestore, "<DELETE>", null, false, err);
                return new RegistryOperationResult(false, originalSerializedValue, null, "Deleted",
                    true, keyCreatedByApp, err, def.TweakId, operationId, "RESTORE_READ_VERIFICATION_FAILED");
            }

            RecordAudit("RESTORE_DELETED_ABSENT", def.TweakId, fullKeyPath, def.ValueName, "Deleted",
                currentBeforeRestore, "<DELETE>", "REMOVED_ABSENT_VALUE", true, null);
            return new RegistryOperationResult(true, currentBeforeRestore, "REMOVED_ABSENT_VALUE", "Deleted",
                true, keyCreatedByApp, null, def.TweakId, operationId);
        }
        else
        {
            typedOriginal ??= DeserializeRegistryValue(originalSerializedValue, originalKind);

            if (!probe.TryWriteValue(def.Hive, def.SubKeyPath, def.ValueName, typedOriginal, originalKind, out _, out string? writeErr, out bool writeDenied))
            {
                RecordAudit("RESTORE_ERROR", def.TweakId, fullKeyPath, def.ValueName, originalKind.ToString(),
                    currentBeforeRestore, originalSerializedValue, null, false, writeErr);
                return new RegistryOperationResult(false, originalSerializedValue, null, originalKind.ToString(),
                    false, keyCreatedByApp, writeErr, def.TweakId, operationId, writeDenied ? "ACCESS_DENIED" : "RESTORE_WRITE_FAILED");
            }

            // Re-read to verify that the original value AND kind were restored (§4)
            var verifyRead = probe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);
            if (verifyRead.Outcome != RegistryProbeReadOutcome.ValueFound)
            {
                string errCode = verifyRead.Outcome is RegistryProbeReadOutcome.AccessDenied or RegistryProbeReadOutcome.ReadError
                    ? "RESTORE_READ_VERIFICATION_FAILED"
                    : "RESTORE_NOT_CONFIRMED";
                string err = $"Falha na releitura pós-restauração ({verifyRead.Outcome}): {verifyRead.ErrorMessage ?? "O valor restaurado não pôde ser lido."}";
                RecordAudit("RESTORE_READ_VERIFY_FAIL", def.TweakId, fullKeyPath, def.ValueName, originalKind.ToString(),
                    currentBeforeRestore, originalSerializedValue, null, false, err);
                return new RegistryOperationResult(false, currentBeforeRestore,
                    null, originalKind.ToString(), false, false, err, def.TweakId, operationId, errCode);
            }

            string? verifiedStr = verifyRead.SerializedValue;
            RegistryValueKind verifiedKind = verifyRead.ActualValueKind;

            if (verifiedKind == originalKind &&
                ValuesMatch(verifyRead.RawValue, typedOriginal, originalKind))
            {
                RecordAudit("RESTORE_SUCCESS", def.TweakId, fullKeyPath, def.ValueName, originalKind.ToString(),
                    currentBeforeRestore, originalSerializedValue, verifiedStr, true, null);
                return new RegistryOperationResult(
                    true,
                    currentBeforeRestore,
                    verifiedStr,
                    originalKind.ToString(),
                    false,
                    false,
                    null,
                    def.TweakId,
                    operationId
                );
            }

            string mismatchErr = $"Verificação pós-restauração não confirmada: valor atual '{verifiedStr ?? "<AUSENTE>"}' ({verifiedKind}) difere do original '{originalSerializedValue}' ({originalKind}).";
            RecordAudit("RESTORE_VERIFY_MISMATCH", def.TweakId, fullKeyPath, def.ValueName, originalKind.ToString(),
                currentBeforeRestore, originalSerializedValue, verifiedStr, false, mismatchErr);
            return new RegistryOperationResult(false, currentBeforeRestore,
                verifiedStr, verifiedKind.ToString(), false, false, mismatchErr, def.TweakId, operationId, "RESTORE_NOT_CONFIRMED");
        }
    }

    public static IReadOnlyList<RegistryAuditLogEntry> GetAuditLogs() => _auditHistory.ToArray();

    public static IReadOnlyList<RegistryAuditLogEntry> GetRecentAuditLogs(int maxCount = 50) =>
        _auditHistory.Reverse().Take(maxCount).ToList();

    /// <summary>
    /// Performs a single real inspection of a cataloged tweak against the provided IRegistryKeyProbe (or ActiveProbe),
    /// validating key existence, value existence, exact RegistryValueKind, and value content (§2).
    /// NEVER reports Active or Inactive if the RegistryValueKind does not match def.ValueKind, even if the numeric/string representation matches.
    /// Differentiates absent keys/values according to TweakDefaultBehaviorKind and classifies read/access/platform failures as Undeterminable State.
    /// </summary>
    public static TweakStateReport InspectCatalogTweak(
        string tweakId,
        IRegistryKeyProbe? probe = null,
        int? windowsBuildOverride = null)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
        {
            return new TweakStateReport(
                TweakId: tweakId ?? string.Empty,
                State: TweakInspectionState.ReadError,
                StateDisplayPt: "Erro de Leitura",
                StateDisplayEn: "Read Error",
                KeyExists: false,
                ValueExists: false,
                CurrentRawValue: null,
                ActualValueKind: null,
                ExpectedValueKind: RegistryValueKind.Unknown,
                RecommendedValue: string.Empty,
                DefaultOriginValue: string.Empty,
                IsTypeMatch: false,
                IsRecommendedValueMatch: false,
                DiagnosticMessagePt: $"O identificador '{tweakId}' não pertence ao catálogo fechado de ajustes.",
                DiagnosticMessageEn: $"Identifier '{tweakId}' does not belong to the closed tweak catalog.",
                DefaultBehaviorKind: TweakDefaultBehaviorKind.UndeterminedWhenAbsent,
                IsUndeterminable: true,
                StateClassificationPt: "Estado Não Determinável",
                StateClassificationEn: "Undeterminable State",
                VerificationSummaryPt: "Estado Não Determinável (Ajuste fora do catálogo autorizado)",
                VerificationSummaryEn: "Undeterminable State (Uncataloged tweak identifier)"
            );
        }

        var activeProbe = probe ?? ActiveProbe;
        if (!activeProbe.IsSupportedPlatform)
        {
            return new TweakStateReport(
                TweakId: def.TweakId,
                State: TweakInspectionState.Incompatible,
                StateDisplayPt: "Incompatível",
                StateDisplayEn: "Incompatible",
                KeyExists: false,
                ValueExists: false,
                CurrentRawValue: null,
                ActualValueKind: null,
                ExpectedValueKind: def.ValueKind,
                RecommendedValue: def.RecommendedValue,
                DefaultOriginValue: def.DefaultOriginValue,
                IsTypeMatch: false,
                IsRecommendedValueMatch: false,
                DiagnosticMessagePt: "Plataforma atual não suporta leitura nativa do Registro do Windows.",
                DiagnosticMessageEn: "Current platform does not support native Windows Registry inspection.",
                DefaultBehaviorKind: def.DefaultBehaviorKind,
                IsUndeterminable: true,
                StateClassificationPt: "Estado Não Determinável",
                StateClassificationEn: "Undeterminable State",
                VerificationSummaryPt: "Estado Não Determinável (Plataforma incompatível para leitura real do Registro)",
                VerificationSummaryEn: "Undeterminable State (Incompatible platform for native Registry read)"
            );
        }

        int currentBuild = windowsBuildOverride ?? activeProbe.CurrentWindowsBuild;
        if ((currentBuild > 0 && currentBuild < def.MinimumWindowsBuild) ||
            (def.MaximumWindowsBuild.HasValue && currentBuild > def.MaximumWindowsBuild.Value))
        {
            return new TweakStateReport(
                TweakId: def.TweakId,
                State: TweakInspectionState.Incompatible,
                StateDisplayPt: "Incompatível",
                StateDisplayEn: "Incompatible",
                KeyExists: false,
                ValueExists: false,
                CurrentRawValue: null,
                ActualValueKind: null,
                ExpectedValueKind: def.ValueKind,
                RecommendedValue: def.RecommendedValue,
                DefaultOriginValue: def.DefaultOriginValue,
                IsTypeMatch: false,
                IsRecommendedValueMatch: false,
                DiagnosticMessagePt: $"Ajuste incompatível com a build atual do Windows ({currentBuild}).",
                DiagnosticMessageEn: $"Tweak is incompatible with the current Windows build ({currentBuild}).",
                DefaultBehaviorKind: def.DefaultBehaviorKind,
                IsUndeterminable: true,
                StateClassificationPt: "Estado Não Determinável",
                StateClassificationEn: "Undeterminable State",
                VerificationSummaryPt: $"Estado Não Determinável (Incompatível com a build {currentBuild} do Windows)",
                VerificationSummaryEn: $"Undeterminable State (Incompatible with Windows build {currentBuild})"
            );
        }

        var readResult = activeProbe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);

        switch (readResult.Outcome)
        {
            case RegistryProbeReadOutcome.AccessDenied:
            {
                string errPt = readResult.ErrorMessage ?? $"O Windows negou permissão para consultar {def.Hive}\\{def.SubKeyPath}.";
                string errEn = readResult.ErrorMessage ?? $"Windows denied permission to read {def.Hive}\\{def.SubKeyPath}.";
                return new TweakStateReport(
                    TweakId: def.TweakId,
                    State: TweakInspectionState.AccessDenied,
                    StateDisplayPt: "Acesso Negado",
                    StateDisplayEn: "Access Denied",
                    KeyExists: true,
                    ValueExists: false,
                    CurrentRawValue: null,
                    ActualValueKind: null,
                    ExpectedValueKind: def.ValueKind,
                    RecommendedValue: def.RecommendedValue,
                    DefaultOriginValue: def.DefaultOriginValue,
                    IsTypeMatch: false,
                    IsRecommendedValueMatch: false,
                    DiagnosticMessagePt: errPt,
                    DiagnosticMessageEn: errEn,
                    DefaultBehaviorKind: def.DefaultBehaviorKind,
                    IsUndeterminable: true,
                    StateClassificationPt: "Estado Não Determinável",
                    StateClassificationEn: "Undeterminable State",
                    VerificationSummaryPt: $"Estado Não Determinável — Acesso Negado ({errPt})",
                    VerificationSummaryEn: $"Undeterminable State — Access Denied ({errEn})"
                );
            }

            case RegistryProbeReadOutcome.IncompatibleEnvironment:
            {
                string errPt = readResult.ErrorMessage ?? "Ambiente incompatível para consulta do Registro.";
                string errEn = readResult.ErrorMessage ?? "Incompatible environment for Registry query.";
                return new TweakStateReport(
                    TweakId: def.TweakId,
                    State: TweakInspectionState.Incompatible,
                    StateDisplayPt: "Incompatível",
                    StateDisplayEn: "Incompatible",
                    KeyExists: false,
                    ValueExists: false,
                    CurrentRawValue: null,
                    ActualValueKind: null,
                    ExpectedValueKind: def.ValueKind,
                    RecommendedValue: def.RecommendedValue,
                    DefaultOriginValue: def.DefaultOriginValue,
                    IsTypeMatch: false,
                    IsRecommendedValueMatch: false,
                    DiagnosticMessagePt: errPt,
                    DiagnosticMessageEn: errEn,
                    DefaultBehaviorKind: def.DefaultBehaviorKind,
                    IsUndeterminable: true,
                    StateClassificationPt: "Estado Não Determinável",
                    StateClassificationEn: "Undeterminable State",
                    VerificationSummaryPt: $"Estado Não Determinável — Incompatível ({errPt})",
                    VerificationSummaryEn: $"Undeterminable State — Incompatible ({errEn})"
                );
            }

            case RegistryProbeReadOutcome.ReadError:
            {
                string errPt = readResult.ErrorMessage ?? "Falha técnica ao ler a chave ou valor no Registro.";
                string errEn = readResult.ErrorMessage ?? "Technical failure while reading the Registry key or value.";
                return new TweakStateReport(
                    TweakId: def.TweakId,
                    State: TweakInspectionState.ReadError,
                    StateDisplayPt: "Erro de Leitura",
                    StateDisplayEn: "Read Error",
                    KeyExists: false,
                    ValueExists: false,
                    CurrentRawValue: null,
                    ActualValueKind: null,
                    ExpectedValueKind: def.ValueKind,
                    RecommendedValue: def.RecommendedValue,
                    DefaultOriginValue: def.DefaultOriginValue,
                    IsTypeMatch: false,
                    IsRecommendedValueMatch: false,
                    DiagnosticMessagePt: errPt,
                    DiagnosticMessageEn: errEn,
                    DefaultBehaviorKind: def.DefaultBehaviorKind,
                    IsUndeterminable: true,
                    StateClassificationPt: "Estado Não Determinável",
                    StateClassificationEn: "Undeterminable State",
                    VerificationSummaryPt: $"Estado Não Determinável — Erro de Leitura ({errPt})",
                    VerificationSummaryEn: $"Undeterminable State — Read Error ({errEn})"
                );
            }

            case RegistryProbeReadOutcome.KeyNotFound:
            {
                var (classPt, classEn, diagPt, diagEn, summaryPt, summaryEn) = BuildAbsenceClassification(def, keyExists: false);
                return new TweakStateReport(
                    TweakId: def.TweakId,
                    State: TweakInspectionState.NotConfigured,
                    StateDisplayPt: "Não Configurado",
                    StateDisplayEn: "Not Configured",
                    KeyExists: false,
                    ValueExists: false,
                    CurrentRawValue: null,
                    ActualValueKind: null,
                    ExpectedValueKind: def.ValueKind,
                    RecommendedValue: def.RecommendedValue,
                    DefaultOriginValue: def.DefaultOriginValue,
                    IsTypeMatch: false,
                    IsRecommendedValueMatch: false,
                    DiagnosticMessagePt: diagPt,
                    DiagnosticMessageEn: diagEn,
                    DefaultBehaviorKind: def.DefaultBehaviorKind,
                    IsUndeterminable: false,
                    StateClassificationPt: classPt,
                    StateClassificationEn: classEn,
                    VerificationSummaryPt: summaryPt,
                    VerificationSummaryEn: summaryEn
                );
            }

            case RegistryProbeReadOutcome.ValueNotFound:
            {
                var (classPt, classEn, diagPt, diagEn, summaryPt, summaryEn) = BuildAbsenceClassification(def, keyExists: true);
                return new TweakStateReport(
                    TweakId: def.TweakId,
                    State: TweakInspectionState.NotConfigured,
                    StateDisplayPt: "Não Configurado",
                    StateDisplayEn: "Not Configured",
                    KeyExists: true,
                    ValueExists: false,
                    CurrentRawValue: null,
                    ActualValueKind: null,
                    ExpectedValueKind: def.ValueKind,
                    RecommendedValue: def.RecommendedValue,
                    DefaultOriginValue: def.DefaultOriginValue,
                    IsTypeMatch: false,
                    IsRecommendedValueMatch: false,
                    DiagnosticMessagePt: diagPt,
                    DiagnosticMessageEn: diagEn,
                    DefaultBehaviorKind: def.DefaultBehaviorKind,
                    IsUndeterminable: false,
                    StateClassificationPt: classPt,
                    StateClassificationEn: classEn,
                    VerificationSummaryPt: summaryPt,
                    VerificationSummaryEn: summaryEn
                );
            }

            case RegistryProbeReadOutcome.ValueFound:
            default:
                RegistryValueKind actualKind = readResult.ActualValueKind;
                string? serialized = readResult.SerializedValue;

                // CRITICAL RULE (§2.6): Explicitly compare GetValueKind (actualKind) with def.ValueKind.
                // Never classify as Active or Inactive if the RegistryValueKind is incompatible!
                if (actualKind != def.ValueKind)
                {
                    return new TweakStateReport(
                        TweakId: def.TweakId,
                        State: TweakInspectionState.WrongType,
                        StateDisplayPt: "Tipo Incorreto",
                        StateDisplayEn: "Wrong Type",
                        KeyExists: true,
                        ValueExists: true,
                        CurrentRawValue: serialized,
                        ActualValueKind: actualKind,
                        ExpectedValueKind: def.ValueKind,
                        RecommendedValue: def.RecommendedValue,
                        DefaultOriginValue: def.DefaultOriginValue,
                        IsTypeMatch: false,
                        IsRecommendedValueMatch: false,
                        DiagnosticMessagePt: $"Tipo incorreto no Registro: encontrado '{actualKind}' (valor '{serialized}'), mas o catálogo exige '{def.ValueKind}'.",
                        DiagnosticMessageEn: $"Incorrect Registry value type: found '{actualKind}' (value '{serialized}'), but the catalog requires '{def.ValueKind}'.",
                        DefaultBehaviorKind: def.DefaultBehaviorKind,
                        IsUndeterminable: false,
                        StateClassificationPt: "Tipo Incorreto",
                        StateClassificationEn: "Wrong Type",
                        VerificationSummaryPt: $"Alerta de Tipo Incorreto: valor '{serialized}' gravado como {actualKind} (esperado {def.ValueKind}). Não classificado como Ativo ou Inativo.",
                        VerificationSummaryEn: $"Wrong Type Warning: value '{serialized}' stored as {actualKind} (expected {def.ValueKind}). Not classified as Active or Inactive."
                    );
                }

                if (SerializedValuesEqualForKind(serialized, def.RecommendedValue, def.ValueKind))
                {
                    bool isAlsoDefault = SerializedValuesEqualForKind(def.RecommendedValue, def.DefaultOriginValue, def.ValueKind);
                    string summaryPt = isAlsoDefault
                        ? $"Confirmado no Registro: {def.ValueName} = {serialized} ({actualKind}) — corresponde ao recomendado e ao padrão oficial do Windows."
                        : $"Confirmado no Registro: {def.ValueName} = {serialized} ({actualKind}) — ajuste recomendado ativo.";
                    string summaryEn = isAlsoDefault
                        ? $"Verified in Registry: {def.ValueName} = {serialized} ({actualKind}) — matches both recommended and official Windows default."
                        : $"Verified in Registry: {def.ValueName} = {serialized} ({actualKind}) — recommended tweak is active.";

                    return new TweakStateReport(
                        TweakId: def.TweakId,
                        State: TweakInspectionState.Active,
                        StateDisplayPt: "Ativo",
                        StateDisplayEn: "Active",
                        KeyExists: true,
                        ValueExists: true,
                        CurrentRawValue: serialized,
                        ActualValueKind: actualKind,
                        ExpectedValueKind: def.ValueKind,
                        RecommendedValue: def.RecommendedValue,
                        DefaultOriginValue: def.DefaultOriginValue,
                        IsTypeMatch: true,
                        IsRecommendedValueMatch: true,
                        DiagnosticMessagePt: $"Ativo e confirmado no Registro ({def.ValueName} = {serialized}, tipo {actualKind}).",
                        DiagnosticMessageEn: $"Active and verified in the Registry ({def.ValueName} = {serialized}, type {actualKind}).",
                        DefaultBehaviorKind: def.DefaultBehaviorKind,
                        IsUndeterminable: false,
                        StateClassificationPt: "Ativo (Confirmado no Registro)",
                        StateClassificationEn: "Active (Verified in Registry)",
                        VerificationSummaryPt: summaryPt,
                        VerificationSummaryEn: summaryEn
                    );
                }

                if (SerializedValuesEqualForKind(serialized, def.DefaultOriginValue, def.ValueKind))
                {
                    return new TweakStateReport(
                        TweakId: def.TweakId,
                        State: TweakInspectionState.Inactive,
                        StateDisplayPt: "Inativo",
                        StateDisplayEn: "Inactive",
                        KeyExists: true,
                        ValueExists: true,
                        CurrentRawValue: serialized,
                        ActualValueKind: actualKind,
                        ExpectedValueKind: def.ValueKind,
                        RecommendedValue: def.RecommendedValue,
                        DefaultOriginValue: def.DefaultOriginValue,
                        IsTypeMatch: true,
                        IsRecommendedValueMatch: false,
                        DiagnosticMessagePt: $"Inativo: valor atual ({serialized}, {actualKind}) corresponde ao valor de referência ({def.DefaultOriginValue}).",
                        DiagnosticMessageEn: $"Inactive: current value ({serialized}, {actualKind}) matches the reference value ({def.DefaultOriginValue}).",
                        DefaultBehaviorKind: def.DefaultBehaviorKind,
                        IsUndeterminable: false,
                        StateClassificationPt: "Inativo (Valor de Referência)",
                        StateClassificationEn: "Inactive (Reference Value)",
                        VerificationSummaryPt: $"Confirmado no Registro: {def.ValueName} = {serialized} ({actualKind}) — ajuste inativo (valor de referência {def.DefaultOriginValue}).",
                        VerificationSummaryEn: $"Verified in Registry: {def.ValueName} = {serialized} ({actualKind}) — tweak inactive (reference value {def.DefaultOriginValue})."
                    );
                }

                return new TweakStateReport(
                    TweakId: def.TweakId,
                    State: TweakInspectionState.Custom,
                    StateDisplayPt: "Personalizado",
                    StateDisplayEn: "Custom",
                    KeyExists: true,
                    ValueExists: true,
                    CurrentRawValue: serialized,
                    ActualValueKind: actualKind,
                    ExpectedValueKind: def.ValueKind,
                    RecommendedValue: def.RecommendedValue,
                    DefaultOriginValue: def.DefaultOriginValue,
                    IsTypeMatch: true,
                    IsRecommendedValueMatch: false,
                    DiagnosticMessagePt: $"Personalizado: encontrado valor '{serialized}' ({actualKind}), diferente do recomendado ({def.RecommendedValue}) e da referência ({def.DefaultOriginValue}).",
                    DiagnosticMessageEn: $"Custom: found value '{serialized}' ({actualKind}), different from recommended ({def.RecommendedValue}) and reference ({def.DefaultOriginValue}).",
                    DefaultBehaviorKind: def.DefaultBehaviorKind,
                    IsUndeterminable: false,
                    StateClassificationPt: "Personalizado (Valor Customizado)",
                    StateClassificationEn: "Custom (Non-Standard Value)",
                    VerificationSummaryPt: $"Confirmado no Registro: {def.ValueName} = {serialized} ({actualKind}) — valor personalizado fora do recomendado ({def.RecommendedValue}).",
                    VerificationSummaryEn: $"Verified in Registry: {def.ValueName} = {serialized} ({actualKind}) — custom value different from recommended ({def.RecommendedValue})."
                );
        }
    }

    private static (string classPt, string classEn, string diagPt, string diagEn, string summaryPt, string summaryEn) BuildAbsenceClassification(
        TweakDefinition def,
        bool keyExists)
    {
        string targetPt = keyExists
            ? $"Valor '{def.ValueName}' ausente na subchave {def.Hive}\\{def.SubKeyPath}"
            : $"Subchave '{def.Hive}\\{def.SubKeyPath}' ausente no Registro";
        string targetEn = keyExists
            ? $"Value '{def.ValueName}' is absent in subkey {def.Hive}\\{def.SubKeyPath}"
            : $"Subkey '{def.Hive}\\{def.SubKeyPath}' is absent in the Registry";

        return def.DefaultBehaviorKind switch
        {
            TweakDefaultBehaviorKind.AbsentWithKnownDefault => (
                classPt: "Ausente com Padrão Conhecido do Windows",
                classEn: "Absent with Known Windows Default",
                diagPt: $"{targetPt}. Por padrão esta entrada não existe no Registro e o Windows aplica o comportamento interno documentado ({def.DefaultOriginValue}).",
                diagEn: $"{targetEn}. By default this entry is absent in the Registry and Windows applies its documented internal behavior ({def.DefaultOriginValue}).",
                summaryPt: $"{targetPt} — Ausente com padrão interno conhecido do Windows ({def.DefaultOriginValue}).",
                summaryEn: $"{targetEn} — Absent with known internal Windows default ({def.DefaultOriginValue})."
            ),
            TweakDefaultBehaviorKind.EditionOrConfigDependent => (
                classPt: "Ausente (Dependente da Edição/Configuração)",
                classEn: "Absent (Edition/Configuration Dependent)",
                diagPt: $"{targetPt}. A ausência não representa um valor fixo universal: o comportamento efetivo depende da edição/versão do Windows, driver ou configuração do usuário.",
                diagEn: $"{targetEn}. Absence does not represent a universal fixed value: effective behavior depends on Windows edition/version, driver, or user configuration.",
                summaryPt: $"{targetPt} — Ausente com comportamento dependente da edição, versão ou configuração do sistema.",
                summaryEn: $"{targetEn} — Absent with behavior dependent on OS edition, version, or system configuration."
            ),
            TweakDefaultBehaviorKind.UndeterminedWhenAbsent => (
                classPt: "Ausente (Sem Comportamento Padrão Determinado)",
                classEn: "Absent (No Determined Standalone Default)",
                diagPt: $"{targetPt}. Entrada auxiliar/opcional ausente sem valor padrão universal independente determinado.",
                diagEn: $"{targetEn}. Optional/auxiliary entry is absent without an independently determined universal default value.",
                summaryPt: $"{targetPt} — Ausente sem comportamento padrão isolado suficientemente determinado.",
                summaryEn: $"{targetEn} — Absent without a sufficiently determined standalone default behavior."
            ),
            _ => (
                classPt: "Não Configurado (Valor Esperado Ausente)",
                classEn: "Not Configured (Expected Value Absent)",
                diagPt: $"{targetPt}. Em instalações padrão do Windows espera-se o valor documentado '{def.DefaultOriginValue}' ({def.ValueKind}).",
                diagEn: $"{targetEn}. On standard Windows installations the documented value '{def.DefaultOriginValue}' ({def.ValueKind}) is expected.",
                summaryPt: $"{targetPt} — Não configurado (valor documentado esperado: {def.DefaultOriginValue}).",
                summaryEn: $"{targetEn} — Not configured (expected documented value: {def.DefaultOriginValue})."
            )
        };
    }

    /// <summary>
    /// Inspects all catalog tweaks in a single pass (1 query per tweak) and returns their structured reports.
    /// </summary>
    public static IReadOnlyList<TweakStateReport> InspectAllCatalogTweaks(
        IRegistryKeyProbe? probe = null,
        int? windowsBuildOverride = null)
    {
        var results = new List<TweakStateReport>(Catalog.Count);
        foreach (var def in Catalog.Values)
        {
            results.Add(InspectCatalogTweak(def.TweakId, probe, windowsBuildOverride));
        }
        return results;
    }

    /// <summary>
    /// Returns the Portuguese display label for the current Registry state of a catalog tweak.
    /// Delegates to InspectCatalogTweak so type validation and all 8 states are strictly enforced.
    /// </summary>
    public static string InspectCatalogTweakCurrentState(string tweakId)
    {
        return InspectCatalogTweak(tweakId).StateDisplayPt;
    }

    private static string GetCurrentWindowsUser()
    {
        try
        {
            string domain = Environment.UserDomainName;
            string user = Environment.UserName;
            return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
        }
        catch
        {
            return "LocalUser";
        }
    }

    private static void RecordAudit(
        string operation,
        string tweakId,
        string keyPath,
        string valueName,
        string valueKind,
        string? previousValue,
        string? requestedValue,
        string? verifiedValue,
        bool success,
        string? errorMessage)
    {
        var entry = new RegistryAuditLogEntry(
            TimestampUtc: DateTime.UtcNow.ToString("O"),
            WindowsUser: GetCurrentWindowsUser(),
            Operation: operation,
            TweakId: tweakId,
            KeyPath: keyPath,
            ValueName: valueName,
            ValueKind: valueKind,
            PreviousValue: previousValue,
            RequestedValue: requestedValue,
            VerifiedValue: verifiedValue,
            Success: success,
            ErrorMessage: errorMessage
        );

        _auditHistory.Enqueue(entry);
        while (_auditHistory.Count > 250)
        {
            _auditHistory.TryDequeue(out _);
        }

        try
        {
            string? dir = Path.GetDirectoryName(AuditLogFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            File.AppendAllText(AuditLogFilePath, line, Encoding.UTF8);
        }
        catch { }
    }
}