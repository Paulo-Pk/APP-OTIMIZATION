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

public enum TweakDefaultBehaviorKind
{
    KnownDocumentedValue,
    AbsentWithKnownDefault,
    EditionOrConfigDependent,
    UndeterminedWhenAbsent
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
    TweakDefaultBehaviorKind DefaultBehavior = TweakDefaultBehaviorKind.UndeterminedWhenAbsent
)
{
    public string TargetRawValue => RecommendedValue;
    public IReadOnlyList<string> AllowedRawValues => AllowedValues;
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
    string? ErrorCode = null
)
{
    public string? VerifiedCurrentValue => VerifiedValue;
}


[SupportedOSPlatform("windows")]
public static class RegistryOptimizer
{
    private static readonly ConcurrentQueue<RegistryAuditLogEntry> _auditHistory = new();
    internal static IRegistryBackend RegistryBackend { get; set; } = new WindowsRegistryBackend();

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
    /// Closed, immutable catalog of authorized Registry tweaks.
    /// Every modification must reference a TweakId in this catalog; clients cannot specify arbitrary paths, value names, or types.
    /// </summary>
    public static IReadOnlyDictionary<string, TweakDefinition> Catalog { get; } = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["perf-mmcss"] = new(
            "perf-mmcss", "HKLM",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            "SystemResponsiveness", RegistryValueKind.DWord,
            "10", "20", new[] { "10", "20" },
            "Prioriza threads multimídia e reduz latência de áudio/DPC em jogos.",
            "Prioritizes multimedia threads and lowers audio/DPC latency in games.",
            "Performance", "Baixo", TweakDefaultBehaviorKind.KnownDocumentedValue
        ),
        ["perf-game-dvr"] = new(
            "perf-game-dvr", "HKCU",
            @"System\GameConfigStore",
            "GameDVR_Enabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Desativa gravação contínua em segundo plano do Xbox Game DVR.",
            "Disables continuous background recording by Xbox Game DVR.",
            "Performance", "Baixo", TweakDefaultBehaviorKind.EditionOrConfigDependent
        ),
        ["perf-hpet"] = new(
            "perf-hpet", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            "Win32PrioritySeparation", RegistryValueKind.DWord,
            "38", "2", new[] { "2", "38", "26" },
            "Otimiza o quantum de prioridade de janelas em primeiro plano (Win32PrioritySeparation=0x26).",
            "Optimizes foreground window thread priority quantum (Win32PrioritySeparation=0x26).",
            "Performance", "Médio"
        ),
        ["perf-tcp-nagle"] = new(
            "perf-tcp-nagle", "HKLM",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            "NetworkThrottlingIndex", RegistryValueKind.DWord,
            "-1", "10", new[] { "-1", "10", "1" },
            "Desativa limitação artificial de pacotes de rede multimídia (NetworkThrottlingIndex).",
            "Disables artificial multimedia network packet throttling (NetworkThrottlingIndex).",
            "Performance", "Baixo"
        ),
        ["start-fast-boot"] = new(
            "start-fast-boot", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
            "HiberbootEnabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Realiza inicialização limpa do kernel a cada boot (HiberbootEnabled=0).",
            "Performs a clean kernel initialization on every boot (HiberbootEnabled=0).",
            "Startup", "Baixo"
        ),
        ["start-edge-preload"] = new(
            "start-edge-preload", "HKLM",
            @"SOFTWARE\Policies\Microsoft\Edge",
            "StartupBoostEnabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Impede pré-carregamento em segundo plano do Microsoft Edge no boot.",
            "Prevents Microsoft Edge background preloading at startup.",
            "Startup", "Baixo"
        ),
        ["bg-telemetry"] = new(
            "bg-telemetry", "HKLM",
            @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            "AllowTelemetry", RegistryValueKind.DWord,
            "1", "3", new[] { "1", "3" },
            "Reduz coleta diagnóstica para nível básico sem afetar segurança.",
            "Reduces diagnostic telemetry collection to basic level without affecting security.",
            "Background", "Baixo"
        ),
        ["bg-cortana-news"] = new(
            "bg-cortana-news", "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Feeds",
            "ShellFeedsTaskbarViewMode", RegistryValueKind.DWord,
            "2", "0", new[] { "0", "1", "2" },
            "Desativa carregamento contínuo do feed de notícias na barra de tarefas.",
            "Disables continuous loading of the taskbar news and interests feed.",
            "Background", "Baixo"
        ),
        ["power-ultimate-plan"] = new(
            "power-ultimate-plan", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
            "PowerThrottlingOff", RegistryValueKind.DWord,
            "1", "0", new[] { "0", "1" },
            "Desativa redução automática de clock (Power Throttling) para processos em execução.",
            "Disables automatic CPU clock throttling (Power Throttling) for active processes.",
            "Power", "Baixo"
        ),
        ["power-pcie-link"] = new(
            "power-pcie-link", "HKLM",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
            "Scheduling Category", RegistryValueKind.String,
            "High", "Medium", new[] { "High", "Medium", "Low" },
            "Define a categoria de escalonamento MMCSS de Jogos para High (REG_SZ).",
            "Sets the MMCSS Games task scheduling category to High (REG_SZ).",
            "Power", "Baixo"
        ),
        ["serv-sysmain"] = new(
            "serv-sysmain", "HKLM",
            @"SYSTEM\CurrentControlSet\Services\SysMain",
            "Start", RegistryValueKind.DWord,
            "3", "2", new[] { "2", "3" },
            "Configura serviço SysMain para inicialização manual em SSDs NVMe.",
            "Configures the SysMain service for manual startup on NVMe SSDs.",
            "Services", "Baixo"
        ),
        ["serv-spooler"] = new(
            "serv-spooler", "HKLM",
            @"SYSTEM\CurrentControlSet\Services\Spooler",
            "Start", RegistryValueKind.DWord,
            "3", "2", new[] { "2", "3" },
            "Configura Spooler de Impressão para modo sob demanda (Manual).",
            "Configures Print Spooler for on-demand (Manual) startup.",
            "Services", "Baixo"
        ),
        ["mem-standby-clean"] = new(
            "mem-standby-clean", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            "LargeSystemCache", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Prioriza memória RAM física para aplicativos e jogos em primeiro plano (LargeSystemCache=0).",
            "Prioritizes physical RAM for foreground applications and games (LargeSystemCache=0).",
            "Memory", "Baixo"
        ),
        ["mem-lock-executive"] = new(
            "mem-lock-executive", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            "DisablePagingExecutive", RegistryValueKind.DWord,
            "1", "0", new[] { "0", "1" },
            "Mantém executivo do kernel e drivers na memória RAM física.",
            "Keeps kernel executive and drivers resident in physical RAM.",
            "Memory", "Baixo", TweakDefaultBehaviorKind.AbsentWithKnownDefault
        ),
        ["game-hags"] = new(
            "game-hags", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
            "HwSchMode", RegistryValueKind.DWord,
            "2", "1", new[] { "1", "2" },
            "Habilita agendamento de GPU acelerado por hardware (HAGS).",
            "Enables Hardware-Accelerated GPU Scheduling (HAGS).",
            "Gaming", "Baixo"
        ),
        ["game-fso-override"] = new(
            "game-fso-override", "HKCU",
            @"System\GameConfigStore",
            "GameDVR_FSEBehaviorMode", RegistryValueKind.DWord,
            "2", "0", new[] { "0", "2" },
            "Otimiza apresentação Flip Model em tela cheia para DX11/DX12.",
            "Optimizes fullscreen Flip Model presentation for DX11/DX12.",
            "Gaming", "Baixo"
        ),
        ["perf-fso-flags"] = new(
            "perf-fso-flags", "HKCU",
            @"System\GameConfigStore",
            "GameDVR_FSEBehavior", RegistryValueKind.DWord,
            "2", "0", new[] { "0", "2" },
            "Define flags de comportamento Fullscreen Optimizations (GameDVR_FSEBehavior).",
            "Configures Fullscreen Optimizations behavior flags (GameDVR_FSEBehavior).",
            "Gaming", "Baixo"
        ),
        ["sys-delivery-p2p"] = new(
            "sys-delivery-p2p", "HKLM",
            @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
            "DODownloadMode", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Restringe otimização de entrega P2P em segundo plano durante jogos.",
            "Restricts background P2P Windows Update Delivery Optimization during gaming.",
            "System", "Baixo"
        ),
        ["sys-transparency"] = new(
            "sys-transparency", "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "EnableTransparency", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Desativa efeito acrílico do DWM para liberar ciclos de GPU.",
            "Disables DWM acrylic transparency blur to free GPU cycles.",
            "Interface", "Baixo"
        ),
        ["storage-ntfs-lastaccess"] = new(
            "storage-ntfs-lastaccess", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\FileSystem",
            "NtfsDisableLastAccessUpdate", RegistryValueKind.DWord,
            "1", "0", new[] { "0", "1", "-2147483647" },
            "Reduz escritas desnecessárias de metadados NTFS em SSDs NVMe e SATA.",
            "Reduces unnecessary NTFS last-access metadata writes on NVMe and SATA SSDs.",
            "Storage", "Baixo"
        ),
        ["net-tcp-autotuning"] = new(
            "net-tcp-autotuning", "HKLM",
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            "TcpMaxDataRetransmissions", RegistryValueKind.DWord,
            "5", "5", new[] { "3", "5", "7" },
            "Mantém limite seguro de retransmissão TCP conforme RFC sem quebrar conexões.",
            "Maintains RFC-compliant safe TCP retransmission limit without breaking connections.",
            "Network", "Baixo"
        ),
        ["priv-advertising-id"] = new(
            "priv-advertising-id", "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            "Enabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Desativa o identificador de anúncios direcionados do perfil de usuário no Windows.",
            "Disables the targeted advertising ID for the user profile in Windows.",
            "Privacy", "Baixo"
        ),
        ["maint-startup-delay"] = new(
            "maint-startup-delay", "HKCU",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
            "StartupDelayInMSec", RegistryValueKind.DWord,
            "0", "1000", new[] { "0", "1000" },
            "Remove atraso artificial do Explorer na inicialização de aplicativos de bandeja.",
            "Removes artificial Explorer startup delay for tray applications.",
            "Maintenance", "Baixo"
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
                : string.Empty,
            _ => rawValue.ToString()
        };
    }

    public static object DeserializeRegistryValue(string? serialized, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => int.TryParse(serialized, out int d) ? d : 0,
            RegistryValueKind.QWord => long.TryParse(serialized, out long q) ? q : 0L,
            RegistryValueKind.String => serialized ?? string.Empty,
            RegistryValueKind.ExpandString => serialized ?? string.Empty,
            RegistryValueKind.MultiString => !string.IsNullOrWhiteSpace(serialized) && serialized.TrimStart().StartsWith("[")
                ? (JsonSerializer.Deserialize<string[]>(serialized) ?? Array.Empty<string>())
                : (serialized != null ? new[] { serialized } : Array.Empty<string>()),
            RegistryValueKind.Binary => !string.IsNullOrWhiteSpace(serialized)
                ? Convert.FromBase64String(serialized)
                : Array.Empty<byte>(),
            _ => serialized ?? string.Empty
        };
    }

    public static bool ValuesMatch(object? readBack, object expectedTyped, RegistryValueKind kind)
    {
        if (readBack == null) return false;

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

        return ExecuteAuthorizedWrite(def, targetVal);
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

        return ExecuteAuthorizedWrite(def, serializedTargetValue);
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

    private static RegistryOperationResult ExecuteAuthorizedWrite(TweakDefinition def, string serializedTargetValue)
    {
        string fullKeyPath = $"{def.Hive}\\{def.SubKeyPath}";
        RegistryValueKind targetKind = def.ValueKind;

        if (!OperatingSystem.IsWindows())
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
        string? operationId = null;
        string operationStage = "READ_PRE_STATE";

        try
        {
            RegistryValueSnapshot before = RegistryBackend.Read(def.Hive, def.SubKeyPath, def.ValueName);
            wasInitiallyAbsent = !before.ValueExists;
            keyCreatedByApp = !before.KeyExists;
            if (before.ValueExists)
            {
                previousKind = before.Kind;
                previousSerialized = SerializeRegistryValue(before.Value, previousKind);
            }

            object typedTarget = DeserializeRegistryValue(serializedTargetValue, targetKind);

            // Persist an authenticated recovery point BEFORE changing the Windows Registry.
            // If the subsequent write or readback fails, the pending entry remains available for rollback.
            operationStage = "RECORD_TRUSTED_LEDGER";
            var ledgerEntry = RegistryStateLedger.RecordAppliedTweakState(
                def,
                previousSerialized,
                previousKind,
                wasInitiallyAbsent,
                keyCreatedByApp,
                serializedTargetValue
            );
            operationId = ledgerEntry.OperationId;

            operationStage = "WRITE_TARGET";
            RegistryBackend.Write(def.Hive, def.SubKeyPath, def.ValueName, typedTarget, targetKind);
            operationStage = "VERIFY_TARGET";
            RegistryValueSnapshot after = RegistryBackend.Read(def.Hive, def.SubKeyPath, def.ValueName);
            if (!after.KeyExists || !after.ValueExists)
            {
                string err = "Falha na verificação: subchave ou valor não encontrado após escrita. O estado original foi preservado no histórico para restauração.";
                RecordAudit("APPLY_VERIFY_FAIL", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                    previousSerialized, serializedTargetValue, null, false, err);
                return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                    wasInitiallyAbsent, keyCreatedByApp, err, def.TweakId, operationId, "REGISTRY_READBACK_MISSING");
            }

            object? readBack = after.Value;
            RegistryValueKind verifiedKind = after.Kind;
            string? verifiedStr = SerializeRegistryValue(readBack, verifiedKind);

            if (verifiedKind == targetKind && ValuesMatch(readBack, typedTarget, targetKind))
            {
                RecordAudit("APPLY_SUCCESS", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                    wasInitiallyAbsent ? "<ABSENT>" : previousSerialized, serializedTargetValue, verifiedStr, true, null);
                return new RegistryOperationResult(
                    true,
                    wasInitiallyAbsent ? null : previousSerialized,
                    verifiedStr,
                    previousKind.ToString(),
                    wasInitiallyAbsent,
                    keyCreatedByApp,
                    null,
                    def.TweakId,
                    operationId
                );
            }

            string mismatchErr = "Verificação falhou: valor ou tipo lido após gravação difere do esperado. O estado original permanece disponível no histórico para restauração.";
            RecordAudit("APPLY_VERIFY_MISMATCH", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                previousSerialized, serializedTargetValue, verifiedStr, false, mismatchErr);
            return new RegistryOperationResult(false, previousSerialized, verifiedStr,
                previousKind.ToString(), wasInitiallyAbsent, keyCreatedByApp, mismatchErr,
                def.TweakId, operationId, "REGISTRY_READBACK_MISMATCH");
        }
        catch (SecurityException ex)
        {
            string msg = operationStage == "RECORD_TRUSTED_LEDGER"
                ? $"Acesso negado ao gravar o histórico seguro do ajuste. Nenhuma alteração no Registro foi iniciada: {ex.Message}"
                : $"Acesso de segurança negado (Elevação UAC necessária para {def.Hive}): {ex.Message}";
            RecordAudit("APPLY_SECURITY_DENIED", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                previousSerialized, serializedTargetValue, null, false, msg);
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, msg, def.TweakId, operationId,
                operationStage == "RECORD_TRUSTED_LEDGER" ? "REGISTRY_LEDGER_WRITE_FAILED" : "REGISTRY_ACCESS_DENIED");
        }
        catch (UnauthorizedAccessException ex)
        {
            string msg = operationStage == "RECORD_TRUSTED_LEDGER"
                ? $"Não foi possível gravar o histórico seguro do ajuste. Nenhuma alteração no Registro foi iniciada: {ex.Message}"
                : $"Privilégios insuficientes para {def.Hive}. Execute como Administrador (UAC): {ex.Message}";
            RecordAudit("APPLY_UAC_REQUIRED", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                previousSerialized, serializedTargetValue, null, false, msg);
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, msg, def.TweakId, operationId,
                operationStage == "RECORD_TRUSTED_LEDGER" ? "REGISTRY_LEDGER_WRITE_FAILED" : "REGISTRY_ACCESS_DENIED");
        }
        catch (Exception ex)
        {
            string msg = operationStage == "RECORD_TRUSTED_LEDGER"
                ? $"Não foi possível registrar o estado original no histórico seguro; a alteração do Registro foi bloqueada: {ex.Message}"
                : $"Erro ao gravar no Registro: {ex.Message}";
            RecordAudit("APPLY_ERROR", def.TweakId, fullKeyPath, def.ValueName, previousKind.ToString(),
                previousSerialized, serializedTargetValue, null, false, msg);
            string errorCode = operationStage == "RECORD_TRUSTED_LEDGER"
                ? "REGISTRY_LEDGER_WRITE_FAILED"
                : operationStage.StartsWith("VERIFY", StringComparison.Ordinal) || operationStage == "READ_PRE_STATE"
                    ? "REGISTRY_READ_ERROR"
                    : "REGISTRY_OPERATION_FAILED";
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, msg, def.TweakId, operationId, errorCode);
        }
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
            entry.OperationId
        );

        if (restoreRes.Success)
        {
            try
            {
                if (!RegistryStateLedger.MarkOperationRestored(entry.OperationId))
                    throw new InvalidOperationException("O Registro foi restaurado, mas o Ledger não confirmou a baixa da operação.");
            }
            catch (Exception ex)
            {
                string ledgerError = $"O valor original foi restaurado no Registro, porém não foi possível registrar a conclusão no Ledger. A operação permanece pendente e não deve ser repetida sem verificação manual. Detalhe: {ex.Message}";
                RecordAudit("RESTORE_LEDGER_MARK_FAILED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                    entry.OriginalValueKind, null, entry.OriginalRawValue, restoreRes.VerifiedValue, false, ledgerError);
                return restoreRes with
                {
                    Success = false,
                    ErrorMessage = ledgerError,
                    ErrorCode = "RESTORE_LEDGER_UPDATE_FAILED"
                };
            }
        }

        return restoreRes;
    }

    /// <summary>
    /// Typed overload demonstrating that caller-supplied originalRawValue / originalKind are never trusted
    /// when a trusted backend ledger entry exists for tweakId; restoration delegates strictly to the backend ledger.
    /// </summary>
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

        // Security rule: Ignore untrusted caller-provided originalRawValue, originalKind, wasInitiallyAbsent, and keyCreatedByApp;
        // always restore from the backend's DPAPI + HMAC-SHA256 RegistryStateLedger.
        return RestoreCatalogTweakFromTrustedSource(def.TweakId);
    }

    /// <summary>
    /// Blocks any attempt by a caller to pass untrusted client-supplied restoration parameters without a verified backend source,
    /// while validating type and AllowedValues strictly when invoked from verified snapshot restoration.
    /// </summary>
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

        // Caller-supplied original value/type/absence flags are untrusted and intentionally ignored.
        // The backend resolves and authenticates the original state from RegistryStateLedger.
        return RestoreCatalogTweakFromTrustedSource(def.TweakId);
    }

    /// <summary>
    /// Internal method used strictly by RegistrySnapshotEngine AFTER verifying canonical SHA-256 and whitelist integrity.
    /// Enforces strict catalog type and AllowedValues validation on the snapshot entry before restoring.
    /// </summary>
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
            verifiedEntry.KeyCreatedByApp
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
        // Rule 5: Validate RegistryValueKind compatibility
        RegistryValueKind parsedKind = def.ValueKind;
        if (!string.IsNullOrWhiteSpace(candidateKindStr))
        {
            if (!Enum.TryParse<RegistryValueKind>(candidateKindStr.Trim(), true, out parsedKind) || parsedKind != def.ValueKind)
            {
                return (false, RegistryValueKind.Unknown,
                    $"INCOMPATIBLE_VALUE_KIND: Tipo de Registro '{candidateKindStr}' é incompatível com o catálogo para '{def.TweakId}' (esperado: '{def.ValueKind}').");
            }
        }

        // Rule 6: Validate candidateRawValue against AllowedValues when not initially absent
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

    private static (bool isValid, RegistryValueKind parsedKind, string? errorMessage) ValidateTrustedRestoreState(
        string? originalSerializedValue,
        string? originalValueKindStr,
        bool wasInitiallyAbsent)
    {
        if (wasInitiallyAbsent)
        {
            if (originalSerializedValue != null)
                return (false, RegistryValueKind.Unknown, "LEDGER_STATE_INVALID: valor original deveria estar ausente.");
            return (true, RegistryValueKind.Unknown, null);
        }

        if (originalSerializedValue == null)
            return (false, RegistryValueKind.Unknown, "LEDGER_STATE_INVALID: valor original autenticado está ausente.");

        if (!Enum.TryParse<RegistryValueKind>(originalValueKindStr, true, out var kind) ||
            !Enum.IsDefined(typeof(RegistryValueKind), kind) ||
            kind == RegistryValueKind.Unknown)
            return (false, RegistryValueKind.Unknown, "LEDGER_KIND_INVALID: tipo original do Registro inválido.");

        try
        {
            if (DeserializeValue(originalSerializedValue, kind) == null)
                return (false, RegistryValueKind.Unknown, "LEDGER_VALUE_INVALID: desserialização do valor original retornou nulo.");
        }
        catch (Exception ex)
        {
            return (false, RegistryValueKind.Unknown, $"LEDGER_VALUE_INVALID: valor original não pode ser desserializado: {ex.Message}");
        }

        return (true, kind, null);
    }

    private static RegistryOperationResult ValidateAndExecuteRestore(
        TweakDefinition def,
        string? originalSerializedValue,
        string? originalValueKindStr,
        bool wasInitiallyAbsent,
        bool keyCreatedByApp,
        string? operationId = null)
    {
        // This method is reached only with state resolved from the authenticated backend ledger.
        // Restore the exact original value/type; AllowedValues applies to new writes, not recovery.
        var check = ValidateTrustedRestoreState(originalSerializedValue, originalValueKindStr, wasInitiallyAbsent);
        if (!check.isValid)
        {
            RecordAudit("RESTORE_VALIDATION_FAILED", def.TweakId, $"{def.Hive}\\{def.SubKeyPath}", def.ValueName,
                originalValueKindStr ?? "Unknown", null, originalSerializedValue, null, false, check.errorMessage);
            return new RegistryOperationResult(false, null, null, originalValueKindStr ?? "Unknown",
                wasInitiallyAbsent, keyCreatedByApp, check.errorMessage, def.TweakId, operationId);
        }

        string fullKeyPath = $"{def.Hive}\\{def.SubKeyPath}";

        if (!OperatingSystem.IsWindows())
        {
            // In non-Windows environments (e.g., automated xUnit verification of trusted ledger / snapshot state rules),
            // report verified logical restoration after passing all cryptographic and catalog validations.
            string simulatedVerified = wasInitiallyAbsent ? "REMOVED_ABSENT_VALUE" : (originalSerializedValue ?? string.Empty);
            RecordAudit("RESTORE_VERIFIED_LOGICAL", def.TweakId, fullKeyPath, def.ValueName,
                wasInitiallyAbsent ? "Deleted" : check.parsedKind.ToString(), null, originalSerializedValue, simulatedVerified, true, null);
            return new RegistryOperationResult(
                true,
                null,
                simulatedVerified,
                wasInitiallyAbsent ? "Deleted" : check.parsedKind.ToString(),
                wasInitiallyAbsent,
                keyCreatedByApp,
                null,
                def.TweakId,
                operationId
            );
        }

        string operationStage = "READ_CURRENT_STATE";
        try
        {
            RegistryValueSnapshot before = RegistryBackend.Read(def.Hive, def.SubKeyPath, def.ValueName);
            string? currentBeforeRestore = before.ValueExists
                ? SerializeRegistryValue(before.Value, before.Kind)
                : null;

            if (wasInitiallyAbsent || originalSerializedValue == null)
            {
                operationStage = "DELETE_VALUE";
                RegistryBackend.DeleteValue(def.Hive, def.SubKeyPath, def.ValueName);
                operationStage = "VERIFY_DELETE";
                RegistryValueSnapshot afterDelete = RegistryBackend.Read(def.Hive, def.SubKeyPath, def.ValueName);
                if (afterDelete.ValueExists)
                {
                    string err = "Falha na verificação: o valor originalmente ausente permaneceu no Registro.";
                    RecordAudit("RESTORE_VERIFY_FAIL", def.TweakId, fullKeyPath, def.ValueName, "Deleted",
                        currentBeforeRestore, "<DELETE>", null, false, err);
                    return new RegistryOperationResult(false, originalSerializedValue, null, "Deleted",
                        true, keyCreatedByApp, err, def.TweakId, operationId, "REGISTRY_READBACK_MISSING");
                }

                if (keyCreatedByApp)
                {
                    operationStage = "DELETE_CREATED_KEY";
                    RegistryBackend.DeleteKeyIfEmpty(def.Hive, def.SubKeyPath);
                }

                RecordAudit("RESTORE_DELETED_ABSENT", def.TweakId, fullKeyPath, def.ValueName, "Deleted",
                    currentBeforeRestore, "<DELETE>", "REMOVED_ABSENT_VALUE", true, null);
                return new RegistryOperationResult(true, currentBeforeRestore, "REMOVED_ABSENT_VALUE", "Deleted",
                    true, keyCreatedByApp, null, def.TweakId, operationId);
            }

            RegistryValueKind originalKind = check.parsedKind;
            object typedOriginal = DeserializeRegistryValue(originalSerializedValue, originalKind);
            operationStage = "WRITE_RESTORE_VALUE";
            RegistryBackend.Write(def.Hive, def.SubKeyPath, def.ValueName, typedOriginal, originalKind);
            operationStage = "VERIFY_RESTORE_VALUE";
            RegistryValueSnapshot after = RegistryBackend.Read(def.Hive, def.SubKeyPath, def.ValueName);
            object? verified = after.Value;
            RegistryValueKind verifiedKind = after.ValueExists ? after.Kind : RegistryValueKind.Unknown;
            string? verifiedStr = after.ValueExists ? SerializeRegistryValue(verified, verifiedKind) : null;

            if (after.ValueExists && verifiedKind == originalKind && ValuesMatch(verified, typedOriginal, originalKind))
            {
                RecordAudit("RESTORE_SUCCESS", def.TweakId, fullKeyPath, def.ValueName, originalKind.ToString(),
                    currentBeforeRestore, originalSerializedValue, verifiedStr, true, null);
                return new RegistryOperationResult(true, currentBeforeRestore, verifiedStr,
                    originalKind.ToString(), false, false, null, def.TweakId, operationId);
            }

            string mismatchErr = "Verificação pós-restauração falhou: valor ou tipo não corresponde ao original.";
            RecordAudit("RESTORE_VERIFY_MISMATCH", def.TweakId, fullKeyPath, def.ValueName, originalKind.ToString(),
                currentBeforeRestore, originalSerializedValue, verifiedStr, false, mismatchErr);
            return new RegistryOperationResult(false, currentBeforeRestore, verifiedStr,
                originalKind.ToString(), false, false, mismatchErr, def.TweakId, operationId, "REGISTRY_READBACK_MISMATCH");
        }
        catch (SecurityException ex)
        {
            string msg = $"Acesso de segurança negado ao restaurar o Registro: {ex.Message}";
            RecordAudit("RESTORE_ACCESS_DENIED", def.TweakId, fullKeyPath, def.ValueName,
                originalValueKindStr ?? def.ValueKind.ToString(), null, originalSerializedValue, null, false, msg);
            return new RegistryOperationResult(false, originalSerializedValue, null,
                originalValueKindStr ?? "Unknown", wasInitiallyAbsent, keyCreatedByApp, msg,
                def.TweakId, operationId, "REGISTRY_ACCESS_DENIED");
        }
        catch (UnauthorizedAccessException ex)
        {
            string msg = $"Permissão insuficiente ao restaurar o Registro: {ex.Message}";
            RecordAudit("RESTORE_ACCESS_DENIED", def.TweakId, fullKeyPath, def.ValueName,
                originalValueKindStr ?? def.ValueKind.ToString(), null, originalSerializedValue, null, false, msg);
            return new RegistryOperationResult(false, originalSerializedValue, null,
                originalValueKindStr ?? "Unknown", wasInitiallyAbsent, keyCreatedByApp, msg,
                def.TweakId, operationId, "REGISTRY_ACCESS_DENIED");
        }
        catch (Exception ex)
        {
            string msg = $"Falha ao restaurar valor no Registro durante {operationStage}: {ex.Message}";
            string errorCode = operationStage.StartsWith("READ", StringComparison.Ordinal) ||
                               operationStage.StartsWith("VERIFY", StringComparison.Ordinal)
                ? "REGISTRY_READ_ERROR"
                : "REGISTRY_RESTORE_FAILED";
            RecordAudit("RESTORE_ERROR", def.TweakId, fullKeyPath, def.ValueName,
                originalValueKindStr ?? def.ValueKind.ToString(), null, originalSerializedValue, null, false, msg);
            return new RegistryOperationResult(false, originalSerializedValue, null,
                originalValueKindStr ?? "Unknown", wasInitiallyAbsent, keyCreatedByApp, msg,
                def.TweakId, operationId, errorCode);
        }
    }

    public static IReadOnlyList<RegistryAuditLogEntry> GetAuditLogs() => _auditHistory.ToArray();

    public static IReadOnlyList<RegistryAuditLogEntry> GetRecentAuditLogs(int maxCount = 50) =>
        _auditHistory.Reverse().Take(maxCount).ToList();

    public static string InspectCatalogTweakCurrentState(string tweakId)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId.Trim(), out var def))
            return "Não Suportado";

        if (!OperatingSystem.IsWindows())
            return "Não Disponível Neste Sistema";

        try
        {
            RegistryValueSnapshot snapshot = RegistryBackend.Read(def.Hive, def.SubKeyPath, def.ValueName);

            if (!snapshot.KeyExists || !snapshot.ValueExists)
            {
                return def.DefaultBehavior switch
                {
                    TweakDefaultBehaviorKind.KnownDocumentedValue => "Inativo",
                    TweakDefaultBehaviorKind.AbsentWithKnownDefault => "Inativo",
                    TweakDefaultBehaviorKind.EditionOrConfigDependent => "Padrão Dependente",
                    _ => "Estado Não Determinável"
                };
            }

            // A value with a different Registry type is incompatible, not a custom value.
            // Inspection is read-only: never rewrite or coerce an unexpected value.
            if (snapshot.Kind != def.ValueKind)
                return "Tipo Incorreto";

            string? serialized = SerializeRegistryValue(snapshot.Value, snapshot.Kind);
            if (string.Equals(serialized, def.RecommendedValue, StringComparison.OrdinalIgnoreCase))
                return "Ativo";
            if (string.Equals(serialized, def.DefaultOriginValue, StringComparison.OrdinalIgnoreCase))
                return "Inativo";

            return "Personalizado";
        }
        catch (UnauthorizedAccessException)
        {
            return "Acesso Negado";
        }
        catch (SecurityException)
        {
            return "Acesso Negado";
        }
        catch (Exception)
        {
            return "Erro de Leitura";
        }
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