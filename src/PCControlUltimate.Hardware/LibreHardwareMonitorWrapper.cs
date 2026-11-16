using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Hardware;

public class LibreHardwareMonitorWrapper : IHardwareMonitorService
{
    private object? _computer;
    private Timer? _pollingTimer;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly object _pollingLock = new();
    private volatile bool _disposed;

    private bool _staticWmiProbed;
    private string? _cachedWmiCpuName;
    private int? _cachedPhysicalCores;
    private int? _cachedLogicalProcessors;
    private string? _cachedRamGeneration;
    private int? _cachedRamSpeedMt;
    private DateTime _lastWmiGpuProbeUtc = DateTime.MinValue;
    private IReadOnlyList<GpuAdapterTelemetryPoint> _cachedWmiGpuAdapters = Array.Empty<GpuAdapterTelemetryPoint>();
    private DateTime _lastWmiCpuClockProbeUtc = DateTime.MinValue;
    private float? _cachedWmiCpuClockMhz;

    public bool IsAvailable { get; private set; }
    public SensorReadingStatus CurrentStatus { get; private set; } = SensorReadingStatus.ReadingUnavailable;
    public string? LastCommunicationError { get; private set; }
    public event EventHandler<HardwareSnapshot>? TelemetryUpdated;

    public LibreHardwareMonitorWrapper()
    {
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private Computer EnsureComputer()
    {
        if (_computer is Computer existing)
            return existing;

        var created = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false
        };
        _computer = created;
        return created;
    }

    public static bool IsSystemDrive(string? driveName, string? systemDrive)
    {
        if (string.IsNullOrWhiteSpace(driveName) || string.IsNullOrWhiteSpace(systemDrive))
            return false;

        return string.Equals(
            driveName.Trim().TrimEnd('\\'),
            systemDrive.Trim().TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _captureLock.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(LibreHardwareMonitorWrapper));
            if (!OperatingSystem.IsWindows())
            {
                IsAvailable = false;
                CurrentStatus = SensorReadingStatus.SensorNotSupported;
                LastCommunicationError = "Leitura física via LibreHardwareMonitor requer sistema operacional Windows nativo.";
                return Task.CompletedTask;
            }

            try
            {
                EnsureComputer().Open();
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
        finally
        {
            _captureLock.Release();
        }
    }

    public async Task<HardwareSnapshot> CaptureSnapshotAsync()
    {
        await _captureLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await CaptureSnapshotCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private Task<HardwareSnapshot> CaptureSnapshotCoreAsync()
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

        // Clear transient read errors before each fresh sample so a recovered sensor
        // does not leave the entire monitor permanently marked as failed.
        CurrentStatus = SensorReadingStatus.Available;
        LastCommunicationError = null;

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
        float? ramAvailable = null;
        float? ramTotal = null;
        string? motherboardModel = null;

        var sensorList = new List<SensorDataPoint>();
        var storageDrives = new List<StorageTelemetryPoint>();
        var gpuAdapters = new List<GpuAdapterTelemetryPoint>();

        try
        {
            // Order hardware so discrete GPUs are listed before integrated GPUs without favoring any specific vendor/model
            var orderedHardware = new List<IHardware>();
            foreach (var rootHardware in EnsureComputer().Hardware)
                AddHardwareTree(rootHardware, orderedHardware);
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
                    CurrentStatus = SensorReadingStatus.CommunicationFailure;
                    LastCommunicationError = $"Falha ao atualizar '{hardware.Name}': {hwEx.Message}";
                    continue;
                }

                bool isGpuHardware = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
                bool isPrimaryGpuCandidate = false;

                if (hardware.HardwareType == HardwareType.Cpu && string.IsNullOrWhiteSpace(cpuModel))
                {
                    cpuModel = hardware.Name;
                }
                else if (hardware.HardwareType == HardwareType.Motherboard && string.IsNullOrWhiteSpace(motherboardModel))
                {
                    motherboardModel = hardware.Name;
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
                float? adapterDedicatedVramUsed = null;
                float? adapterDedicatedVramTotal = null;

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
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value is > 0)
                        {
                            int candidatePriority = GetCpuTemperatureSensorPriority(sensor.Name);
                            if (cpuTemp is null || candidatePriority > cpuTempPriority ||
                                (candidatePriority == cpuTempPriority && sensor.Value.Value > cpuTemp.Value))
                            {
                                cpuTemp = sensor.Value;
                                cpuTempPriority = candidatePriority;
                            }
                        }
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            cpuLoad ??= sensor.Value;
                        if (sensor.SensorType == SensorType.Clock && sensor.Value is > 0)
                        {
                            // Ignore zero-MHz readings from sleeping cores and bus-clock sensors (e.g. 100 MHz Bus Speed),
                            // reporting the fastest currently active core instead of freezing on the first core sensor.
                            if (!sensor.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuClockFallback = Math.Max(cpuClockFallback ?? 0, sensor.Value.Value);
                            }
                            if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                cpuClock = Math.Max(cpuClock ?? 0, sensor.Value.Value);
                        }
                        if (sensor.SensorType == SensorType.Power &&
                            (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                             sensor.Name.Contains("Cores", StringComparison.OrdinalIgnoreCase)))
                        {
                            cpuPower ??= sensor.Value;
                        }
                    }
                    else if (hardware.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO)
                    {
                        // When Windows 11 HVCI / Memory Integrity blocks direct CPU MSR temperature sensors,
                        // some boards expose the real CPU socket/package sensor on the Motherboard/SuperIO node.
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value is > 0 &&
                            (sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                             sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                             sensor.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)))
                        {
                            const int motherboardCpuTempPriority = 5;
                            if (cpuTemp is null || motherboardCpuTempPriority > cpuTempPriority)
                            {
                                cpuTemp = sensor.Value;
                                cpuTempPriority = motherboardCpuTempPriority;
                            }
                        }
                    }
                    else if (isGpuHardware)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value is > 0)
                        {
                            if (sensor.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                                sensor.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase))
                            {
                                adapterHotspot ??= sensor.Value;
                            }
                            else if ((sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Contains("GPU Temp", StringComparison.OrdinalIgnoreCase) ||
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
                        // Only the GPU's dedicated-memory sensors represent VRAM.
                        // "D3D Shared Memory" is backed by system RAM and must never be
                        // merged into the dedicated VRAM figures.
                        if (sensor.SensorType == SensorType.SmallData &&
                            sensor.Name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase))
                            adapterVramUsed ??= sensor.Value;
                        if (sensor.SensorType == SensorType.SmallData &&
                            sensor.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
                            adapterVramTotal ??= sensor.Value;

                        // Some drivers expose dedicated VRAM only through the D3D counters.
                        // Keep these as a fallback, but intentionally ignore D3D Shared Memory.
                        if (sensor.SensorType == SensorType.SmallData &&
                            sensor.Name.Equals("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
                            adapterDedicatedVramUsed ??= sensor.Value;
                        if (sensor.SensorType == SensorType.SmallData &&
                            sensor.Name.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase))
                            adapterDedicatedVramTotal ??= sensor.Value;

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
                    else if (hardware.HardwareType == HardwareType.Memory &&
                             hardware.Identifier.ToString().Equals("/ram", StringComparison.OrdinalIgnoreCase) &&
                             !hardware.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        // LibreHardwareMonitor also reports "Virtual Memory" as HardwareType.Memory
                        // (with a /vram identifier). That is the page file, not physical RAM or GPU VRAM.
                        // The physical RAM node (/ram) may be named "Total Memory" or "Generic Memory" depending on SMBIOS.
                        if (sensor.SensorType == SensorType.Data && sensor.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase))
                            ramUsed ??= sensor.Value;
                        if (sensor.SensorType == SensorType.Data && sensor.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase))
                            ramAvailable ??= sensor.Value;
                    }
                    else if (hardware.HardwareType == HardwareType.Storage)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value is > 0)
                            driveTemp ??= sensor.Value;
                    }
                }

                if (isGpuHardware)
                {
                    adapterVramUsed ??= adapterDedicatedVramUsed;
                    adapterVramTotal ??= adapterDedicatedVramTotal;

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
                        PnpDeviceId: hardware.Identifier.ToString(),
                        SensorIdentifier: hardware.Identifier.ToString()
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

        // Enrich with cached Windows WMI / Registry / Win32 counters when running on Windows
        if (OperatingSystem.IsWindows())
        {
            EnsureStaticHardwareMetadataProbed();

            if (string.IsNullOrWhiteSpace(cpuModel) && !string.IsNullOrWhiteSpace(_cachedWmiCpuName))
            {
                cpuModel = _cachedWmiCpuName;
            }

            // Merge complementary WMI & Registry GPU adapters (which provide 64-bit qwMemorySize VRAM Total, DriverVersion, and PNPDeviceID)
            if ((DateTime.UtcNow - _lastWmiGpuProbeUtc).TotalSeconds >= 30)
            {
                _cachedWmiGpuAdapters = UniversalHardwareEngine.QueryWmiAndRegistryGpuAdapters();
                _lastWmiGpuProbeUtc = DateTime.UtcNow;
            }

            if (_cachedWmiGpuAdapters.Count > 0)
            {
                var mergedAdapters = UniversalHardwareEngine.MergeGpuAdapters(gpuAdapters, _cachedWmiGpuAdapters);
                var physicalMerged = mergedAdapters
                    .Where(a =>
                    {
                        var kind = UniversalHardwareEngine.ClassifyGpuAdapterKind(a.Model, a.PnpDeviceId, null, a.HardwareType);
                        return kind is GpuAdapterKind.PhysicalDiscrete or GpuAdapterKind.PhysicalIntegrated;
                    })
                    .OrderByDescending(a =>
                        UniversalHardwareEngine.ClassifyGpuAdapterKind(a.Model, a.PnpDeviceId, null, a.HardwareType) == GpuAdapterKind.PhysicalDiscrete)
                    .ThenByDescending(a => a.CoreTempC.HasValue || a.LoadPercent.HasValue)
                    .ThenByDescending(a => a.VramTotalMb ?? 0f)
                    .ToList();

                if (physicalMerged.Count > 0)
                {
                    gpuAdapters = physicalMerged;
                    var primaryPhysical = physicalMerged[0];
                    gpuModel ??= primaryPhysical.Model;
                    if (string.Equals(primaryPhysical.Model, gpuModel, StringComparison.OrdinalIgnoreCase) || gpuTemp is null)
                    {
                        gpuModel = primaryPhysical.Model;
                        gpuTemp ??= primaryPhysical.CoreTempC;
                        gpuHotspot ??= primaryPhysical.HotspotTempC;
                        gpuLoad ??= primaryPhysical.LoadPercent;
                        gpuClock ??= primaryPhysical.CoreClockMhz;
                        gpuPower ??= primaryPhysical.PowerWatts;
                        gpuFanRpm ??= primaryPhysical.FanRpm;
                        gpuVramUsed ??= primaryPhysical.VramUsedMb;
                        gpuVramTotal ??= primaryPhysical.VramTotalMb;
                    }
                }
            }
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
        float? primaryPhysicalDriveTemp = storageDrives.FirstOrDefault(d => d.TemperatureC.HasValue)?.TemperatureC;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    float totalGb = (float)Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                    float freeGb = (float)Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Sem rótulo" : drive.VolumeLabel;
                    bool isSys = IsSystemDrive(drive.Name, Environment.GetEnvironmentVariable("SystemDrive"));
                    storageDrives.Add(new StorageTelemetryPoint(
                        Model: $"Volume {drive.Name.TrimEnd('\\')} — {label}",
                        CapacityGb: totalGb,
                        FreeSpaceGb: freeGb,
                        TemperatureC: isSys ? primaryPhysicalDriveTemp : null,
                        IsSystemDisk: isSys
                    ));
                }
            }
        }
        catch { }

        if (ramUsed.HasValue && ramAvailable.HasValue)
            ramTotal = ramUsed.Value + ramAvailable.Value;

        if (OperatingSystem.IsWindows() && (!ramUsed.HasValue || !ramTotal.HasValue))
        {
            var (winTotalGb, winUsedGb) = TryReadWin32PhysicalMemoryGb();
            ramTotal ??= winTotalGb;
            ramUsed ??= winUsedGb;
        }

        // Keep the legacy top-level GPU VRAM fields consistent with the selected
        // primary adapter, including when only the D3D dedicated-memory fallback exists.
        var primaryGpuTelemetry = gpuAdapters.FirstOrDefault(adapter =>
            string.Equals(adapter.Model, gpuModel, StringComparison.OrdinalIgnoreCase))
            ?? gpuAdapters.FirstOrDefault();
        gpuVramUsed ??= primaryGpuTelemetry?.VramUsedMb;
        gpuVramTotal ??= primaryGpuTelemetry?.VramTotalMb;

        cpuClock ??= cpuClockFallback;
        if (OperatingSystem.IsWindows() && !cpuClock.HasValue)
        {
            cpuClock = TryGetWmiCpuCurrentClockMhz();
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
            LastCommunicationError,
            gpuAdapters,
            _cachedPhysicalCores,
            _cachedLogicalProcessors,
            _cachedRamGeneration,
            _cachedRamSpeedMt,
            motherboardModel
        );

        return Task.FromResult(snapshot);
    }

    private void EnsureStaticHardwareMetadataProbed()
    {
        if (_staticWmiProbed || !OperatingSystem.IsWindows()) return;
        _staticWmiProbed = true;

        try
        {
            var (cpuName, physCores, logCores) = UniversalHardwareEngine.QueryWmiCpuDetails();
            _cachedWmiCpuName = cpuName;
            _cachedPhysicalCores = physCores;
            _cachedLogicalProcessors = logCores ?? Math.Max(1, Environment.ProcessorCount);
        }
        catch
        {
            _cachedLogicalProcessors = Math.Max(1, Environment.ProcessorCount);
        }

        try
        {
            var (memGen, memSpeed) = UniversalHardwareEngine.QueryWmiMemoryDetails();
            _cachedRamGeneration = memGen;
            _cachedRamSpeedMt = memSpeed;
        }
        catch { }
    }

    private float? TryGetWmiCpuCurrentClockMhz()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if ((DateTime.UtcNow - _lastWmiCpuClockProbeUtc).TotalSeconds < 3)
            return _cachedWmiCpuClockMhz;

        _lastWmiCpuClockProbeUtc = DateTime.UtcNow;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
            using var collection = searcher.Get();
            float maxClock = 0f;
            foreach (ManagementBaseObject obj in collection)
            {
                if (obj["CurrentClockSpeed"] != null &&
                    float.TryParse(obj["CurrentClockSpeed"].ToString(), out float clk) &&
                    clk > maxClock)
                {
                    maxClock = clk;
                }
            }
            _cachedWmiCpuClockMhz = maxClock > 0 ? maxClock : null;
        }
        catch { }

        return _cachedWmiCpuClockMhz;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (float? TotalGb, float? UsedGb) TryReadWin32PhysicalMemoryGb()
    {
        if (!OperatingSystem.IsWindows()) return (null, null);
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = Math.Max(0.0, totalGb - availGb);
                return ((float)Math.Round(totalGb, 1), (float)Math.Round(usedGb, 1));
            }
        }
        catch { }
        return (null, null);
    }

    public void StartPolling(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "O intervalo de telemetria deve ser maior que zero.");

        lock (_pollingLock)
        {
            if (_disposed) return;

            _pollingTimer?.Dispose();
            _pollingTimer = new Timer(async _ =>
            {
                if (_disposed) return;
                try
                {
                    var snap = await CaptureSnapshotAsync().ConfigureAwait(false);
                    if (!_disposed)
                        TelemetryUpdated?.Invoke(this, snap);
                }
                catch (ObjectDisposedException)
                {
                    // Shutdown may race with a scheduled timer callback.
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                    {
                        LastCommunicationError = $"Falha inesperada no ciclo de telemetria: {ex.Message}";
                        CurrentStatus = SensorReadingStatus.CommunicationFailure;
                    }
                }
            }, null, TimeSpan.Zero, interval);
        }
    }

    public void StopPolling()
    {
        lock (_pollingLock)
        {
            _pollingTimer?.Change(Timeout.Infinite, 0);
        }
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

    private static void AddHardwareTree(IHardware hardware, List<IHardware> destination)
    {
        destination.Add(hardware);
        // Some CPU/GPU sensor groups are exposed as child hardware nodes by LHM.
        // Visit them explicitly; reading only the root node silently misses valid temperatures/clocks.
        foreach (var child in hardware.SubHardware)
            AddHardwareTree(child, destination);
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void CloseComputerCore()
    {
        (_computer as Computer)?.Close();
    }

    public void Dispose()
    {
        lock (_pollingLock)
        {
            if (_disposed) return;
            _disposed = true;
            _pollingTimer?.Change(Timeout.Infinite, 0);
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }

        // Wait for an in-flight hardware read before closing LibreHardwareMonitor.
        _captureLock.Wait();
        try
        {
            if (IsAvailable && OperatingSystem.IsWindows())
            {
                CloseComputerCore();
            }
        }
        catch
        {
            // Disposal must remain best-effort even if a hardware driver fails to close.
        }
        finally
        {
            _captureLock.Release();
        }
        GC.SuppressFinalize(this);
    }
}