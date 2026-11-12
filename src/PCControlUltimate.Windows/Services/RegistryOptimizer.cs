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
    string DiagnosticMessageEn
)
{
    public string StateBadgePt => StateDisplayPt.ToUpperInvariant();
    public string StateBadgeEn => StateDisplayEn.ToUpperInvariant();
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
    int? MaximumWindowsBuild = null
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
            ExpectedBenefitPt: "Permite que threads registradas no MMCSS utilizem até 90% dos ciclos de CPU em momentos de pico.",
            ExpectedBenefitEn: "Allows MMCSS-registered threads to utilize up to 90% of CPU cycles during peak contention.",
            PotentialSideEffectsPt: "Tarefas de segundo plano (como indexação ou sincronização) podem progredir mais lentamente enquanto um jogo estiver aberto.",
            PotentialSideEffectsEn: "Background tasks (such as indexing or file syncing) may progress slightly slower while a game is running.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Requer Administrador para gravar em HKLM).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires Administrator to write to HKLM).",
            DefaultWindowsBehaviorPt: "Padrão documentado pelo Windows Client: 20 (20% reservado para tarefas de fundo).",
            DefaultWindowsBehaviorEn: "Documented Windows Client default: 20 (20% reserved for background tasks).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            ExpectedBenefitPt: "Elimina o consumo de VRAM e de codificador de hardware causado pela gravação automática em segundo plano.",
            ExpectedBenefitEn: "Eliminates VRAM and hardware encoder overhead caused by automatic background recording.",
            PotentialSideEffectsPt: "Desativa o recurso de gravar os últimos segundos de jogo via atalho Win+Alt+G no Xbox Game Bar.",
            PotentialSideEffectsEn: "Disables the 'Record last X seconds' shortcut (Win+Alt+G) in Xbox Game Bar.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Escopo do usuário atual HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Current user scope HKCU).",
            DefaultWindowsBehaviorPt: "Quando ativo (1), permite captura em segundo plano pelo Game Bar.",
            DefaultWindowsBehaviorEn: "When enabled (1), allows background capture via Game Bar.",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            Description: "Separação de Prioridade de Primeiro Plano (Win32PrioritySeparation = 0x26 / 38)",
            DescriptionEn: "Foreground Thread Priority Separation (Win32PrioritySeparation = 0x26 / 38)",
            Category: "Performance",
            RiskLevel: "Médio",
            FriendlyNamePt: "Quantum de Prioridade de Processos em Primeiro Plano (Win32PrioritySeparation)",
            FriendlyNameEn: "Foreground Process Priority Quantum (Win32PrioritySeparation)",
            WhatItDoesPt: "Controla a duração do quantum de agendamento do processador e o reforço de prioridade atribuído aos threads da janela ativa em primeiro plano (NOTA: este ajuste altera Win32PrioritySeparation, não o timer HPET).",
            WhatItDoesEn: "Controls processor scheduling quantum length and priority boost given to threads of the active foreground window (NOTE: modifies Win32PrioritySeparation, not the HPET hardware timer).",
            ProblemSolvedPt: "Define intervalos de quantum curtos e fixos com proporção 3:1 a favor do aplicativo em primeiro plano (valor decimal 38 / 0x26 hex).",
            ProblemSolvedEn: "Configures short, fixed-length scheduling quantums with a 3:1 ratio favoring the foreground application (decimal 38 / 0x26 hex).",
            ExpectedBenefitPt: "Pode melhorar a responsividade da janela em foco sob carga concorrente de processos em segundo plano.",
            ExpectedBenefitEn: "Can improve responsiveness of the focused window when background processes compete for CPU time.",
            PotentialSideEffectsPt: "Processos em segundo plano (como codificadores de streaming OBS ou renderizações paralelas) recebem fatias menores de CPU.",
            PotentialSideEffectsEn: "Background processes (such as OBS streaming encoders or background renders) receive shorter CPU time slices.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKLM — requer privilégios de Administrador).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKLM — requires Administrator privileges).",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows Client: 2 (quantum variável curto com reforço padrão para primeiro plano).",
            DefaultWindowsBehaviorEn: "Official Windows Client default: 2 (short variable quantum with standard foreground boost).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            Description: "Desativar Limitação de Rede Multimídia (NetworkThrottlingIndex = 0xFFFFFFFF)",
            DescriptionEn: "Disable Multimedia Network Packet Throttling (NetworkThrottlingIndex = 0xFFFFFFFF)",
            Category: "Network",
            RiskLevel: "Médio",
            FriendlyNamePt: "Índice de Limitação de Rede do Subsistema Multimídia (NetworkThrottlingIndex)",
            FriendlyNameEn: "Multimedia Subsystem Network Throttling Index (NetworkThrottlingIndex)",
            WhatItDoesPt: "Controla o limite de pacotes de rede por milissegundo processados pelo driver NDIS enquanto aplicativos multimídia estão ativos (NOTA: modifica NetworkThrottlingIndex do MMCSS, não o algoritmo TCP Nagle).",
            WhatItDoesEn: "Controls the maximum network packets per millisecond processed by NDIS while multimedia apps are active (NOTE: modifies MMCSS NetworkThrottlingIndex, not the TCP Nagle algorithm).",
            ProblemSolvedPt: "Remove o teto padrão de 10 pacotes/ms imposto pelo MMCSS definindo 0xFFFFFFFF (-1 em DWORD com sinal).",
            ProblemSolvedEn: "Removes the default 10 packets/ms cap enforced by MMCSS by setting 0xFFFFFFFF (-1 in signed DWORD).",
            ExpectedBenefitPt: "Evita retenção artificial de pacotes de rede quando jogos e fluxos de áudio multimídia rodam simultaneamente.",
            ExpectedBenefitEn: "Prevents artificial network packet throttling when games and multimedia audio streams run simultaneously.",
            PotentialSideEffectsPt: "Segundo a documentação da Microsoft, desativar o throttling em redes Gigabit saturadas pode causar falhas em reprodução de áudio/vídeo de tempo real em CPUs antigas.",
            PotentialSideEffectsEn: "Per Microsoft documentation, disabling throttling on saturated Gigabit networks may cause real-time audio/video playback glitches on older CPUs.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Requer reinicialização para recarregar o perfil do MMCSS).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires reboot to reload the MMCSS profile).",
            DefaultWindowsBehaviorPt: "Padrão documentado pela Microsoft: 10 (10 pacotes por milissegundo).",
            DefaultWindowsBehaviorEn: "Microsoft documented default: 10 (10 packets per millisecond).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            Description: "Desativar Inicialização Rápida Híbrida (HiberbootEnabled = 0)",
            DescriptionEn: "Disable Hybrid Fast Startup (HiberbootEnabled = 0)",
            Category: "Startup",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Inicialização Rápida do Windows (Fast Startup / HiberbootEnabled)",
            FriendlyNameEn: "Windows Fast Startup (HiberbootEnabled)",
            WhatItDoesPt: "Desativa a hibernação híbrida da sessão do kernel ao desligar o computador, forçando um desligamento completo.",
            WhatItDoesEn: "Disables hybrid kernel session hibernation when shutting down the PC, enforcing a full shutdown.",
            ProblemSolvedPt: "Garante que o kernel NT e os drivers de vídeo/rede sejam reinicializados do zero ao ligar o PC.",
            ProblemSolvedEn: "Ensures the NT kernel and video/network drivers are freshly initialized when powering on the PC.",
            ExpectedBenefitPt: "Melhora a previsibilidade após atualizações de drivers e evita estado residual de uptime acumulado.",
            ExpectedBenefitEn: "Improves predictability after driver updates and avoids accumulated kernel uptime state.",
            PotentialSideEffectsPt: "O tempo de boot a frio pode aumentar em 2 a 5 segundos em comparação à retomada híbrida.",
            PotentialSideEffectsEn: "Cold boot time may increase by 2 to 5 seconds compared to hybrid resume.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 com hibernação habilitada.",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 with hibernation enabled.",
            DefaultWindowsBehaviorPt: "Padrão quando a hibernação está ativa: 1 (Inicialização Rápida ativada).",
            DefaultWindowsBehaviorEn: "Default when hibernation is enabled: 1 (Fast Startup enabled).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            DefaultWindowsBehaviorPt: "Por padrão, a subchave de política não existe e o usuário controla o recurso nas configurações do Edge.",
            DefaultWindowsBehaviorEn: "By default, the policy subkey is absent and the user controls the feature inside Edge settings.",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            FriendlyNamePt: "Nível de Dados de Diagnóstico do Windows (AllowTelemetry)",
            FriendlyNameEn: "Windows Diagnostic Data Level (AllowTelemetry)",
            WhatItDoesPt: "Configura a política de coleta de dados de diagnóstico do Windows para o nível 1 (Obrigatório / Básico), sem desativar serviços de segurança ou Windows Update.",
            WhatItDoesEn: "Configures the Windows diagnostic data collection policy to level 1 (Required / Basic), without disabling security services or Windows Update.",
            ProblemSolvedPt: "Reduz o envio de telemetria diagnóstica opcional e relatórios detalhados de uso em segundo plano.",
            ProblemSolvedEn: "Reduces optional diagnostic telemetry uploads and detailed usage reporting in the background.",
            ExpectedBenefitPt: "Menor atividade de coleta diagnóstica em segundo plano mantendo conformidade com o Windows Update.",
            ExpectedBenefitEn: "Lower background diagnostic collection activity while maintaining Windows Update compliance.",
            PotentialSideEffectsPt: "A página 'Diagnóstico e comentários' nas Configurações do Windows pode informar que algumas configurações são gerenciadas por política.",
            PotentialSideEffectsEn: "The 'Diagnostics & feedback' page in Windows Settings may state that some settings are managed by policy.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Em edições Home/Pro o nível mínimo suportado é 1).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (On Home/Pro editions the minimum supported level is 1).",
            DefaultWindowsBehaviorPt: "Se não configurado por política, segue a escolha feita na instalação (frequentemente 3 = Opcional/Completo).",
            DefaultWindowsBehaviorEn: "When not configured by policy, follows the OOBE choice (often 3 = Optional/Full).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            DefaultWindowsBehaviorPt: "Padrão no Windows 10: 0 (Mostrar ícone e texto).",
            DefaultWindowsBehaviorEn: "Default on Windows 10: 0 (Show icon and text).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            Description: "Desativar Limitação de Energia de Processos (PowerThrottlingOff = 1)",
            DescriptionEn: "Disable Process Execution Power Throttling (PowerThrottlingOff = 1)",
            Category: "Power",
            RiskLevel: "Médio",
            FriendlyNamePt: "Controle de Limitação de Energia de Processos (PowerThrottlingOff)",
            FriendlyNameEn: "Process Power Throttling Control (PowerThrottlingOff)",
            WhatItDoesPt: "Desativa globalmente o recurso Power Throttling (EcoQoS) do kernel do Windows para processos em segundo plano (NOTA: modifica PowerThrottlingOff, não cria nem ativa o plano de energia Ultimate Performance).",
            WhatItDoesEn: "Globally disables the Windows kernel Power Throttling (EcoQoS) feature for background processes (NOTE: modifies PowerThrottlingOff; does not create or activate the Ultimate Performance power plan).",
            ProblemSolvedPt: "Impede que o sistema reduza automaticamente a frequência de CPU ou aloque núcleos de eficiência para tarefas classificadas como segundo plano.",
            ProblemSolvedEn: "Prevents the OS from automatically lowering CPU frequency or forcing efficiency cores on tasks classified as background work.",
            ExpectedBenefitPt: "Útil em desktops quando aplicativos auxiliares de áudio/captura sofrem redução de clock em segundo plano.",
            ExpectedBenefitEn: "Useful on desktops when auxiliary audio/capture applications suffer clock throttling while in the background.",
            PotentialSideEffectsPt: "Aumenta o consumo de energia e reduz a autonomia de bateria em notebooks; pode interferir no agendamento de núcleos eficientes (E-Cores) em CPUs híbridas.",
            PotentialSideEffectsEn: "Increases power consumption and reduces battery life on laptops; may interfere with efficiency-core (E-Core) scheduling on hybrid CPUs.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Processadores com suporte a Intel Speed Shift / EcoQoS no Windows 10 e 11. Não recomendado para notebooks na bateria.",
            HardwareOrOsDependencyEn: "Processors supporting Intel Speed Shift / EcoQoS on Windows 10 and 11. Not recommended for laptops on battery.",
            DefaultWindowsBehaviorPt: "Por padrão a chave não existe (ou vale 0), mantendo o gerenciamento automático de eficiência energética ativo.",
            DefaultWindowsBehaviorEn: "By default the key is absent (or 0), keeping automatic power efficiency management active.",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            Description: "Categoria de Agendamento MMCSS para Jogos (Scheduling Category = High)",
            DescriptionEn: "MMCSS Games Task Scheduling Category (Scheduling Category = High)",
            Category: "Gaming",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Categoria de Prioridade MMCSS para Tarefas de Jogos (Scheduling Category)",
            FriendlyNameEn: "MMCSS Priority Category for Games Task (Scheduling Category)",
            WhatItDoesPt: "Define o atributo 'Scheduling Category' (REG_SZ) do perfil MMCSS 'Games' para 'High' (NOTA: altera a categoria do agendador multimídia MMCSS, não o gerenciamento de energia PCIe ASPM).",
            WhatItDoesEn: "Sets the 'Scheduling Category' (REG_SZ) attribute of the MMCSS 'Games' profile to 'High' (NOTE: modifies the MMCSS scheduler category, not PCIe ASPM power management).",
            ProblemSolvedPt: "Garante que threads de jogos registradas na tarefa MMCSS 'Games' recebam a categoria de prioridade 'High' em vez de 'Medium'.",
            ProblemSolvedEn: "Ensures game threads registered under the MMCSS 'Games' task receive the 'High' priority category instead of 'Medium'.",
            ExpectedBenefitPt: "Prioriza o escalonamento de threads de jogos que utilizam a API AvSetMmThreadCharacteristics(\"Games\").",
            ExpectedBenefitEn: "Prioritizes thread scheduling for games that invoke the AvSetMmThreadCharacteristics(\"Games\") API.",
            PotentialSideEffectsPt: "Impacto restrito a jogos e motores gráficos que se registram explicitamente no serviço MMCSS.",
            PotentialSideEffectsEn: "Effect is limited to games and engines that explicitly register with the MMCSS service.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKLM).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKLM).",
            DefaultWindowsBehaviorPt: "Padrão de instalação do Windows: 'Medium' (REG_SZ).",
            DefaultWindowsBehaviorEn: "Windows default installation value: 'Medium' (REG_SZ).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            ExpectedBenefitPt: "Pode reduzir atividade de disco e memória em segundo plano logo após a inicialização em cenários específicos.",
            ExpectedBenefitEn: "Can reduce background disk and memory activity right after boot in specific scenarios.",
            PotentialSideEffectsPt: "Desativa a pré-busca e parte do gerenciamento de compressão de memória do SysMain, o que pode aumentar o tempo de abertura de aplicativos cotidianos.",
            PotentialSideEffectsEn: "Disables SysMain prefetching and parts of memory compression management, which may slightly increase launch times for everyday apps.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Exige reinicialização para alterar o estado efetivo do serviço).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Requires reboot to change the effective service state).",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows: 2 (Inicialização Automática).",
            DefaultWindowsBehaviorEn: "Official Windows default: 2 (Automatic startup).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            ExpectedBenefitPt: "Elimina um processo de serviço em segundo plano em máquinas dedicadas exclusivamente a jogos sem impressora.",
            ExpectedBenefitEn: "Eliminates one background service process on dedicated gaming machines without a printer.",
            PotentialSideEffectsPt: "ATENÇÃO: Impressoras físicas, impressoras de rede e 'Microsoft Print to PDF' deixarão de funcionar até que o serviço seja iniciado manualmente ou revertido para 2.",
            PotentialSideEffectsEn: "WARNING: Physical printers, network printers, and 'Microsoft Print to PDF' will stop working until the service is started manually or reverted to 2.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Não recomendado se você utiliza impressoras físicas ou geração de PDF via Spooler.",
            HardwareOrOsDependencyEn: "Not recommended if you use physical printers or Spooler-based PDF generation.",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows: 2 (Automático).",
            DefaultWindowsBehaviorEn: "Official Windows default: 2 (Automatic).",
            RecommendationStatus: TweakRecommendationStatus.NotRecommendedOrDefault,
            IsRecommendedForApply: false
        ),

        ["mem-standby-clean"] = new(
            TweakId: "mem-standby-clean",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            ValueName: "LargeSystemCache",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "0",
            DefaultOriginValue: "1",
            AllowedValues: new[] { "0", "1" },
            Description: "Prioridade de Memória para Aplicativos vs. Cache de Arquivos (LargeSystemCache = 0)",
            DescriptionEn: "Application vs. File Cache Memory Priority (LargeSystemCache = 0)",
            Category: "Memory",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Prioridade de Memória para Aplicativos (LargeSystemCache)",
            FriendlyNameEn: "Application Memory Priority (LargeSystemCache)",
            WhatItDoesPt: "Define se o gerenciador de memória prioriza o working set de programas em execução (0) ou o cache de sistema de arquivos para servidores de rede (1). NOTA: não limpa a lista de memória Standby.",
            WhatItDoesEn: "Specifies whether the memory manager prioritizes the working set of running programs (0) or the file system cache for network servers (1). NOTE: does not purge the Standby memory list.",
            ProblemSolvedPt: "Garante que edições Client mantenham o valor padrão 0 caso algum utilitário de terceiros tenha ativado o modo servidor (1).",
            ProblemSolvedEn: "Ensures Client editions keep the default value 0 if a third-party utility previously enabled server cache mode (1).",
            ExpectedBenefitPt: "Como 0 já é o padrão oficial do Windows 10/11 Client, só produz efeito corretivo se o valor tiver sido alterado para 1 anteriormente.",
            ExpectedBenefitEn: "Since 0 is already the official Windows 10/11 Client default, it only has a corrective effect if the value was previously changed to 1.",
            PotentialSideEffectsPt: "Nenhum efeito negativo em desktops quando definido como 0 (padrão).",
            PotentialSideEffectsEn: "No negative effect on desktops when set to 0 (default).",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Conformidade do padrão Client).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Client default compliance).",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows Client: 0 (Windows Server usa 1 para compartilhamento de arquivos).",
            DefaultWindowsBehaviorEn: "Official Windows Client default: 0 (Windows Server uses 1 for file sharing).",
            RecommendationStatus: TweakRecommendationStatus.NotRecommendedOrDefault,
            IsRecommendedForApply: false
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
            WhatItDoesPt: "Orienta o gerenciador de memória a manter seções pagináveis do executivo do Windows NT e de drivers residentes na memória RAM física.",
            WhatItDoesEn: "Instructs the memory manager to keep pageable sections of the Windows NT executive and drivers resident in physical RAM.",
            ProblemSolvedPt: "Evita que código pouco usado de drivers seja enviado ao arquivo de paginação quando há pressão moderada de memória.",
            ProblemSolvedEn: "Prevents infrequently used driver code from being paged out to disk under moderate memory pressure.",
            ExpectedBenefitPt: "Em sistemas com 16 GB+ de RAM e SSD NVMe, o ganho prático de desempenho é mínimo; útil principalmente para depuração de kernel.",
            ExpectedBenefitEn: "On systems with 16 GB+ RAM and NVMe SSDs, practical performance gain is minimal; primarily intended for kernel debugging.",
            PotentialSideEffectsPt: "Consome mais memória RAM física permanentemente; não recomendado em PCs com 8 GB ou menos de RAM.",
            PotentialSideEffectsEn: "Permanently consumes more physical RAM; not recommended on PCs with 8 GB or less RAM.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Requer reinicialização e pelo menos 16 GB de memória RAM física.",
            HardwareOrOsDependencyEn: "Requires reboot and at least 16 GB of physical RAM.",
            DefaultWindowsBehaviorPt: "Padrão oficial do Windows: 0 (permite paginação normal quando necessário).",
            DefaultWindowsBehaviorEn: "Official Windows default: 0 (allows normal paging when needed).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: false
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
            WhatItDoesPt: "Permite que a GPU gerencie diretamente sua fila de agendamento de pacotes e memória VRAM via processador interno de agendamento.",
            WhatItDoesEn: "Allows the GPU to directly manage its own packet scheduling queue and VRAM via its internal hardware scheduler.",
            ProblemSolvedPt: "Habilita o suporte nativo do sistema operacional para recursos modernos como NVIDIA DLSS 3 Frame Generation e reduz parte do overhead de submissão da CPU.",
            ProblemSolvedEn: "Enables OS support for modern features such as NVIDIA DLSS 3 Frame Generation and reduces part of CPU submission overhead.",
            ExpectedBenefitPt: "Necessário para Frame Generation (DLSS 3) e pode reduzir latência de agendamento de quadros em títulos DX12 compatíveis.",
            ExpectedBenefitEn: "Required for Frame Generation (DLSS 3) and can reduce frame scheduling latency in compatible DX12 titles.",
            PotentialSideEffectsPt: "O resultado depende da GPU, do driver e do jogo. Em alguns jogos ou quando combinado com captura OBS em GPU 99% saturada, pode causar instabilidade se o driver for antigo. Exige reinicialização.",
            PotentialSideEffectsEn: "Results depend on GPU, driver, and game. In some titles or with OBS capture at 99% GPU load, may cause stutter on older drivers. Requires reboot.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Requer GPU compatível (NVIDIA GTX 1000+/RTX, AMD RX 5600+/7000+, Intel Arc) e driver WDDM 2.7+.",
            HardwareOrOsDependencyEn: "Requires supported GPU (NVIDIA GTX 1000+/RTX, AMD RX 5600+/7000+, Intel Arc) and WDDM 2.7+ driver.",
            DefaultWindowsBehaviorPt: "Valor 2 = Ativado (padrão no Windows 11 com GPUs recentes); Valor 1 = Desativado.",
            DefaultWindowsBehaviorEn: "Value 2 = Enabled (default on Windows 11 with modern GPUs); Value 1 = Disabled.",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            ProblemSolvedPt: "Permite ajustar como o Windows trata a transição entre Fullscreen Otimizado (Flip Model) e Fullscreen Exclusivo.",
            ProblemSolvedEn: "Controls how Windows handles the transition between Optimized Fullscreen (Flip Model) and Exclusive Fullscreen.",
            ExpectedBenefitPt: "Útil para padronizar o comportamento de apresentação em jogos DirectX 9/11 sensíveis ao overlay do sistema.",
            ExpectedBenefitEn: "Useful for standardizing presentation behavior in DirectX 9/11 games sensitive to system overlays.",
            PotentialSideEffectsPt: "Jogos DirectX 12 nativos utilizam Flip Model independentemente desta chave; pode alterar a velocidade do Alt+Tab em jogos DX9/DX11 antigos.",
            PotentialSideEffectsEn: "Native DirectX 12 games use Flip Model regardless of this key; may change Alt+Tab speed in older DX9/DX11 games.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKCU).",
            DefaultWindowsBehaviorPt: "Padrão do Windows: 0 (Otimizações de Tela Inteira padrão habilitadas).",
            DefaultWindowsBehaviorEn: "Windows default: 0 (Standard Fullscreen Optimizations enabled).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            WhatItDoesPt: "Define o valor auxiliar GameDVR_FSEBehavior utilizado em conjunto com GameDVR_FSEBehaviorMode no perfil do usuário.",
            WhatItDoesEn: "Sets the auxiliary GameDVR_FSEBehavior value used alongside GameDVR_FSEBehaviorMode in the user profile.",
            ProblemSolvedPt: "Complementa a configuração de preferência de tela inteira para jogos legados DirectX 9/11.",
            ProblemSolvedEn: "Complements the fullscreen preference configuration for legacy DirectX 9/11 games.",
            ExpectedBenefitPt: "Sincroniza o estado do flag secundário do GameConfigStore quando o usuário opta por desativar FSO globalmente.",
            ExpectedBenefitEn: "Synchronizes the secondary GameConfigStore flag when the user opts to override FSO globally.",
            PotentialSideEffectsPt: "Ausente por padrão na maioria das instalações limpas do Windows 10/11 até ser configurado.",
            PotentialSideEffectsEn: "Absent by default on most clean Windows 10/11 installations until explicitly configured.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKCU).",
            DefaultWindowsBehaviorPt: "Por padrão o valor não existe no Registro (ausente = comportamento automático do DWM).",
            DefaultWindowsBehaviorEn: "By default the value is absent in the Registry (absent = automatic DWM behavior).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            ExpectedBenefitPt: "Evita consumo inesperado de upload em segundo plano durante partidas online.",
            ExpectedBenefitEn: "Prevents unexpected background upload usage during online matches.",
            PotentialSideEffectsPt: "Se você tiver vários PCs na mesma rede doméstica, cada um baixará atualizações diretamente da Microsoft em vez de compartilhar via LAN.",
            PotentialSideEffectsEn: "If you have multiple PCs on the same home LAN, each will download updates directly from Microsoft instead of sharing over LAN.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKLM).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKLM).",
            DefaultWindowsBehaviorPt: "Por padrão a chave de política não existe e o Windows usa o modo 1 (HTTP misto com P2P na rede local).",
            DefaultWindowsBehaviorEn: "By default the policy key is absent and Windows uses mode 1 (HTTP blended with local network peering).",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            ProblemSolvedPt: "Reduz o processamento de shaders de desfoque pelo Desktop Window Manager (dwm.exe) em placas de vídeo integradas ou antigas.",
            ProblemSolvedEn: "Reduces blur shader compositing work by the Desktop Window Manager (dwm.exe) on integrated or older GPUs.",
            ExpectedBenefitPt: "Interface com superfícies sólidas e menor uso de composição do DWM em GPUs de entrada.",
            ExpectedBenefitEn: "Solid surface UI with slightly lower DWM compositing overhead on entry-level GPUs.",
            PotentialSideEffectsPt: "Altera a aparência visual do tema escuro/claro do Windows (mesma opção disponível em Configurações > Personalização > Cores).",
            PotentialSideEffectsEn: "Changes the visual look of the Windows dark/light theme (same toggle found in Settings > Personalization > Colors).",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (Efeito imediato no usuário atual HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (Immediate effect on current user HKCU).",
            DefaultWindowsBehaviorPt: "Padrão do Windows: 1 (Efeitos de transparência ativados).",
            DefaultWindowsBehaviorEn: "Windows default: 1 (Transparency effects enabled).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            ProblemSolvedPt: "Evita operações extras de atualização de metadados NTFS durante leituras intensivas de pequenos arquivos.",
            ProblemSolvedEn: "Avoids extra NTFS metadata update operations during intensive small-file reads.",
            ExpectedBenefitPt: "No Windows 10/11 moderno, o sistema já desativa essa atualização por padrão em modo gerenciado pelo sistema (0x80000001 / -2147483647); aplicar 1 fixa a preferência manual.",
            ExpectedBenefitEn: "On modern Windows 10/11, the OS already disables this by default in system-managed mode (0x80000001 / -2147483647); setting 1 locks the user-managed preference.",
            PotentialSideEffectsPt: "Ferramentas de backup ou auditoria que dependam da data de último acesso de leitura não verão essa data atualizada.",
            PotentialSideEffectsEn: "Backup or auditing tools that rely on file last-read timestamps will not see updated access times.",
            RequiresReboot: true,
            HardwareOrOsDependencyPt: "Volumes formatados em NTFS no Windows 10 e 11.",
            HardwareOrOsDependencyEn: "NTFS-formatted volumes on Windows 10 and 11.",
            DefaultWindowsBehaviorPt: "Padrão moderno do Windows: 0x80000001 (-2147483647, System Managed, Last Access desativado).",
            DefaultWindowsBehaviorEn: "Modern Windows default: 0x80000001 (-2147483647, System Managed, Last Access disabled).",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
        ),

        ["net-tcp-autotuning"] = new(
            TweakId: "net-tcp-autotuning",
            Hive: "HKLM",
            SubKeyPath: @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            ValueName: "TcpMaxDataRetransmissions",
            ValueKind: RegistryValueKind.DWord,
            RecommendedValue: "5",
            DefaultOriginValue: "3",
            AllowedValues: new[] { "3", "5", "7" },
            Description: "Limite Padrão RFC de Retransmissões TCP (TcpMaxDataRetransmissions = 5)",
            DescriptionEn: "RFC Standard TCP Data Retransmissions Limit (TcpMaxDataRetransmissions = 5)",
            Category: "Network",
            RiskLevel: "Baixo",
            FriendlyNamePt: "Limite Máximo de Retransmissões de Dados TCP (TcpMaxDataRetransmissions)",
            FriendlyNameEn: "TCP Maximum Data Retransmissions Limit (TcpMaxDataRetransmissions)",
            WhatItDoesPt: "Define quantas vezes a pilha TCP/IP retransmite um segmento de dados não confirmado antes de encerrar a conexão (NOTA: modifica TcpMaxDataRetransmissions, não o TCP Receive Window Auto-Tuning).",
            WhatItDoesEn: "Specifies how many times the TCP/IP stack retransmits an unacknowledged data segment before aborting the connection (NOTE: modifies TcpMaxDataRetransmissions, not TCP Receive Window Auto-Tuning).",
            ProblemSolvedPt: "Restaura o valor padrão seguro da RFC/Microsoft (5) caso outros softwares tenham reduzido para 3 (o que causa quedas prematuras em conexões instáveis).",
            ProblemSolvedEn: "Restores the safe RFC/Microsoft default (5) if third-party tools previously lowered it to 3 (which causes premature drops on unstable links).",
            ExpectedBenefitPt: "Como 5 já é o comportamento padrão do Windows quando o valor não está configurado, este item serve para verificação de conformidade e restauração.",
            ExpectedBenefitEn: "Because 5 is already Windows' default behavior when the value is absent, this item serves for compliance verification and restoration.",
            PotentialSideEffectsPt: "Nenhum quando mantido em 5 (padrão oficial da Microsoft).",
            PotentialSideEffectsEn: "None when kept at 5 (Microsoft official default).",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Pilha TCP/IPv4 e TCP/IPv6 do Windows 10 e 11.",
            HardwareOrOsDependencyEn: "Windows 10 and 11 TCP/IPv4 and TCP/IPv6 stack.",
            DefaultWindowsBehaviorPt: "Por padrão o valor não existe explicitamente no Registro e a pilha TCP utiliza internamente 5 retransmissões.",
            DefaultWindowsBehaviorEn: "By default the value is absent in the Registry and the TCP stack internally uses 5 retransmissions.",
            RecommendationStatus: TweakRecommendationStatus.NotRecommendedOrDefault,
            IsRecommendedForApply: false
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
            ExpectedBenefitPt: "Melhora a privacidade do usuário (equivalente à opção em Configurações > Privacidade e segurança > Geral).",
            ExpectedBenefitEn: "Improves user privacy (equivalent to the toggle in Settings > Privacy & security > General).",
            PotentialSideEffectsPt: "Nenhum impacto em desempenho ou estabilidade do sistema.",
            PotentialSideEffectsEn: "No impact on system performance or stability.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Windows 10 e Windows 11 (HKCU).",
            HardwareOrOsDependencyEn: "Windows 10 and Windows 11 (HKCU).",
            DefaultWindowsBehaviorPt: "Ativado (1) caso o usuário tenha aceitado durante a configuração inicial do Windows.",
            DefaultWindowsBehaviorEn: "Enabled (1) if accepted during initial Windows setup.",
            RecommendationStatus: TweakRecommendationStatus.Recommended,
            IsRecommendedForApply: true
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
            WhatItDoesPt: "Define StartupDelayInMSec=0 na subchave Serialize do Windows Explorer para iniciar aplicativos da bandeja imediatamente após o carregamento da área de trabalho.",
            WhatItDoesEn: "Sets StartupDelayInMSec=0 under the Windows Explorer Serialize subkey so tray startup apps launch immediately after the desktop loads.",
            ProblemSolvedPt: "Reduz a espera artificial do shell antes de disparar os programas de inicialização do usuário.",
            ProblemSolvedEn: "Reduces artificial shell wait time before launching user startup programs.",
            ExpectedBenefitPt: "Carregamento mais rápido de utilitários de inicialização em computadores equipados com SSD.",
            ExpectedBenefitEn: "Faster loading of startup utilities on SSD-equipped computers.",
            PotentialSideEffectsPt: "Em discos rígidos mecânicos (HDD), iniciar todos os programas ao mesmo tempo pode causar concorrência de leitura no logon.",
            PotentialSideEffectsEn: "On mechanical hard drives (HDD), launching all startup apps simultaneously may cause disk read contention at logon.",
            RequiresReboot: false,
            HardwareOrOsDependencyPt: "Recomendado apenas para sistemas instalados em SSD SATA ou NVMe.",
            HardwareOrOsDependencyEn: "Recommended only for systems installed on SATA or NVMe SSDs.",
            DefaultWindowsBehaviorPt: "Por padrão a subchave Serialize não existe e o Explorer gerencia o atraso internamente.",
            DefaultWindowsBehaviorEn: "By default the Serialize subkey does not exist and Explorer manages startup delay internally.",
            RecommendationStatus: TweakRecommendationStatus.Conditional,
            IsRecommendedForApply: true
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
            var ledgerEntry = RegistryStateLedger.RecordAppliedTweakState(
                def,
                previousSerialized,
                previousKind,
                wasInitiallyAbsent,
                keyCreatedByApp,
                serializedTargetValue
            );

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
            RegistryStateLedger.MarkOperationRestored(entry.OperationId);
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
    /// NEVER reports Active if the RegistryValueKind does not match def.ValueKind, even if the numeric/string representation matches.
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
                DiagnosticMessageEn: $"Identifier '{tweakId}' does not belong to the closed tweak catalog."
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
                DiagnosticMessageEn: "Current platform does not support native Windows Registry inspection."
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
                DiagnosticMessageEn: $"Tweak is incompatible with the current Windows build ({currentBuild})."
            );
        }

        var readResult = activeProbe.ReadValue(def.Hive, def.SubKeyPath, def.ValueName);

        switch (readResult.Outcome)
        {
            case RegistryProbeReadOutcome.AccessDenied:
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
                    DiagnosticMessagePt: readResult.ErrorMessage ?? $"O Windows negou permissão para consultar {def.Hive}\\{def.SubKeyPath}.",
                    DiagnosticMessageEn: readResult.ErrorMessage ?? $"Windows denied permission to read {def.Hive}\\{def.SubKeyPath}."
                );

            case RegistryProbeReadOutcome.IncompatibleEnvironment:
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
                    DiagnosticMessagePt: readResult.ErrorMessage ?? "Ambiente incompatível para consulta do Registro.",
                    DiagnosticMessageEn: readResult.ErrorMessage ?? "Incompatible environment for Registry query."
                );

            case RegistryProbeReadOutcome.ReadError:
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
                    DiagnosticMessagePt: readResult.ErrorMessage ?? "Falha técnica ao ler a chave ou valor no Registro.",
                    DiagnosticMessageEn: readResult.ErrorMessage ?? "Technical failure while reading the Registry key or value."
                );

            case RegistryProbeReadOutcome.KeyNotFound:
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
                    DiagnosticMessagePt: $"Subchave ausente ({def.Hive}\\{def.SubKeyPath}). O Windows aplica o comportamento interno padrão sem chave explícita.",
                    DiagnosticMessageEn: $"Subkey absent ({def.Hive}\\{def.SubKeyPath}). Windows applies its internal default behavior without an explicit key."
                );

            case RegistryProbeReadOutcome.ValueNotFound:
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
                    DiagnosticMessagePt: $"Valor '{def.ValueName}' não configurado na subchave. O Windows utiliza o comportamento padrão interno.",
                    DiagnosticMessageEn: $"Value '{def.ValueName}' is not configured in the subkey. Windows uses its internal default behavior."
                );

            case RegistryProbeReadOutcome.ValueFound:
            default:
                RegistryValueKind actualKind = readResult.ActualValueKind;
                string? serialized = readResult.SerializedValue;

                // CRITICAL RULE (§2.6): Never claim a tweak is Active or Inactive if the RegistryValueKind does not match!
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
                        DiagnosticMessagePt: $"Tipo incorreto no Registro: encontrado '{actualKind}' (valor '{serialized}'), mas o Windows exige '{def.ValueKind}'.",
                        DiagnosticMessageEn: $"Incorrect Registry value type: found '{actualKind}' (value '{serialized}'), but Windows expects '{def.ValueKind}'."
                    );
                }

                if (SerializedValuesEqualForKind(serialized, def.RecommendedValue, def.ValueKind))
                {
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
                        DiagnosticMessageEn: $"Active and verified in the Registry ({def.ValueName} = {serialized}, type {actualKind})."
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
                        DiagnosticMessagePt: $"Inativo: valor atual ({serialized}) corresponde ao estado de referência ({def.DefaultOriginValue}).",
                        DiagnosticMessageEn: $"Inactive: current value ({serialized}) matches the reference state ({def.DefaultOriginValue})."
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
                    DiagnosticMessageEn: $"Custom: found value '{serialized}' ({actualKind}), different from recommended ({def.RecommendedValue}) and reference ({def.DefaultOriginValue})."
                );
        }
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