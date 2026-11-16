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
    string VerificationScopeNotice = "Inventário somente-leitura via WMI. A presença do dispositivo não significa que o driver seja o mais recente."
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

            foreach (ManagementObject obj in signedSearcher.Get())
            {
                results.Add(CreateEntry(
                    obj["DeviceName"]?.ToString() ?? "Dispositivo sem nome",
                    obj["DeviceClass"]?.ToString() ?? string.Empty,
                    obj["DriverVersion"]?.ToString(),
                    obj["DriverDate"]?.ToString(),
                    obj["Manufacturer"]?.ToString(),
                    obj["IsSigned"] is bool signed ? signed : null,
                    null
                ));
            }
        }
        catch (Exception ex)
        {
            signedQueryError = $"Consulta de pacotes assinados indisponível ({ex.GetType().Name}: {ex.Message}).";
        }

        if (results.Count > 0)
        {
            return results
                .GroupBy(d => d.Device, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(d => d.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // Fallback: list actual Plug and Play devices. Driver version/date/signature remain
        // explicitly N/D when Windows does not expose those fields through this class.
        try
        {
            using var pnpSearcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, PNPClass, PNPDeviceID, Status, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID IS NOT NULL"
            );

            foreach (ManagementObject obj in pnpSearcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "Dispositivo sem nome";
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
                    deviceStatus
                ));
            }
        }
        catch (Exception ex)
        {
            LastError = $"Não foi possível consultar os dispositivos Plug and Play via WMI ({ex.GetType().Name}: {ex.Message})." +
                        (signedQueryError == null ? string.Empty : $" {signedQueryError}");
            return results;
        }

        if (results.Count == 0)
        {
            LastError = "O Windows não retornou dispositivos Plug and Play nesta consulta." +
                        (signedQueryError == null ? string.Empty : $" {signedQueryError}");
            return results;
        }

        LastError = "O Windows não forneceu metadados completos dos pacotes de driver; a lista abaixo é o inventário Plug and Play. Versão, data e assinatura aparecem como N/D quando não foram informadas." +
                    (signedQueryError == null ? string.Empty : $" {signedQueryError}");

        return results
            .GroupBy(d => d.Device, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static InstalledDriverEntry CreateEntry(
        string deviceName,
        string deviceClass,
        string? version,
        string? rawDate,
        string? manufacturer,
        bool? isSigned,
        string? deviceStatus)
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
            OfficialUrl: ResolveVendorSupportUrl(deviceName, vendor)
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
