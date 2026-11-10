using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Hardware;

public class LibreHardwareMonitorWrapper : IHardwareMonitorService
{
    private readonly Computer _computer;
    private Timer? _pollingTimer;
    private bool _disposed;

    public bool IsAvailable { get; private set; }
    public event EventHandler<HardwareSnapshot>? TelemetryUpdated;

    public LibreHardwareMonitorWrapper()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false
        };
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _computer.Open();
            IsAvailable = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
        return Task.CompletedTask;
    }

    public Task<HardwareSnapshot> CaptureSnapshotAsync()
    {
        if (!IsAvailable)
        {
            return Task.FromResult(new HardwareSnapshot(
                DateTime.UtcNow,
                null, null, null, null, null, null, null, null,
                Array.Empty<SensorDataPoint>()
            ));
        }

        string? cpuModel = null;
        float? cpuTemp = null;
        float? cpuLoad = null;
        float? cpuClock = null;
        float? cpuPower = null;

        string? gpuModel = null;
        float? gpuTemp = null;
        float? gpuHotspot = null;
        float? gpuLoad = null;
        float? gpuClock = null;
        float? gpuPower = null;
        float? gpuFanRpm = null;
        float? gpuVramUsed = null;
        float? gpuVramTotal = null;

        float? ramUsed = null;
        float? ramTotal = null;

        var sensorList = new List<SensorDataPoint>();
        var storageDrives = new List<StorageTelemetryPoint>();

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();

            if (hardware.HardwareType == HardwareType.Cpu && string.IsNullOrWhiteSpace(cpuModel))
            {
                cpuModel = hardware.Name;
            }
            else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel &&
                     string.IsNullOrWhiteSpace(gpuModel))
            {
                gpuModel = hardware.Name;
            }

            float? driveTemp = null;

            foreach (var sensor in hardware.Sensors)
            {
                var point = new SensorDataPoint(
                    sensor.Identifier.ToString(),
                    sensor.Name,
                    hardware.HardwareType.ToString(),
                    sensor.SensorType.ToString(),
                    sensor.Value,
                    sensor.SensorType switch
                    {
                        SensorType.Temperature => "°C",
                        SensorType.Load => "%",
                        SensorType.Clock => "MHz",
                        SensorType.Voltage => "V",
                        SensorType.Power => "W",
                        SensorType.Fan => "RPM",
                        _ => string.Empty
                    },
                    sensor.Value.HasValue
                );
                sensorList.Add(point);

                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    if (sensor.SensorType == SensorType.Temperature &&
                        (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                         sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                         sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
                    {
                        cpuTemp ??= sensor.Value;
                    }
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        cpuLoad ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Clock && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        cpuClock ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Power &&
                        (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                         sensor.Name.Contains("Cores", StringComparison.OrdinalIgnoreCase)))
                    {
                        cpuPower ??= sensor.Value;
                    }
                }
                else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    if (sensor.SensorType == SensorType.Temperature)
                    {
                        if (sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuHotspot ??= sensor.Value;
                        }
                        else if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                 sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuTemp ??= sensor.Value;
                        }
                    }
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuLoad ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Clock && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuClock ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Power)
                        gpuPower ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Fan)
                        gpuFanRpm ??= sensor.Value;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                        gpuVramUsed ??= sensor.Value;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                        gpuVramTotal ??= sensor.Value;
                }
                else if (hardware.HardwareType == HardwareType.Memory)
                {
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                        ramUsed ??= sensor.Value;
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Memory Available", StringComparison.OrdinalIgnoreCase))
                        ramTotal ??= (ramUsed ?? 0) + (sensor.Value ?? 0);
                }
                else if (hardware.HardwareType == HardwareType.Storage)
                {
                    if (sensor.SensorType == SensorType.Temperature)
                        driveTemp ??= sensor.Value;
                }
            }

            if (hardware.HardwareType == HardwareType.Storage)
            {
                storageDrives.Add(new StorageTelemetryPoint(
                    Model: hardware.Name,
                    CapacityGb: null,
                    FreeSpaceGb: null,
                    TemperatureC: driveTemp
                ));
            }
        }

        // Enrich storage drives with real logical volume capacities if none were added
        if (storageDrives.Count == 0)
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        float totalGb = (float)Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                        float freeGb = (float)Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                        storageDrives.Add(new StorageTelemetryPoint(
                            Model: $"Volume {drive.Name} ({drive.VolumeLabel})",
                            CapacityGb: totalGb,
                            FreeSpaceGb: freeGb,
                            TemperatureC: null
                        ));
                    }
                }
            }
            catch { }
        }

        var snapshot = new HardwareSnapshot(
            DateTime.UtcNow,
            cpuTemp,
            cpuLoad,
            gpuTemp,
            gpuHotspot,
            gpuVramUsed,
            gpuVramTotal,
            ramUsed,
            ramTotal,
            sensorList,
            cpuModel,
            cpuClock,
            cpuPower,
            gpuModel,
            gpuLoad,
            gpuClock,
            gpuPower,
            gpuFanRpm,
            storageDrives
        );

        return Task.FromResult(snapshot);
    }

    public void StartPolling(TimeSpan interval)
    {
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(async _ =>
        {
            var snap = await CaptureSnapshotAsync();
            TelemetryUpdated?.Invoke(this, snap);
        }, null, TimeSpan.Zero, interval);
    }

    public void StopPolling()
    {
        _pollingTimer?.Change(Timeout.Infinite, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollingTimer?.Dispose();
        _computer.Close();
    }
}