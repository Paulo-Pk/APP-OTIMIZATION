using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PCControlUltimate.Windows.Services;

public record InstalledDriverEntry(
    string Device,
    string Category,
    string InstalledVersion,
    string ReleaseDate,
    string Vendor,
    string Status,
    string OfficialUrl,
    string VerificationScopeNotice = "Inventário somente-leitura via WMI. A presença do dispositivo não significa que o driver seja o mais recente.",
    string DeviceInstanceId = ""
);

public static class DriverScannerService
{
    public const string ModulePolicyNotice =
        "Inventário somente-leitura dos dispositivos Plug and Play e dos drivers reportados pelo Windows. " +
        "Não baixa nem instala drivers automaticamente e não declara que uma versão é a mais recente.";

    public static string? LastError { get; private set; }
    public static string? LastScanError => LastError;

    public static IReadOnlyList<InstalledDriverEntry> QuerySignedDrivers()
    {
        LastError = null;
        var results = new List<InstalledDriverEntry>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            LastError = "A consulta de dispositivos e drivers requer Windows.";
            return results;
        }

        string? signedQueryError = null;

        // Win32_PnPSignedDriver is the authoritative source for driver package metadata.
        // Some Windows installations expose few/no rows here, so the PnP inventory below
        // is used as a safe read-only fallback instead of displaying an empty table.
        try
        {
            using var signedSearcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass, IsSigned, DeviceID FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"
            );

            var seenSignedDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenSignedNamesWithoutId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject obj in signedSearcher.Get())
            {
                string? deviceId = obj["DeviceID"]?.ToString();
                string deviceName = obj["DeviceName"]?.ToString() ?? "Dispositivo sem nome";
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    if (!seenSignedDeviceIds.Add(deviceId)) continue;
                }
                else if (!seenSignedNamesWithoutId.Add(deviceName))
                {
                    continue;
                }

                results.Add(CreateEntry(
                    deviceName,
                    obj["DeviceClass"]?.ToString() ?? string.Empty,
                    obj["DriverVersion"]?.ToString(),
                    obj["DriverDate"]?.ToString(),
                    obj["Manufacturer"]?.ToString(),
                    obj["IsSigned"] is bool signed ? signed : null,
                    null,
                    deviceId
                ));
            }
        }
        catch (Exception ex)
        {
            signedQueryError = $"Consulta de pacotes assinados indisponível ({ex.GetType().Name}: {ex.Message}).";
        }

        // Merge the Plug and Play inventory even when signed-driver metadata exists.
        // Win32_PnPSignedDriver can omit devices; the PnP list adds those devices while
        // preserving package version/date/signature for entries already found above.
        int pnpOnlyEntriesAdded = 0;
        var seenDeviceIds = new HashSet<string>(
            results.Select(entry => entry.DeviceInstanceId)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var seenDeviceNamesWithoutId = new HashSet<string>(
            results.Where(entry => string.IsNullOrWhiteSpace(entry.DeviceInstanceId))
                .Select(entry => entry.Device),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            using var pnpSearcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, PNPClass, PNPDeviceID, Status, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID IS NOT NULL"
            );

            var seenPnpDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject obj in pnpSearcher.Get())
            {
                string? deviceId = obj["PNPDeviceID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(deviceId) && !seenPnpDeviceIds.Add(deviceId))
                    continue;

                string name = obj["Name"]?.ToString() ?? "Dispositivo sem nome";
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    // Device instance IDs identify hardware instances; equal display names
                    // must not cause distinct adapters or peripherals to disappear.
                    if (!seenDeviceIds.Add(deviceId)) continue;
                }
                else if (!seenDeviceNamesWithoutId.Add(name))
                {
                    continue;
                }

                string deviceClass = obj["PNPClass"]?.ToString() ?? string.Empty;
                string manufacturer = obj["Manufacturer"]?.ToString() ?? "Fabricante não informado";

                bool? isSigned = null;
                int? problemCode = obj["ConfigManagerErrorCode"] == null
                    ? null
                    : Convert.ToInt32(obj["ConfigManagerErrorCode"]);

                string deviceStatus = problemCode switch
                {
                    0 => "Dispositivo funcionando (Windows)",
                    null => obj["Status"]?.ToString() ?? "Estado não informado",
                    _ => $"Código de problema do Windows: {problemCode.Value}"
                };

                results.Add(CreateEntry(
                    name,
                    deviceClass,
                    null,
                    null,
                    manufacturer,
                    isSigned,
                    deviceStatus,
                    deviceId
                ));
                pnpOnlyEntriesAdded++;
            }
        }
        catch (Exception ex)
        {
            LastError = results.Count > 0
                ? $"Inventário complementar Plug and Play indisponível ({ex.GetType().Name}: {ex.Message}); foram mantidos os metadados de drivers assinados já encontrados."
                : $"Não foi possível consultar os dispositivos Plug and Play via WMI ({ex.GetType().Name}: {ex.Message})." +
                  (signedQueryError == null ? string.Empty : $" {signedQueryError}");
            return SortResults(results);
        }

        if (results.Count == 0)
        {
            LastError = "O Windows não retornou dispositivos Plug and Play nesta consulta." +
                        (signedQueryError == null ? string.Empty : $" {signedQueryError}");
            return results;
        }

        if (pnpOnlyEntriesAdded > 0 || signedQueryError != null)
        {
            LastError = $"Inventário combinado: {pnpOnlyEntriesAdded} dispositivo(s) Plug and Play sem metadados completos de pacote; campos não informados permanecem como N/D." +
                        (signedQueryError == null ? string.Empty : $" {signedQueryError}");
        }

        return SortResults(results);
    }

    private static IReadOnlyList<InstalledDriverEntry> SortResults(IEnumerable<InstalledDriverEntry> entries) =>
        entries
            .GroupBy(d => string.IsNullOrWhiteSpace(d.DeviceInstanceId)
                ? $"name:{d.Device}"
                : $"id:{d.DeviceInstanceId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static InstalledDriverEntry CreateEntry(
        string deviceName,
        string deviceClass,
        string? version,
        string? rawDate,
        string? manufacturer,
        bool? isSigned,
        string? deviceStatus,
        string? deviceInstanceId = null)
    {
        string formattedDate = "N/D";
        if (!string.IsNullOrWhiteSpace(rawDate) && rawDate.Length >= 8 &&
            DateTime.TryParseExact(rawDate[..8], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            formattedDate = date.ToString("yyyy-MM-dd");
        }

        string vendor = string.IsNullOrWhiteSpace(manufacturer)
            ? "Fabricante não informado"
            : manufacturer.Trim();

        string signature = isSigned switch
        {
            true => "Assinado digitalmente (Windows)",
            false => "Windows informa que não possui assinatura digital",
            _ => "Assinatura não informada"
        };

        string status = string.IsNullOrWhiteSpace(deviceStatus)
            ? signature
            : $"{deviceStatus} · {signature}";

        return new InstalledDriverEntry(
            Device: deviceName,
            Category: MapCategory(deviceClass, deviceName),
            InstalledVersion: string.IsNullOrWhiteSpace(version) ? "N/D" : version,
            ReleaseDate: formattedDate,
            Vendor: vendor,
            Status: status,
            OfficialUrl: ResolveVendorSupportUrl(deviceName, vendor),
            DeviceInstanceId: deviceInstanceId ?? string.Empty
        );
    }

    private static string MapCategory(string deviceClass, string deviceName)
    {
        string key = deviceClass.ToUpperInvariant();
        string combined = $"{key} {deviceName}".ToUpperInvariant();

        if (combined.Contains("DISPLAY") || combined.Contains("VIDEO") || combined.Contains("RADEON") ||
            combined.Contains("GEFORCE") || combined.Contains("NVIDIA")) return "GPU";
        if (key == "NET" || combined.Contains("NETWORK") || combined.Contains("ETHERNET") ||
            combined.Contains("WIRELESS") || combined.Contains("WI-FI")) return "Rede";
        if (key is "MEDIA" or "AUDIOENDPOINT" || combined.Contains("AUDIO") || combined.Contains("REALTEK")) return "Áudio";
        if (key is "SCSIADAPTER" or "HDC" or "DISKDRIVE" || combined.Contains("NVME") || combined.Contains("STORAGE")) return "Armazenamento";
        if (key == "SYSTEM" || combined.Contains("CHIPSET") || combined.Contains("SMBUS")) return "Chipset";
        if (key is "KEYBOARD" or "MOUSE" or "HIDCLASS" || combined.Contains("KEYBOARD") || combined.Contains("MOUSE")) return "Periféricos";
        if (key == "BLUETOOTH" || combined.Contains("BLUETOOTH")) return "Bluetooth";
        if (key == "MONITOR" || combined.Contains("MONITOR")) return "Monitor";
        if (key == "USB" || combined.Contains("USB")) return "USB";
        if (key == "PROCESSOR" || combined.Contains("PROCESSOR")) return "Processador";
        return string.IsNullOrWhiteSpace(deviceClass) ? "Outros" : deviceClass;
    }

    private static string ResolveVendorSupportUrl(string deviceName, string vendor)
    {
        string combined = $"{deviceName} {vendor}".ToUpperInvariant();
        if (combined.Contains("NVIDIA")) return "https://www.nvidia.com/Download/index.aspx";
        if (combined.Contains("AMD") || combined.Contains("RADEON")) return "https://www.amd.com/en/support";
        if (combined.Contains("INTEL")) return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (combined.Contains("REALTEK")) return "https://www.realtek.com/Download/Index?cate_id=194";
        return "https://support.microsoft.com/windows";
    }
}
