using System;
using System.IO;
using Microsoft.Win32;

namespace PCControlUltimate.Windows.Services;

internal sealed record RegistryValueSnapshot(
    bool KeyExists,
    bool ValueExists,
    object? Value,
    RegistryValueKind Kind,
    int ValueCount,
    int SubKeyCount);

internal interface IRegistryBackend
{
    RegistryValueSnapshot Read(string hive, string subKeyPath, string valueName);
    void Write(string hive, string subKeyPath, string valueName, object value, RegistryValueKind kind);
    void DeleteValue(string hive, string subKeyPath, string valueName);
    void DeleteKeyIfEmpty(string hive, string subKeyPath);
}

internal sealed class WindowsRegistryBackend : IRegistryBackend
{
    public RegistryValueSnapshot Read(string hive, string subKeyPath, string valueName)
    {
        RegistryKey root = RegistryOptimizer.GetRootKey(hive);
        using RegistryKey? key = root.OpenSubKey(subKeyPath, writable: false);
        if (key == null)
            return new RegistryValueSnapshot(false, false, null, RegistryValueKind.Unknown, 0, 0);

        object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null)
            return new RegistryValueSnapshot(true, false, null, RegistryValueKind.Unknown, key.ValueCount, key.SubKeyCount);

        return new RegistryValueSnapshot(true, true, value, key.GetValueKind(valueName), key.ValueCount, key.SubKeyCount);
    }

    public void Write(string hive, string subKeyPath, string valueName, object value, RegistryValueKind kind)
    {
        RegistryKey root = RegistryOptimizer.GetRootKey(hive);
        using RegistryKey? key = root.CreateSubKey(subKeyPath, writable: true);
        if (key == null)
            throw new IOException("O Windows não conseguiu criar ou abrir a chave do Registro para escrita.");

        key.SetValue(valueName, value, kind);
        key.Flush();
    }

    public void DeleteValue(string hive, string subKeyPath, string valueName)
    {
        RegistryKey root = RegistryOptimizer.GetRootKey(hive);
        using RegistryKey? key = root.OpenSubKey(subKeyPath, writable: true);
        if (key == null)
            return;

        if (key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            key.Flush();
        }
    }

    public void DeleteKeyIfEmpty(string hive, string subKeyPath)
    {
        RegistryKey root = RegistryOptimizer.GetRootKey(hive);
        using (RegistryKey? key = root.OpenSubKey(subKeyPath, writable: false))
        {
            if (key == null || key.ValueCount != 0 || key.SubKeyCount != 0)
                return;
        }

        root.DeleteSubKey(subKeyPath, throwOnMissingSubKey: false);
    }
}
