using System;
using System.Collections.Generic;
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
    string OfficialUrl
);

public static class DriverScannerService
{
    public static IReadOnlyList<InstalledDriverEntry> QuerySignedDrivers()
    {
        var results = new List<InstalledDriverEntry>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return results;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL AND DriverVersion IS NOT NULL"
            );

            foreach (ManagementObject obj in searcher.Get())
            {
                string deviceClass = obj["DeviceClass"]?.ToString() ?? string.Empty;
                string? category = MapCategory(deviceClass);
                if (category == null) continue;

                string deviceName = obj["DeviceName"]?.ToString() ?? "Dispositivo Desconhecido";
                string version = obj["DriverVersion"]?.ToString() ?? "Não disponível";
                string manufacturer = obj["Manufacturer"]?.ToString() ?? "Fabricante OEM";
                string rawDate = obj["DriverDate"]?.ToString() ?? string.Empty;

                string formattedDate = "Não disponível";
                if (rawDate.Length >= 8)
                {
                    formattedDate = $"{rawDate.Substring(0, 4)}-{rawDate.Substring(4, 2)}-{rawDate.Substring(6, 2)}";
                }

                string officialUrl = ResolveVendorSupportUrl(deviceName, manufacturer);

                results.Add(new InstalledDriverEntry(
                    Device: deviceName,
                    Category: category,
                    InstalledVersion: version,
                    ReleaseDate: formattedDate,
                    Vendor: manufacturer,
                    Status: "Verificado via WMI",
                    OfficialUrl: officialUrl
                ));
            }
        }
        catch
        {
            // WMI query failed or permission restricted
        }

        return results;
    }

    private static string? MapCategory(string deviceClass)
    {
        return deviceClass.ToUpperInvariant() switch
        {
            "DISPLAY" => "GPU",
            "NET" => "Rede",
            "MEDIA" or "AUDIOENDPOINT" => "Audio",
            "SCSIADAPTER" or "HDC" or "DISKDRIVE" => "Armazenamento",
            "SYSTEM" => "Chipset",
            _ => null
        };
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
