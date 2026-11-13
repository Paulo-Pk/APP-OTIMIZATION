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
    string VerificationScopeNotice = "Inventário somente-leitura via WMI (Win32_PnPSignedDriver). A assinatura é verificada; atualização automática não é presumida."
);

public static class DriverScannerService
{
    public const string ModulePolicyNotice =
        "Inventário somente-leitura de drivers reportados pelo Windows. O aplicativo não presume que um driver está atualizado apenas por estar assinado e não instala drivers automaticamente.";

    public static string? LastScanError { get; private set; }

    public static IReadOnlyList<InstalledDriverEntry> QuerySignedDrivers()
    {
        var results = new List<InstalledDriverEntry>();
        LastScanError = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            LastScanError = "A consulta de drivers requer Windows.";
            return results;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"
            );

            foreach (ManagementObject obj in searcher.Get())
            {
                string deviceName = obj["DeviceName"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(deviceName)) continue;

                string deviceClass = obj["DeviceClass"]?.ToString() ?? string.Empty;
                string category = MapCategory(deviceClass);
                string version = obj["DriverVersion"]?.ToString() ?? "Versão não informada";
                string manufacturer = obj["Manufacturer"]?.ToString() ?? "Fabricante não informado";
                string rawDate = obj["DriverDate"]?.ToString() ?? string.Empty;
                bool isSigned = obj["IsSigned"] is bool signed && signed;

                string formattedDate = "Data não informada";
                if (rawDate.Length >= 8 &&
                    int.TryParse(rawDate.Substring(0, 4), out int year) &&
                    int.TryParse(rawDate.Substring(4, 2), out int month) &&
                    int.TryParse(rawDate.Substring(6, 2), out int day) &&
                    month is >= 1 and <= 12 && day is >= 1 and <= 31)
                {
                    formattedDate = $"{day:00}/{month:00}/{year:0000}";
                }

                results.Add(new InstalledDriverEntry(
                    Device: deviceName,
                    Category: category,
                    InstalledVersion: version,
                    ReleaseDate: formattedDate,
                    Vendor: manufacturer,
                    Status: isSigned ? "Assinado digitalmente — versão instalada" : "Assinatura não confirmada pelo Windows",
                    OfficialUrl: ResolveVendorSupportUrl(deviceName, manufacturer)
                ));
            }

            return results
                .GroupBy(d => $"{d.Device}|{d.InstalledVersion}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Device, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            LastScanError = $"O Windows não conseguiu concluir a consulta WMI: {ex.Message}";
            return results;
        }
    }

    private static string MapCategory(string deviceClass)
    {
        return deviceClass.ToUpperInvariant() switch
        {
            "DISPLAY" => "Vídeo",
            "NET" => "Rede",
            "MEDIA" or "AUDIOENDPOINT" => "Áudio",
            "SCSIADAPTER" or "HDC" or "DISKDRIVE" => "Armazenamento",
            "SYSTEM" => "Sistema / Chipset",
            "USB" => "USB",
            "BLUETOOTH" => "Bluetooth",
            "HIDCLASS" => "Teclado / Mouse",
            "IMAGE" or "CAMERA" => "Câmera",
            "MONITOR" => "Monitor",
            "PRINTER" => "Impressora",
            "KEYBOARD" => "Teclado",
            "MOUSE" => "Mouse",
            "" => "Outros dispositivos",
            _ => $"Outros ({deviceClass})"
        };
    }

    private static string ResolveVendorSupportUrl(string deviceName, string vendor)
    {
        string combined = $"{deviceName} {vendor}".ToUpperInvariant();
        if (combined.Contains("NVIDIA")) return "https://www.nvidia.com/Download/index.aspx";
        if (combined.Contains("AMD") || combined.Contains("RADEON")) return "https://www.amd.com/en/support";
        if (combined.Contains("INTEL")) return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (combined.Contains("REALTEK")) return "https://www.realtek.com/Download/Index?cate_id=194";
        if (combined.Contains("MICROSOFT")) return "https://www.catalog.update.microsoft.com/";
        return "https://support.microsoft.com/windows";
    }
}