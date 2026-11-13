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
        int cpuTempPriority = 0;
        float? cpuLoad = null;
        float? cpuClock = null;
        float? cpuClockFallback = null;
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
        var gpuAdapters = new List<GpuAdapterTelemetryPoint>();

        try
        {
            // Order hardware so discrete GPUs are listed before integrated GPUs without favoring any specific vendor/model
            var orderedHardware = new List<IHardware>(_computer.Hardware);
            orderedHardware.Sort((a, b) => GetHardwarePriorityScore(b).CompareTo(GetHardwarePriorityScore(a)));

            foreach (var hardware in orderedHardware)
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

                bool isGpuHardware = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
                bool isPrimaryGpuCandidate = false;

                if (hardware.HardwareType == HardwareType.Cpu && string.IsNullOrWhiteSpace(cpuModel))
                {
                    cpuModel = hardware.Name;
                }
                else if (isGpuHardware && string.IsNullOrWhiteSpace(gpuModel))
                {
                    gpuModel = hardware.Name;
                    isPrimaryGpuCandidate = true;
                }

                float? driveTemp = null;
                float? adapterCoreTemp = null;
                float? adapterHotspot = null;
                float? adapterLoad = null;
                float? adapterClock = null;
                float? adapterPower = null;
                float? adapterFanRpm = null;
                float? adapterVramUsed = null;
                float? adapterVramTotal = null;

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

                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            int candidatePriority = GetCpuTemperatureSensorPriority(sensor.Name);
                            if (cpuTemp is null || candidatePriority > cpuTempPriority)
                            {
                                cpuTemp = sensor.Value;
                                cpuTempPriority = candidatePriority;
                            }
                        }
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            cpuLoad ??= sensor.Value;
                        if (sensor.SensorType == SensorType.Clock && sensor.Value is not null)
                        {
                            cpuClockFallback = Math.Max(cpuClockFallback ?? 0, sensor.Value.Value);
                            if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                cpuClock ??= sensor.Value;
                        }
                        if (sensor.SensorType == SensorType.Power &&
                            (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                             sensor.Name.Contains("Cores", StringComparison.OrdinalIgnoreCase)))
                        {
                            cpuPower ??= sensor.Value;
                        }
                    }
                    else if (isGpuHardware)
                    {
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if (sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase))
                            {
                                adapterHotspot ??= sensor.Value;
                            }
                            else if ((sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase)) &&
                                     !sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) &&
                                     !sensor.Name.Contains("SoC", StringComparison.OrdinalIgnoreCase))
                            {
                                adapterCoreTemp ??= sensor.Value;
                            }
                        }
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            adapterLoad ??= sensor.Value;
                        if (sensor.SensorType == SensorType.Clock && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            adapterClock ??= sensor.Value;
                        if (sensor.SensorType == SensorType.Power)
                            adapterPower ??= sensor.Value;
                        if (sensor.SensorType == SensorType.Fan)
                            adapterFanRpm ??= sensor.Value;
                        if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                            adapterVramUsed ??= sensor.Value;
                        if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                            adapterVramTotal ??= sensor.Value;

                        if (isPrimaryGpuCandidate)
                        {
                            gpuTemp ??= adapterCoreTemp;
                            gpuHotspot ??= adapterHotspot;
                            gpuLoad ??= adapterLoad;
                            gpuClock ??= adapterClock;
                            gpuPower ??= adapterPower;
                            gpuFanRpm ??= adapterFanRpm;
                            gpuVramUsed ??= adapterVramUsed;
                            gpuVramTotal ??= adapterVramTotal;
                        }
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

                if (isGpuHardware)
                {
                    gpuAdapters.Add(new GpuAdapterTelemetryPoint(
                        Model: hardware.Name,
                        HardwareType: hardware.HardwareType.ToString(),
                        CoreTempC: adapterCoreTemp,
                        HotspotTempC: adapterHotspot,
                        LoadPercent: adapterLoad,
                        CoreClockMhz: adapterClock,
                        PowerWatts: adapterPower,
                        FanRpm: adapterFanRpm,
                        VramUsedMb: adapterVramUsed,
                        VramTotalMb: adapterVramTotal,
                        DriverVersion: null,
                        PnpDeviceId: hardware.Identifier.ToString()
                    ));
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

        // Also append logical fixed volumes: physical drive sensors do not expose mount-point capacity.
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    float totalGb = (float)Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                    float freeGb = (float)Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Sem rótulo" : drive.VolumeLabel;
                    storageDrives.Add(new StorageTelemetryPoint(
                        Model: $"Volume {drive.Name.TrimEnd('\\')} — {label}",
                        CapacityGb: totalGb,
                        FreeSpaceGb: freeGb,
                        TemperatureC: null
                    ));
                }
            }
        }
        catch { }

        cpuClock ??= cpuClockFallback;

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
            LastCommunicationError,
            gpuAdapters
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

    public static bool IsIntegratedGpuName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // Discrete Intel Arc GPUs must not be classified as integrated
        if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Iris Xe MAX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 780M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 760M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 740M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 680M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 660M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 890M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 880M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon 610M", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Radeon Vega", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Intel(R) UHD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Intel(R) Iris", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Iris(R) Xe", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Iris Xe", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Iris Plus", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Iris Pro", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Intel(R) HD Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Intel(R) Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Intel Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Adreno", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Selects the most representative real CPU temperature sensor without assuming one vendor's naming convention.
    /// AMD Ryzen sensors such as Core (Tctl/Tdie), CCDx (Tdie), and CCDs Max (Tdie) are explicitly recognized.
    /// Any other valid temperature sensor exposed by the CPU is retained as a last-resort real reading.
    /// </summary>
    public static int GetCpuTemperatureSensorPriority(string? sensorName)
    {
        if (string.IsNullOrWhiteSpace(sensorName)) return 0;

        if (sensorName.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase)) return 100;
        if (sensorName.Contains("CCDs Max", StringComparison.OrdinalIgnoreCase)) return 98;
        if (sensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 95;
        if (sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) return 90;
        if (sensorName.Contains("Die", StringComparison.OrdinalIgnoreCase)) return 88;
        if (sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 85;
        if (sensorName.Contains("CCD", StringComparison.OrdinalIgnoreCase) &&
            sensorName.Contains("Max", StringComparison.OrdinalIgnoreCase)) return 82;
        if (sensorName.Contains("CCD", StringComparison.OrdinalIgnoreCase)) return 78;
        if (sensorName.Contains("Core Max", StringComparison.OrdinalIgnoreCase)) return 74;
        if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 68;
        if (sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return 60;

        return 1;
    }

    private static int GetHardwarePriorityScore(IHardware hardware)
    {
        if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
        {
            if (IsIntegratedGpuName(hardware.Name))
            {
                return 10; // iGPU processed after discrete GPUs
            }

            return 95; // All discrete GPUs across AMD, NVIDIA, and Intel Arc have equal priority
        }

        return hardware.HardwareType switch
        {
            HardwareType.Cpu => 90,
            HardwareType.Memory => 70,
            HardwareType.Motherboard => 60,
            HardwareType.Storage => 50,
            _ => 20
        };
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