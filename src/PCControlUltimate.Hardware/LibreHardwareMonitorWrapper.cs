using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    public SensorReadingStatus CurrentStatus { get; private set; } = SensorReadingStatus.ReadingUnavailable;
    public string? LastCommunicationError { get; private set; }
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
        if (!OperatingSystem.IsWindows())
        {
            IsAvailable = false;
            CurrentStatus = SensorReadingStatus.SensorNotSupported;
            LastCommunicationError = "Leitura física via LibreHardwareMonitor requer sistema operacional Windows nativo.";
            return Task.CompletedTask;
        }

        try
        {
            _computer.Open();
            IsAvailable = true;
            CurrentStatus = SensorReadingStatus.Available;
            LastCommunicationError = null;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            CurrentStatus = SensorReadingStatus.CommunicationFailure;
            LastCommunicationError = $"Falha ao inicializar sensores de hardware (verifique elevação UAC): {ex.Message}";
        }
        return Task.CompletedTask;
    }

    private IEnumerable<IHardware> EnumerateHardwareTree()
    {
        var pending = new Stack<IHardware>(_computer.Hardware.Reverse());
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            try
            {
                current.Update();
                for (var i = current.SubHardware.Length - 1; i >= 0; i--)
                    pending.Push(current.SubHardware[i]);
            }
            catch
            {
                // Per-device failures are recorded by the capture loop.
            }
        }
    }

    private static int GetGpuSelectionPriority(IHardware hardware)
    {
        if (hardware.HardwareType is not (HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel))
            return 0;
        var name = hardware.Name ?? string.Empty;
        if (name.Contains("RX 7900 XTX", StringComparison.OrdinalIgnoreCase)) return 100;
        if (hardware.HardwareType == HardwareType.GpuAmd &&
            !name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Integrated", StringComparison.OrdinalIgnoreCase)) return 60;
        return 10;
    }

    private static int GetCpuTemperaturePriority(HardwareType hardwareType, string sensorName)
    {
        // Prefer AMD's die/control readings, then package/CCD readings. Some boards
        // expose the CPU socket temperature under Motherboard/SuperIO instead of Cpu.
        if (sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
            sensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 100;
        if (sensorName.Contains("CPU Die", StringComparison.OrdinalIgnoreCase) ||
            sensorName.Contains("Die (average)", StringComparison.OrdinalIgnoreCase)) return 90;
        if (sensorName.Contains("CCD", StringComparison.OrdinalIgnoreCase)) return 80;
        if (sensorName.Contains("CPU Package", StringComparison.OrdinalIgnoreCase) ||
            sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 70;
        if (hardwareType == HardwareType.Cpu &&
            (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
             sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase))) return 60;
        if (hardwareType == HardwareType.Motherboard &&
            sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return 40;
        return 0;
    }

    public Task<HardwareSnapshot> CaptureSnapshotAsync()
    {
        if (_disposed)
        {
            return Task.FromResult(new HardwareSnapshot(
                DateTime.UtcNow,
                null, null, null, null, null, null, null, null,
                Array.Empty<SensorDataPoint>(),
                MonitorStatus: SensorReadingStatus.CommunicationFailure,
                DiagnosticMessage: "Serviço de monitoramento encerrado (Disposed)."
            ));
        }

        if (!IsAvailable)
        {
            return Task.FromResult(new HardwareSnapshot(
                DateTime.UtcNow,
                null, null, null, null, null, null, null, null,
                Array.Empty<SensorDataPoint>(),
                MonitorStatus: CurrentStatus,
                DiagnosticMessage: LastCommunicationError ?? "Sensores de hardware indisponíveis."
            ));
        }

        string? cpuModel = null;
        float? cpuTemp = null;
        var cpuTempPriority = 0;
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

        try
        {
            var hardwareItems = EnumerateHardwareTree().OrderByDescending(GetGpuSelectionPriority).ToList();
            foreach (var hardware in hardwareItems)
            {
                try
                {
                    hardware.Update();
                }
                catch (Exception hwEx)
                {
                    sensorList.Add(new SensorDataPoint(
                        $"{hardware.Identifier}/comm-error",
                        $"{hardware.Name} (Erro de leitura)",
                        hardware.HardwareType.ToString(),
                        "Diagnostic",
                        null,
                        string.Empty,
                        false,
                        SensorReadingStatus.CommunicationFailure
                    ));
                    LastCommunicationError = hwEx.Message;
                    continue;
                }

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
                    bool hasValidReading = sensor.Value.HasValue && !float.IsNaN(sensor.Value.Value) && !float.IsInfinity(sensor.Value.Value);
                    var status = hasValidReading
                        ? SensorReadingStatus.Available
                        : SensorReadingStatus.ReadingUnavailable;

                    var point = new SensorDataPoint(
                        sensor.Identifier.ToString(),
                        sensor.Name,
                        hardware.HardwareType.ToString(),
                        sensor.SensorType.ToString(),
                        hasValidReading ? sensor.Value : null,
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
                        hasValidReading,
                        status
                    );
                    sensorList.Add(point);

                    if (!hasValidReading) continue;

                    // CPU temperature may be published by the CPU node or by the
                    // motherboard/SuperIO node. Rank candidates instead of accepting
                    // the first matching sensor (which can be a socket/board reading).
                    if (sensor.SensorType == SensorType.Temperature &&
                        hardware.HardwareType is HardwareType.Cpu or HardwareType.Motherboard)
                    {
                        var candidatePriority = GetCpuTemperaturePriority(hardware.HardwareType, sensor.Name);
                        if (candidatePriority > cpuTempPriority)
                        {
                            cpuTemp = sensor.Value;
                            cpuTempPriority = candidatePriority;
                        }
                    }

                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
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
                                sensor.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase))
                            {
                                gpuHotspot ??= sensor.Value;
                            }
                            else if ((sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase)) &&
                                     !sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) &&
                                     !sensor.Name.Contains("SoC", StringComparison.OrdinalIgnoreCase))
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
        }
        catch (Exception ex)
        {
            CurrentStatus = SensorReadingStatus.CommunicationFailure;
            LastCommunicationError = $"Erro de comunicação ao ler sensores: {ex.Message}";
        }

        // Explicitly record SensorNotSupported if GPU is present but Hotspot sensor is not exposed by the hardware
        if (!string.IsNullOrWhiteSpace(gpuModel) && !gpuHotspot.HasValue)
        {
            sensorList.Add(new SensorDataPoint(
                "/gpu/hotspot-unsupported",
                "GPU Hot Spot",
                "Gpu",
                "Temperature",
                null,
                "°C",
                false,
                SensorReadingStatus.SensorNotSupported
            ));
        }

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
            storageDrives,
            CurrentStatus,
            LastCommunicationError
        );

        return Task.FromResult(snapshot);
    }

    public void StartPolling(TimeSpan interval)
    {
        if (_disposed) return;
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(async _ =>
        {
            if (_disposed) return;
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
        try
        {
            _pollingTimer?.Change(Timeout.Infinite, 0);
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _computer.Close();
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}