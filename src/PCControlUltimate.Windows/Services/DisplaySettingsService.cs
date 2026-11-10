using System;
using System.Runtime.InteropServices;

namespace PCControlUltimate.Windows.Services;

public static class DisplaySettingsService
{
    private const int ENUM_CURRENT_SETTINGS = -1;

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

    public static object GetPrimaryDisplayInfo()
    {
        // Safe check for Windows platform
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new
            {
                available = false,
                nativeRefreshRateHz = (int?)null,
                monitorName = "Não detectado (Ambiente não-Windows)",
                resolution = "Não detectado"
            };
        }

        try
        {
            var devMode = new DEVMODEW();
            devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODEW));

            if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref devMode))
            {
                int hz = devMode.dmDisplayFrequency;
                int width = devMode.dmPelsWidth;
                int height = devMode.dmPelsHeight;

                if (hz > 0 && width > 0 && height > 0)
                {
                    return new
                    {
                        available = true,
                        nativeRefreshRateHz = hz,
                        monitorName = string.IsNullOrWhiteSpace(devMode.dmDeviceName) ? "Primary Gaming Monitor" : devMode.dmDeviceName,
                        resolution = $"{width}x{height}"
                    };
                }
            }
        }
        catch
        {
            // P/Invoke failed or driver restriction
        }

        return new
        {
            available = false,
            nativeRefreshRateHz = (int?)null,
            monitorName = "Não detectado",
            resolution = "Não detectado"
        };
    }
}