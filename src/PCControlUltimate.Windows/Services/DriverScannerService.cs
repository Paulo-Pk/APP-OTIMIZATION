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
        LastError = null;
        var results = new List<InstalledDriverEntry>();
        LastScanError = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            LastScanError = "A consulta de drivers requer Windows.";
            return results;
        }

        string? signedQueryError = null;

        // Win32_PnPSignedDriver is the authoritative source for driver package metadata.
        // Some Windows installations expose few/no rows here, so the PnP inventory below
        // is used as a safe read-only fallback instead of displaying an empty table.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"
            );

            foreach (ManagementObject obj in signedSearcher.Get())
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

            // If Windows exposes no signed-driver package rows, fall back to the
            // Plug and Play device inventory. This is device discovery only: it must
            // never imply that a driver version or digital signature was verified.
            if (results.Count == 0)
            {
                try
                {
                    using var deviceSearcher = new ManagementObjectSearcher(
                        "SELECT Name, Manufacturer, PNPClass FROM Win32_PnPEntity WHERE Name IS NOT NULL"
                    );

                    foreach (ManagementObject device in deviceSearcher.Get())
                    {
                        string name = device["Name"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string manufacturer = device["Manufacturer"]?.ToString() ?? "Fabricante não informado";
                        string deviceClass = device["PNPClass"]?.ToString() ?? string.Empty;
                        results.Add(new InstalledDriverEntry(
                            Device: name,
                            Category: MapCategory(deviceClass),
                            InstalledVersion: "Versão não informada pelo Windows",
                            ReleaseDate: "Data não informada",
                            Vendor: manufacturer,
                            Status: "Dispositivo encontrado — pacote/assinatura não verificados",
                            OfficialUrl: ResolveVendorSupportUrl(name, manufacturer)
                        ));
                    }
                }
                catch (Exception fallbackException)
                {
                    LastScanError = $"A consulta de pacotes de driver e o inventário Plug and Play falharam: {fallbackException.Message}";
                }
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
        string formattedDate = "N/D";
        if (!string.IsNullOrWhiteSpace(rawDate) && rawDate.Length >= 8 &&
            DateTime.TryParseExact(rawDate[..8], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
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
        if (combined.Contains("MICROSOFT")) return "https://www.catalog.update.microsoft.com/";
        return "https://support.microsoft.com/windows";
    }
}