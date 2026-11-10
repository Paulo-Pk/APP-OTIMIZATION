using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PCControlUltimate.Windows.Services;

public record DisplayMonitorInfo(
    string DeviceName,
    string MonitorModel,
    string AdapterName,
    bool IsPrimary,
    int Width,
    int Height,
    int? RefreshRateHz
);

public static class DisplaySettingsService
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICEW
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, int iModeNum, ref DEVMODEW lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICEW lpDisplayDevice, uint dwFlags);

    public static object GetPrimaryDisplayInfo()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new
            {
                available = false,
                nativeRefreshRateHz = (int?)null,
                monitorName = "Não detectado",
                resolution = "Não detectado",
                monitorsCount = 0,
                monitors = Array.Empty<DisplayMonitorInfo>()
            };
        }

        try
        {
            var monitors = new List<DisplayMonitorInfo>();
            uint devIndex = 0;

            while (true)
            {
                var adapter = new DISPLAY_DEVICEW();
                adapter.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICEW));

                if (!EnumDisplayDevicesW(null, devIndex, ref adapter, 0))
                    break;

                devIndex++;

                if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                    continue;

                bool isPrimary = (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

                // Query child monitor device for the real hardware monitor name
                var monitorDevice = new DISPLAY_DEVICEW();
                monitorDevice.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICEW));
                string realMonitorName = "Não detectado";

                if (EnumDisplayDevicesW(adapter.DeviceName, 0, ref monitorDevice, 0) &&
                    !string.IsNullOrWhiteSpace(monitorDevice.DeviceString))
                {
                    realMonitorName = monitorDevice.DeviceString.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(adapter.DeviceString))
                {
                    realMonitorName = adapter.DeviceString.Trim();
                }

                var devMode = new DEVMODEW();
                devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODEW));

                if (EnumDisplaySettingsW(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
                {
                    int width = devMode.dmPelsWidth;
                    int height = devMode.dmPelsHeight;
                    int rawHz = devMode.dmDisplayFrequency;

                    // Windows returns 0 or 1 for "default hardware frequency" on some virtual/headless adapters
                    int? validHz = rawHz > 1 ? rawHz : null;

                    if (width > 0 && height > 0)
                    {
                        monitors.Add(new DisplayMonitorInfo(
                            DeviceName: adapter.DeviceName,
                            MonitorModel: realMonitorName,
                            AdapterName: adapter.DeviceString?.Trim() ?? "Não detectado",
                            IsPrimary: isPrimary,
                            Width: width,
                            Height: height,
                            RefreshRateHz: validHz
                        ));
                    }
                }
            }

            if (monitors.Count > 0)
            {
                var primary = monitors.Find(m => m.IsPrimary) ?? monitors[0];
                return new
                {
                    available = primary.RefreshRateHz.HasValue,
                    nativeRefreshRateHz = primary.RefreshRateHz,
                    monitorName = primary.MonitorModel,
                    adapterName = primary.AdapterName,
                    resolution = $"{primary.Width}x{primary.Height}",
                    monitorsCount = monitors.Count,
                    monitors
                };
            }
        }
        catch
        {
            // P/Invoke failure or restricted session
        }

        return new
        {
            available = false,
            nativeRefreshRateHz = (int?)null,
            monitorName = "Não detectado",
            resolution = "Não detectado",
            monitorsCount = 0,
            monitors = Array.Empty<DisplayMonitorInfo>()
        };
    }
}