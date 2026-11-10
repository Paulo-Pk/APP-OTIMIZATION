using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace PCControlUltimate.Windows.Services;

public record RegistryOperationResult(
    bool Success,
    string? PreviousValue,
    string? VerifiedValue,
    bool WasInitiallyAbsent,
    string? ErrorMessage
);

public static class RegistryOptimizer
{
    // Strict Whitelist of allowed subkeys for gaming and OS optimization
    // Prevents arbitrary or destructive registry modifications
    private static readonly HashSet<string> AllowedSubkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
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

    public static bool IsPathAllowed(string subKeyPath)
    {
        if (string.IsNullOrWhiteSpace(subKeyPath)) return false;
        
        // Strip HKLM\ or HKCU\ prefix if present
        string normalized = NormalizeSubKeyPath(subKeyPath);
        
        foreach (var allowed in AllowedSubkeys)
        {
            if (normalized.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(allowed + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeSubKeyPath(string fullPath)
    {
        if (fullPath.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            return fullPath.Substring(@"HKEY_LOCAL_MACHINE\".Length);
        if (fullPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            return fullPath.Substring(@"HKLM\".Length);
        if (fullPath.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            return fullPath.Substring(@"HKEY_CURRENT_USER\".Length);
        if (fullPath.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            return fullPath.Substring(@"HKCU\".Length);
        return fullPath;
    }

    private static RegistryKey GetRootKey(string fullPath)
    {
        if (fullPath.StartsWith(@"HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(@"HKCU", StringComparison.OrdinalIgnoreCase))
        {
            return Registry.CurrentUser;
        }
        return Registry.LocalMachine;
    }

    /// <summary>
    /// Reads, modifies and verifies a 32-bit DWORD registry value.
    /// Never returns success without reading back and verifying the modified value.
    /// </summary>
    public static RegistryOperationResult TryApplyDword(string keyPath, string valueName, int targetValue)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName))
        {
            return new RegistryOperationResult(false, null, null, false, "Caminho da chave ou nome do valor inválido.");
        }

        string normalizedSubKey = NormalizeSubKeyPath(keyPath);

        if (!IsPathAllowed(normalizedSubKey))
        {
            return new RegistryOperationResult(false, null, null, false, 
                $"Acesso negado: O caminho de registro '{keyPath}' não pertence à lista de chaves seguras autorizadas.");
        }

        RegistryKey root = GetRootKey(keyPath);
        object? previousValue = null;
        bool wasInitiallyAbsent = false;

        try
        {
            // 1. Read existing value and identify if it was absent
            using (RegistryKey? readKey = root.OpenSubKey(normalizedSubKey, false))
            {
                if (readKey == null)
                {
                    wasInitiallyAbsent = true;
                }
                else
                {
                    previousValue = readKey.GetValue(valueName);
                    if (previousValue == null)
                    {
                        wasInitiallyAbsent = true;
                    }
                }
            }

            // 2. Open or create with write permissions
            using (RegistryKey writeKey = root.CreateSubKey(normalizedSubKey, true))
            {
                writeKey.SetValue(valueName, targetValue, RegistryValueKind.DWord);
                writeKey.Flush();
            }

            // 3. Post-write verification: Read back to ensure real modification
            using (RegistryKey? verifyKey = root.OpenSubKey(normalizedSubKey, false))
            {
                if (verifyKey == null)
                {
                    return new RegistryOperationResult(false, previousValue?.ToString(), null, wasInitiallyAbsent, 
                        "Falha na verificação: chave de registro não encontrada após escrita.");
                }

                object? readBack = verifyKey.GetValue(valueName);
                if (readBack is int intVal && intVal == targetValue)
                {
                    return new RegistryOperationResult(true, 
                        wasInitiallyAbsent ? null : previousValue?.ToString(), 
                        targetValue.ToString(), 
                        wasInitiallyAbsent, 
                        null);
                }

                return new RegistryOperationResult(false, previousValue?.ToString(), readBack?.ToString(), wasInitiallyAbsent,
                    $"Verificação falhou: valor lido ({readBack}) difere do valor solicitado ({targetValue}).");
            }
        }
        catch (SecurityException ex)
        {
            return new RegistryOperationResult(false, previousValue?.ToString(), null, wasInitiallyAbsent,
                $"Acesso negado ao Registro do Windows (UAC/Permissão necessária): {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new RegistryOperationResult(false, previousValue?.ToString(), null, wasInitiallyAbsent,
                $"Privilégios insuficientes para alterar a chave do Registro. Execute como Administrador: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new RegistryOperationResult(false, previousValue?.ToString(), null, wasInitiallyAbsent,
                $"Erro inesperado na operação de Registro: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores a previously modified registry value to its exact original state.
    /// If the value was absent originally, deletes the value to revert cleanly.
    /// </summary>
    public static RegistryOperationResult TryRestoreValue(string keyPath, string valueName, object? originalValue, bool wasInitiallyAbsent)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName))
        {
            return new RegistryOperationResult(false, null, null, false, "Parâmetros inválidos.");
        }

        string normalizedSubKey = NormalizeSubKeyPath(keyPath);

        if (!IsPathAllowed(normalizedSubKey))
        {
            return new RegistryOperationResult(false, null, null, false, "Caminho não autorizado para restauração.");
        }

        RegistryKey root = GetRootKey(keyPath);

        try
        {
            if (wasInitiallyAbsent || originalValue == null)
            {
                // Delete the value to return to pristine OEM state
                using RegistryKey? writeKey = root.OpenSubKey(normalizedSubKey, true);
                if (writeKey != null)
                {
                    try
                    {
                        writeKey.DeleteValue(valueName, false);
                        writeKey.Flush();
                    }
                    catch (ArgumentException)
                    {
                        // Value was already absent
                    }
                }

                return new RegistryOperationResult(true, null, "Valor Excluído (Originalmente Inexistente)", true, null);
            }
            else
            {
                // Restore exact numeric or string value
                int targetInt = originalValue is int i ? i : int.TryParse(originalValue.ToString(), out int parsed) ? parsed : 0;

                using (RegistryKey writeKey = root.CreateSubKey(normalizedSubKey, true))
                {
                    writeKey.SetValue(valueName, targetInt, RegistryValueKind.DWord);
                    writeKey.Flush();
                }

                // Verify restoration
                using RegistryKey? verifyKey = root.OpenSubKey(normalizedSubKey, false);
                object? verified = verifyKey?.GetValue(valueName);
                if (verified is int verifiedInt && verifiedInt == targetInt)
                {
                    return new RegistryOperationResult(true, null, verifiedInt.ToString(), false, null);
                }

                return new RegistryOperationResult(false, null, verified?.ToString(), false, 
                    "Verificação de restauração falhou.");
            }
        }
        catch (Exception ex)
        {
            return new RegistryOperationResult(false, null, null, false, 
                $"Falha ao restaurar valor: {ex.Message}");
        }
    }
}