using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace PCControlUltimate.Windows.Services;

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
    string RiskLevel
);

public record RegistryOperationResult(
    bool Success,
    string? PreviousValue,
    string? VerifiedValue,
    string ValueKind,
    bool WasInitiallyAbsent,
    bool KeyCreatedByApp,
    string? ErrorMessage
);

public static class RegistryOptimizer
{
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
        @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings",
        @"SYSTEM\CurrentControlSet\Services\SysMain",
        @"SYSTEM\CurrentControlSet\Services\Spooler",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
    };

    public static IReadOnlyCollection<string> AllowedSubkeys => _allowedSubkeys;

    // Strictly forbidden security/boot paths that must never be touched
    private static readonly string[] ForbiddenPathFragments = new[]
    {
        "Windows Defender",
        "WindowsFirewall",
        "SharedAccess\\Parameters\\FirewallPolicy",
        "CurrentVersion\\Run",
        "CurrentVersion\\RunOnce",
        "WindowsUpdate\\AU",
        "SecurityHealth"
    };

    public static IReadOnlyDictionary<string, TweakDefinition> Catalog { get; } = new Dictionary<string, TweakDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["perf-mmcss"] = new(
            "perf-mmcss", "HKLM",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            "SystemResponsiveness", RegistryValueKind.DWord,
            "10", "20", new[] { "10", "20" },
            "Prioriza threads multimídia e reduz latência de áudio/DPC em jogos.", "Baixo"
        ),
        ["perf-game-dvr"] = new(
            "perf-game-dvr", "HKCU",
            @"System\GameConfigStore",
            "GameDVR_Enabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Desativa gravação contínua em segundo plano do Xbox Game DVR.", "Baixo"
        ),
        ["perf-tcp-nagle"] = new(
            "perf-tcp-nagle", "HKLM",
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
            "TCPNoDelay", RegistryValueKind.DWord,
            "1", "0", new[] { "0", "1" },
            "Desativa agrupamento Nagle para envio imediato de pacotes TCP.", "Baixo"
        ),
        ["start-fast-boot"] = new(
            "start-fast-boot", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
            "HiberbootEnabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Realiza inicialização limpa do kernel a cada boot.", "Baixo"
        ),
        ["start-edge-preload"] = new(
            "start-edge-preload", "HKLM",
            @"SOFTWARE\Policies\Microsoft\Edge",
            "StartupBoostEnabled", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Impede pré-carregamento em segundo plano do navegador no boot.", "Baixo"
        ),
        ["bg-telemetry"] = new(
            "bg-telemetry", "HKLM",
            @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            "AllowTelemetry", RegistryValueKind.DWord,
            "1", "3", new[] { "1", "3" },
            "Reduz coleta diagnóstica para nível básico sem afetar segurança.", "Baixo"
        ),
        ["bg-cortana-news"] = new(
            "bg-cortana-news", "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Feeds",
            "ShellFeedsTaskbarViewMode", RegistryValueKind.DWord,
            "2", "0", new[] { "0", "1", "2" },
            "Desativa carregamento contínuo do feed de notícias na barra de tarefas.", "Baixo"
        ),
        ["power-pcie-link"] = new(
            "power-pcie-link", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings",
            "Attributes", RegistryValueKind.DWord,
            "2", "1", new[] { "1", "2" },
            "Mantém barramento PCIe sem estado de economia profunda em carga.", "Baixo"
        ),
        ["serv-sysmain"] = new(
            "serv-sysmain", "HKLM",
            @"SYSTEM\CurrentControlSet\Services\SysMain",
            "Start", RegistryValueKind.DWord,
            "3", "2", new[] { "2", "3" },
            "Configura serviço SysMain para inicialização manual em SSDs NVMe.", "Baixo"
        ),
        ["serv-spooler"] = new(
            "serv-spooler", "HKLM",
            @"SYSTEM\CurrentControlSet\Services\Spooler",
            "Start", RegistryValueKind.DWord,
            "3", "2", new[] { "2", "3" },
            "Configura Spooler de Impressão para modo sob demanda.", "Baixo"
        ),
        ["mem-lock-executive"] = new(
            "mem-lock-executive", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            "DisablePagingExecutive", RegistryValueKind.DWord,
            "1", "0", new[] { "0", "1" },
            "Mantém executivo do kernel e drivers na memória RAM física.", "Baixo"
        ),
        ["game-hags"] = new(
            "game-hags", "HKLM",
            @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
            "HwSchMode", RegistryValueKind.DWord,
            "2", "1", new[] { "1", "2" },
            "Habilita agendamento de GPU acelerado por hardware (HAGS).", "Baixo"
        ),
        ["game-fso-override"] = new(
            "game-fso-override", "HKCU",
            @"System\GameConfigStore",
            "GameDVR_FSEBehaviorMode", RegistryValueKind.DWord,
            "2", "0", new[] { "0", "2" },
            "Otimiza apresentação Flip Model em tela cheia para DX11/DX12.", "Baixo"
        ),
        ["sys-delivery-p2p"] = new(
            "sys-delivery-p2p", "HKLM",
            @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
            "DODownloadMode", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Restringe otimização de entrega P2P em segundo plano durante jogos.", "Baixo"
        ),
        ["sys-transparency"] = new(
            "sys-transparency", "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "EnableTransparency", RegistryValueKind.DWord,
            "0", "1", new[] { "0", "1" },
            "Desativa efeito acrílico do DWM para liberar ciclos de GPU.", "Baixo"
        )
    };

    public static bool IsPathAllowed(string subKeyPath)
    {
        if (string.IsNullOrWhiteSpace(subKeyPath)) return false;

        string normalized = NormalizeSubKeyPath(subKeyPath);
        if (normalized.Contains("..")) return false;

        foreach (var forbidden in ForbiddenPathFragments)
        {
            if (normalized.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var allowed in _allowedSubkeys)
        {
            if (normalized.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(allowed + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static string NormalizeSubKeyPath(string fullPath)
    {
        string trimmed = fullPath.Trim().TrimStart('\\');
        if (trimmed.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKEY_LOCAL_MACHINE\".Length);
        if (trimmed.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKLM\".Length);
        if (trimmed.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKEY_CURRENT_USER\".Length);
        if (trimmed.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(@"HKCU\".Length);
        return trimmed;
    }

    public static RegistryKey GetRootKey(string fullPathOrHive)
    {
        if (fullPathOrHive.StartsWith(@"HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
            fullPathOrHive.StartsWith(@"HKCU", StringComparison.OrdinalIgnoreCase))
        {
            return Registry.CurrentUser;
        }
        return Registry.LocalMachine;
    }

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
    /// Applies an authorized tweak from the strict internal catalog by TweakId.
    /// Rejects unknown tweak IDs or unauthorized target values.
    /// </summary>
    public static RegistryOperationResult ApplyCatalogTweak(string tweakId, string? requestedValue = null)
    {
        if (string.IsNullOrWhiteSpace(tweakId) || !Catalog.TryGetValue(tweakId, out var def))
        {
            return new RegistryOperationResult(
                false, null, null, "Unknown", false, false,
                $"Ajuste '{tweakId}' não faz parte do catálogo oficial autorizado."
            );
        }

        string targetVal = string.IsNullOrWhiteSpace(requestedValue) ? def.RecommendedValue : requestedValue!;
        if (!def.AllowedValues.Contains(targetVal, StringComparer.OrdinalIgnoreCase))
        {
            return new RegistryOperationResult(
                false, null, null, def.ValueKind.ToString(), false, false,
                $"Valor '{targetVal}' não é autorizado para o ajuste '{tweakId}'."
            );
        }

        string fullKeyPath = $"{def.Hive}\\{def.SubKeyPath}";
        return TryApplyTypedValue(fullKeyPath, def.ValueName, def.ValueKind, targetVal);
    }

    /// <summary>
    /// Reads original value and type, writes typed value to Registry, and verifies by immediate re-read.
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
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                $"Acesso negado: O caminho '{keyPath}' não pertence à whitelist de Registro.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new RegistryOperationResult(false, null, null, targetKind.ToString(), false, false,
                "Operação de Registro requer plataforma Windows nativa.");
        }

        RegistryKey root = GetRootKey(keyPath);
        string? previousSerialized = null;
        RegistryValueKind previousKind = targetKind;
        bool wasInitiallyAbsent = false;
        bool keyCreatedByApp = false;

        try
        {
            // 1. Read original value & type before modification
            using (RegistryKey? readKey = root.OpenSubKey(normalizedSubKey, false))
            {
                if (readKey == null)
                {
                    wasInitiallyAbsent = true;
                    keyCreatedByApp = true;
                }
                else
                {
                    object? rawPrev = readKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (rawPrev == null)
                    {
                        wasInitiallyAbsent = true;
                    }
                    else
                    {
                        previousKind = readKey.GetValueKind(valueName);
                        previousSerialized = SerializeRegistryValue(rawPrev, previousKind);
                    }
                }
            }

            object typedTarget = DeserializeRegistryValue(serializedTargetValue, targetKind);

            // 2. Write value with explicit RegistryValueKind
            using (RegistryKey writeKey = root.CreateSubKey(normalizedSubKey, true))
            {
                writeKey.SetValue(valueName, typedTarget, targetKind);
                writeKey.Flush();
            }

            // 3. Re-read and verify both value and kind
            using (RegistryKey? verifyKey = root.OpenSubKey(normalizedSubKey, false))
            {
                if (verifyKey == null)
                {
                    return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                        wasInitiallyAbsent, keyCreatedByApp, "Falha na verificação: subchave não encontrada após escrita.");
                }

                object? readBack = verifyKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind verifiedKind = verifyKey.GetValueKind(valueName);

                if (verifiedKind == targetKind && ValuesMatch(readBack, typedTarget, targetKind))
                {
                    return new RegistryOperationResult(
                        true,
                        wasInitiallyAbsent ? null : previousSerialized,
                        SerializeRegistryValue(readBack, verifiedKind),
                        previousKind.ToString(),
                        wasInitiallyAbsent,
                        keyCreatedByApp,
                        null
                    );
                }

                return new RegistryOperationResult(false, previousSerialized, SerializeRegistryValue(readBack, verifiedKind),
                    previousKind.ToString(), wasInitiallyAbsent, keyCreatedByApp,
                    "Verificação falhou: valor ou tipo lido após gravação difere do esperado.");
            }
        }
        catch (SecurityException ex)
        {
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, $"Acesso de segurança negado (UAC necessário): {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, $"Privilégios insuficientes. Execute como Administrador: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new RegistryOperationResult(false, previousSerialized, null, previousKind.ToString(),
                wasInitiallyAbsent, keyCreatedByApp, $"Erro ao gravar no Registro: {ex.Message}");
        }
    }

    public static RegistryOperationResult TryApplyDword(string keyPath, string valueName, int targetValue)
    {
        return TryApplyTypedValue(keyPath, valueName, RegistryValueKind.DWord, targetValue.ToString());
    }

    public static bool TryApplyDword(string keyPath, string valueName, int targetValue, out object? previousValue)
    {
        var result = TryApplyTypedValue(keyPath, valueName, RegistryValueKind.DWord, targetValue.ToString());
        previousValue = result.PreviousValue;
        return result.Success;
    }

    /// <summary>
    /// Restores a registry value to its exact prior state, preserving its original RegistryValueKind.
    /// If the value was initially absent, deletes the value and verifies its removal.
    /// </summary>
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
            return new RegistryOperationResult(false, null, null, "Unknown", false, false, "Parâmetros de restauração inválidos.");
        }

        string normalizedSubKey = NormalizeSubKeyPath(keyPath);
        if (!IsPathAllowed(normalizedSubKey))
        {
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                "Caminho não autorizado na whitelist para restauração.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new RegistryOperationResult(false, null, null, "Unknown", false, false,
                "Restauração de Registro requer plataforma Windows nativa.");
        }

        RegistryKey root = GetRootKey(keyPath);

        try
        {
            if (wasInitiallyAbsent || originalSerializedValue == null)
            {
                using (RegistryKey? writeKey = root.OpenSubKey(normalizedSubKey, true))
                {
                    if (writeKey != null)
                    {
                        if (writeKey.GetValue(valueName) != null)
                        {
                            writeKey.DeleteValue(valueName, false);
                            writeKey.Flush();
                        }
                    }
                }

                // Verify that the value no longer exists
                using (RegistryKey? verifyKey = root.OpenSubKey(normalizedSubKey, false))
                {
                    if (verifyKey != null && verifyKey.GetValue(valueName) != null)
                    {
                        return new RegistryOperationResult(false, originalSerializedValue, null, "Deleted",
                            true, keyCreatedByApp, "Falha na verificação: o valor inexistente originalmente permaneceu no Registro.");
                    }

                    // Clean up subkey if created by app and now completely empty
                    if (keyCreatedByApp && verifyKey != null && verifyKey.ValueCount == 0 && verifyKey.SubKeyCount == 0)
                    {
                        verifyKey.Close();
                        root.DeleteSubKey(normalizedSubKey, false);
                    }
                }

                return new RegistryOperationResult(true, null, "REMOVED_ABSENT_VALUE", "Deleted", true, keyCreatedByApp, null);
            }
            else
            {
                if (!Enum.TryParse<RegistryValueKind>(originalValueKindStr, true, out var originalKind))
                {
                    originalKind = RegistryValueKind.DWord;
                }

                object typedOriginal = DeserializeRegistryValue(originalSerializedValue, originalKind);

                using (RegistryKey writeKey = root.CreateSubKey(normalizedSubKey, true))
                {
                    writeKey.SetValue(valueName, typedOriginal, originalKind);
                    writeKey.Flush();
                }

                using (RegistryKey? verifyKey = root.OpenSubKey(normalizedSubKey, false))
                {
                    object? verified = verifyKey?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    RegistryValueKind verifiedKind = verifyKey?.GetValueKind(valueName) ?? RegistryValueKind.Unknown;

                    if (verifiedKind == originalKind && ValuesMatch(verified, typedOriginal, originalKind))
                    {
                        return new RegistryOperationResult(
                            true,
                            originalSerializedValue,
                            SerializeRegistryValue(verified, verifiedKind),
                            originalKind.ToString(),
                            false,
                            false,
                            null
                        );
                    }

                    return new RegistryOperationResult(false, originalSerializedValue,
                        verified?.ToString(), originalKind.ToString(), false, false,
                        "Verificação de restauração falhou: valor ou tipo não corresponde ao original.");
                }
            }
        }
        catch (Exception ex)
        {
            return new RegistryOperationResult(false, originalSerializedValue, null,
                originalValueKindStr ?? "Unknown", wasInitiallyAbsent, keyCreatedByApp,
                $"Falha ao restaurar valor no Registro: {ex.Message}");
        }
    }
}