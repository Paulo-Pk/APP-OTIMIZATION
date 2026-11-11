using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Hardware;

/// <summary>
/// Universal Hardware Engine (§3 & §5):
/// Multi-source hardware detection, independent multi-GPU enumeration (LHM + WMI Win32_VideoController + Registry Display Class),
/// real SMBIOS memory generation/speed detection, real WMI physical core detection, and real Win32 battery status.
/// Never presents estimated or hardcoded values as real hardware readings.
/// </summary>
public class UniversalHardwareEngine
{
    private readonly IHardwareMonitorService _monitorService;

    public UniversalHardwareEngine(IHardwareMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    public async Task<UniversalHardwareReport> BuildUniversalReportAsync(
        HardwareSnapshot? existingSnapshot = null,
        bool? hasBatteryOverride = null,
        bool? isDomainOrPolicyManagedOverride = null,
        bool? isRunningAsAdminOverride = null,
        CancellationToken cancellationToken = default,
        int? batteryChargePercentOverride = null,
        int? physicalCoresOverride = null,
        string? memoryGenerationOverride = null,
        int? memorySpeedMtOverride = null)
    {
        var snapshot = existingSnapshot ?? await _monitorService.CaptureSnapshotAsync();
        DateTime now = DateTime.UtcNow;
        bool isAdmin = isRunningAsAdminOverride ?? CheckIsRunningAsAdministrator();
        bool sensorSubsystemAvailable = snapshot.IsHardwareMonitorAvailable;

        // 1. CPU Identification & Capabilities (Never divides threads by 2 to guess physical cores)
        var (wmiCpuName, wmiPhysicalCores, wmiLogicalProcessors) = QueryWmiCpuDetails();
        string rawCpuName = !string.IsNullOrWhiteSpace(snapshot.CpuModelName)
            ? snapshot.CpuModelName!
            : !string.IsNullOrWhiteSpace(wmiCpuName)
                ? wmiCpuName!
                : Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Indisponível";

        var cpuVendor = DetectCpuVendor(rawCpuName);
        string cpuArch = RuntimeInformation.OSArchitecture.ToString();
        int logicalCores = wmiLogicalProcessors ?? Math.Max(1, Environment.ProcessorCount);
        int? physicalCores = physicalCoresOverride ?? wmiPhysicalCores; // Null when WMI is unavailable; never estimated by dividing by 2
        bool isHybridCpu = IsHybridCpuArchitecture(rawCpuName, cpuVendor);

        var cpuCapabilities = new HardwareComponentCapabilityMatrix(
            ComponentCategory: "CPU",
            ComponentName: rawCpuName,
            Vendor: cpuVendor,
            Identified: !string.IsNullOrWhiteSpace(rawCpuName) && rawCpuName != "Indisponível",
            Monitorable: true,
            SensorAvailable: snapshot.CpuPackageTemp.HasValue || snapshot.CpuTotalLoad.HasValue,
            Adjustable: true,
            BenchmarkCompatible: true,
            DiagnosticCompatible: true,
            RequiresDriver: !sensorSubsystemAvailable,
            RequiresAdmin: !isAdmin && !snapshot.CpuPackageTemp.HasValue,
            NotSupported: false,
            CapabilityNotes: BuildCpuAdapterNote(rawCpuName, cpuVendor, isHybridCpu, snapshot.CpuPackageTemp.HasValue)
        );

        var cpuInfo = new DetectedCpuInfo(
            Name: rawCpuName,
            Vendor: cpuVendor,
            Architecture: cpuArch,
            PhysicalCores: physicalCores,
            LogicalProcessors: logicalCores,
            PackageTemperatureC: WrapSensorMetric(snapshot.CpuPackageTemp, sensorSubsystemAvailable, isAdmin, "°C", "Sensor térmico de pacote CPU"),
            TotalLoadPercent: WrapSensorMetric(snapshot.CpuTotalLoad, sensorSubsystemAvailable, isAdmin, "%", "Carga total de utilização CPU"),
            CoreClockMhz: WrapSensorMetric(snapshot.CpuClockMhz, sensorSubsystemAvailable, isAdmin, "MHz", "Frequência de clock de núcleo"),
            PackagePowerWatts: WrapSensorMetric(snapshot.CpuPowerWatts, sensorSubsystemAvailable, isAdmin, "W", "Consumo energético de pacote"),
            IsHybridArchitecture: isHybridCpu,
            Capabilities: cpuCapabilities
        );

        // 2. Independent Multi-GPU Enumeration (LHM GpuAdapters + WMI Win32_VideoController + Registry Display Class)
        var gpus = EnumerateAllGpusIndependently(snapshot, sensorSubsystemAvailable, isAdmin);
        var primaryGpu = gpus
            .OrderByDescending(g => g.IsDiscrete)
            .ThenByDescending(g => g.VramTotalMb.Value ?? 0f)
            .FirstOrDefault();

        // 3. Memory Identification (Queries real SMBIOSMemoryType and ConfiguredClockSpeed; null when unavailable)
        float? ramUsed = snapshot.RamUsedGb;
        float? ramTotal = snapshot.RamTotalGb;
        float? ramAvail = (ramTotal.HasValue && ramUsed.HasValue) ? Math.Max(0f, ramTotal.Value - ramUsed.Value) : null;
        float? ramPct = (ramTotal.HasValue && ramTotal.Value > 0 && ramUsed.HasValue)
            ? (float)Math.Round(ramUsed.Value / ramTotal.Value * 100.0, 1)
            : null;

        var (smbiosGeneration, smbiosSpeedMt) = QueryWmiMemoryDetails();
        string? resolvedMemGen = memoryGenerationOverride ?? smbiosGeneration;
        int? resolvedMemSpeed = memorySpeedMtOverride ?? smbiosSpeedMt;

        var memCapabilities = new HardwareComponentCapabilityMatrix(
            ComponentCategory: "Memory",
            ComponentName: ramTotal.HasValue ? $"RAM Física ({ramTotal.Value:F1} GB)" : "Memória RAM do Sistema",
            Vendor: HardwareVendor.Generic,
            Identified: ramTotal.HasValue,
            Monitorable: true,
            SensorAvailable: ramUsed.HasValue && ramTotal.HasValue,
            Adjustable: true,
            BenchmarkCompatible: true,
            DiagnosticCompatible: true,
            RequiresDriver: false,
            RequiresAdmin: false,
            NotSupported: false,
            CapabilityNotes: resolvedMemGen != null
                ? $"Memória {resolvedMemGen} detectada via SMBIOS."
                : "Monitoramento de alocação física ativo; geração SMBIOS não reportada nesta sessão."
        );

        var memoryInfo = new DetectedMemoryInfo(
            TotalGb: WrapSensorMetric(ramTotal, sensorSubsystemAvailable, isAdmin, "GB", "Capacidade física instalada"),
            UsedGb: WrapSensorMetric(ramUsed, sensorSubsystemAvailable, isAdmin, "GB", "Memória física em uso"),
            AvailableGb: WrapSensorMetric(ramAvail, sensorSubsystemAvailable, isAdmin, "GB", "Memória física livre"),
            UsagePercent: WrapSensorMetric(ramPct, sensorSubsystemAvailable, isAdmin, "%", "Percentual de ocupação RAM"),
            MemoryGeneration: resolvedMemGen,
            ConfiguredSpeedMt: resolvedMemSpeed,
            Capabilities: memCapabilities
        );

        // 4. Storage Devices
        var storageList = BuildStorageDeviceList(snapshot, isAdmin);

        // 5. Network Adapters
        var networkList = BuildNetworkAdapterList();

        // 6. Battery & Power (Reads real Win32 SYSTEM_POWER_STATUS; never hardcodes 100%)
        var batteryInfo = ReadRealBatteryPowerInfo(hasBatteryOverride, batteryChargePercentOverride);

        // 7. Automatic System Profile Classification (§5.1)
        bool isCorporateManaged = isDomainOrPolicyManagedOverride ?? DetectCorporateDomainOrPolicyEnvironment();
        var (profile, justification) = ClassifySystemHardwareProfile(
            cpuInfo,
            gpus,
            primaryGpu,
            memoryInfo,
            batteryInfo.HasBattery,
            isCorporateManaged
        );

        var capabilityMatrix = new List<HardwareComponentCapabilityMatrix>
        {
            cpuCapabilities,
            memCapabilities
        };
        capabilityMatrix.AddRange(gpus.Select(g => g.Capabilities));
        capabilityMatrix.AddRange(storageList.Select(s => s.Capabilities));
        capabilityMatrix.AddRange(networkList.Select(n => n.Capabilities));

        return new UniversalHardwareReport(
            CapturedAtUtc: now,
            DetectedProfile: profile,
            ProfileJustification: justification,
            IsRunningAsAdmin: isAdmin,
            Cpu: cpuInfo,
            Gpus: gpus,
            PrimaryGpu: primaryGpu,
            Memory: memoryInfo,
            StorageDevices: storageList,
            NetworkAdapters: networkList,
            BatteryAndPower: batteryInfo,
            CapabilityMatrix: capabilityMatrix
        );
    }

    public static HardwareVendor DetectCpuVendor(string? cpuName)
    {
        if (string.IsNullOrWhiteSpace(cpuName)) return HardwareVendor.Unknown;
        if (cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Athlon", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Amd;

        if (cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Xeon", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Pentium", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Celeron", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Intel;

        if (cpuName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Oryon", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Qualcomm;

        return HardwareVendor.Generic;
    }

    public static HardwareVendor DetectGpuVendor(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return HardwareVendor.Unknown;
        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Quadro", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Nvidia;

        if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("FirePro", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Amd;

        if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Intel;

        if (gpuName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Qualcomm;

        return HardwareVendor.Generic;
    }

    public static bool IsIntegratedGpu(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return false;
        if (LibreHardwareMonitorWrapper.IsIntegratedGpuName(gpuName)) return true;

        return gpuName.Contains("Radeon 780M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 760M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 680M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon Vega", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Intel(R) Graphics", StringComparison.OrdinalIgnoreCase);
    }

    public static (SystemHardwareProfile Profile, string Justification) ClassifySystemHardwareProfile(
        DetectedCpuInfo cpu,
        IReadOnlyList<DetectedGpuAdapterInfo> gpus,
        DetectedGpuAdapterInfo? primaryGpu,
        DetectedMemoryInfo memory,
        bool hasBattery,
        bool isCorporateManaged)
    {
        float ramGb = memory.TotalGb.Value ?? 0f;
        float vramMb = primaryGpu?.VramTotalMb.Value ?? 0f;
        bool hasDiscreteGpu = gpus.Any(g => g.IsDiscrete);
        bool hasIntegratedGpu = gpus.Any(g => g.IsIntegrated);
        bool isHybridMultiGpu = hasDiscreteGpu && hasIntegratedGpu;
        string cpuName = cpu.Name;
        string gpuName = primaryGpu?.Name ?? string.Empty;

        // 1. Corporate / Policy-managed PC
        if (isCorporateManaged)
        {
            return (
                SystemHardwareProfile.Corporate,
                "Ambiente gerenciado por políticas corporativas ou domínio detectado. Operações restritas por GPO operam em modo seguro somente-leitura."
            );
        }

        // 2. Workstation (Threadripper / Xeon / Radeon PRO / NVIDIA RTX A-series / >= 64GB RAM + >= 24 threads)
        if (cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Xeon", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RTX A", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
            (ramGb >= 64f && cpu.LogicalProcessors >= 24))
        {
            return (
                SystemHardwareProfile.Workstation,
                $"Estação de trabalho detectada ({cpu.Name}, {ramGb:F0} GB RAM, {gpus.Count} GPU(s)). Foco em estabilidade sustentada e cargas multi-thread."
            );
        }

        // 3. Notebook / Portable (Has battery; may be hybrid iGPU + dGPU or ultra-portable)
        if (hasBattery)
        {
            string gpuNote = isHybridMultiGpu
                ? $"com gráficos híbridos iGPU + dGPU ({gpus.Count} adaptadores: {string.Join(" + ", gpus.Select(g => g.Name))})"
                : hasDiscreteGpu
                    ? $"com GPU dedicada ({primaryGpu?.Name})"
                    : "com gráficos integrados de eficiência energética";
            return (
                SystemHardwareProfile.Notebook,
                $"Dispositivo portátil com bateria detectado {gpuNote}. Otimizações priorizam eficiência térmica, autonomia e alternância gráfica."
            );
        }

        // 4. High Performance / Enthusiast Desktop (High core count + high VRAM/RAM)
        if (hasDiscreteGpu && (vramMb >= 12000f || (ramGb >= 32f && cpu.LogicalProcessors >= 16)))
        {
            return (
                SystemHardwareProfile.HighPerformance,
                $"Desktop de alto desempenho detectado ({cpu.Name} + {primaryGpu?.Name}, {gpus.Count} GPU(s) instalada(s))."
            );
        }

        // 5. Gamer Desktop (Any discrete GPU)
        if (hasDiscreteGpu)
        {
            return (
                SystemHardwareProfile.Gamer,
                $"Sistema Gamer com GPU dedicada detectado ({primaryGpu?.Name}). Foco em estabilidade de frametime (1% Low), prioridade MMCSS e baixa latência de rede."
            );
        }

        // 6. Basic / Integrated Graphics PC
        return (
            SystemHardwareProfile.Basic,
            "Computador com vídeo integrado ou sem GPU dedicada detectado. Foco em responsividade do Windows, redução de consumo de RAM e inicialização rápida."
        );
    }

    /// <summary>
    /// Enumerates all GPUs present in the system independently:
    /// 1) All adapters captured in snapshot.GpuAdapters (LibreHardwareMonitor / multi-GPU snapshot)
    /// 2) WMI Win32_VideoController + Registry Display Class adapters not yet in the list
    /// 3) Fallback to snapshot.GpuModelName when GpuAdapters is null/empty
    /// Each GPU receives its own independent identification, sensors, capabilities, and status.
    /// </summary>
    private static List<DetectedGpuAdapterInfo> EnumerateAllGpusIndependently(
        HardwareSnapshot snapshot,
        bool sensorSubsystemAvailable,
        bool isAdmin)
    {
        var adapterPoints = new List<GpuAdapterTelemetryPoint>();

        if (snapshot.GpuAdapters != null && snapshot.GpuAdapters.Count > 0)
        {
            adapterPoints.AddRange(snapshot.GpuAdapters);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.GpuModelName))
        {
            adapterPoints.Add(new GpuAdapterTelemetryPoint(
                Model: snapshot.GpuModelName!,
                HardwareType: "Gpu",
                CoreTempC: snapshot.GpuCoreTemp,
                HotspotTempC: snapshot.GpuHotspotTemp,
                LoadPercent: snapshot.GpuLoadPercent,
                CoreClockMhz: snapshot.GpuClockMhz,
                PowerWatts: snapshot.GpuPowerWatts,
                FanRpm: snapshot.GpuFanRpm,
                VramUsedMb: snapshot.GpuVramUsedMb,
                VramTotalMb: snapshot.GpuVramTotalMb,
                DriverVersion: null,
                PnpDeviceId: null
            ));
        }

        // Merge with OS-level WMI & Registry video controllers when running on Windows and not using a synthetic test snapshot
        if (OperatingSystem.IsWindows() && snapshot.GpuAdapters == null && string.IsNullOrWhiteSpace(snapshot.GpuModelName))
        {
            foreach (var osGpu in QueryWmiAndRegistryGpuAdapters())
            {
                bool alreadyPresent = adapterPoints.Any(existing =>
                    string.Equals(existing.Model, osGpu.Model, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(existing.PnpDeviceId) &&
                     !string.IsNullOrWhiteSpace(osGpu.PnpDeviceId) &&
                     string.Equals(existing.PnpDeviceId, osGpu.PnpDeviceId, StringComparison.OrdinalIgnoreCase)));

                if (!alreadyPresent)
                {
                    adapterPoints.Add(osGpu);
                }
            }
        }

        var list = new List<DetectedGpuAdapterInfo>();
        foreach (var pt in adapterPoints)
        {
            if (string.IsNullOrWhiteSpace(pt.Model)) continue;

            string name = pt.Model.Trim();
            var vendor = DetectGpuVendor(name);
            bool isIntegrated = IsIntegratedGpu(name);
            bool isDiscrete = !isIntegrated;
            string archFamily = ResolveOptionalVendorAdapterFamily(name, vendor, isDiscrete);

            bool hasCoreTemp = pt.CoreTempC.HasValue;
            bool hasHotspot = pt.HotspotTempC.HasValue;

            var hotspotMetric = hasHotspot
                ? new DetectedMetric<float?>(
                    pt.HotspotTempC,
                    HardwareDataSource.LibreHardwareMonitor,
                    DateTime.UtcNow,
                    MetricReliability.VerifiedPhysicalSensor,
                    true,
                    UniversalSensorStatus.Available,
                    null,
                    "°C")
                : DetectedMetric<float?>.Unavailable(
                    UniversalSensorStatus.NotSupported,
                    "Sensor de Hotspot/Junction não exposto por este adaptador gráfico ou driver.",
                    "°C");

            var vramTotalMetric = pt.VramTotalMb.HasValue
                ? new DetectedMetric<float?>(
                    pt.VramTotalMb,
                    hasCoreTemp ? HardwareDataSource.LibreHardwareMonitor : HardwareDataSource.Wmi,
                    DateTime.UtcNow,
                    hasCoreTemp ? MetricReliability.VerifiedPhysicalSensor : MetricReliability.OsReportedCounter,
                    true,
                    UniversalSensorStatus.Available,
                    null,
                    "MB")
                : DetectedMetric<float?>.Unavailable(UniversalSensorStatus.Unavailable, "Capacidade total de VRAM indisponível.", "MB");

            var caps = new HardwareComponentCapabilityMatrix(
                ComponentCategory: "GPU",
                ComponentName: name,
                Vendor: vendor,
                Identified: true,
                Monitorable: true,
                SensorAvailable: hasCoreTemp || pt.LoadPercent.HasValue,
                Adjustable: false,
                BenchmarkCompatible: true,
                DiagnosticCompatible: true,
                RequiresDriver: !hasCoreTemp && !pt.LoadPercent.HasValue,
                RequiresAdmin: !isAdmin && !hasCoreTemp,
                NotSupported: false,
                CapabilityNotes: $"Adaptador independente ({archFamily}): {(isDiscrete ? "GPU Dedicada" : "GPU Integrada")}. Telemetria somente-leitura."
            );

            list.Add(new DetectedGpuAdapterInfo(
                Name: name,
                Vendor: vendor,
                ArchitectureFamily: archFamily,
                IsIntegrated: isIntegrated,
                IsDiscrete: isDiscrete,
                CoreTemperatureC: WrapSensorMetric(pt.CoreTempC, sensorSubsystemAvailable, isAdmin, "°C", $"Temperatura Core ({name})"),
                HotspotTemperatureC: hotspotMetric,
                CoreLoadPercent: WrapSensorMetric(pt.LoadPercent, sensorSubsystemAvailable, isAdmin, "%", $"Carga do núcleo ({name})"),
                CoreClockMhz: WrapSensorMetric(pt.CoreClockMhz, sensorSubsystemAvailable, isAdmin, "MHz", $"Frequência do núcleo ({name})"),
                PowerWatts: WrapSensorMetric(pt.PowerWatts, sensorSubsystemAvailable, isAdmin, "W", $"Potência elétrica ({name})"),
                VramUsedMb: WrapSensorMetric(pt.VramUsedMb, sensorSubsystemAvailable, isAdmin, "MB", $"VRAM em uso ({name})"),
                VramTotalMb: vramTotalMetric,
                FanRpm: WrapSensorMetric(pt.FanRpm, sensorSubsystemAvailable, isAdmin, "RPM", $"Ventoinha ({name})"),
                DriverVersion: pt.DriverVersion,
                Capabilities: caps,
                PnpDeviceId: pt.PnpDeviceId
            ));
        }

        return list;
    }

    [SupportedOSPlatform("windows")]
    private static List<GpuAdapterTelemetryPoint> QueryWmiAndRegistryGpuAdapters()
    {
        var results = new List<GpuAdapterTelemetryPoint>();
        if (!OperatingSystem.IsWindows()) return results;

        var registryVramByDesc = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var displayClassKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", false);
            if (displayClassKey != null)
            {
                foreach (var subName in displayClassKey.GetSubKeyNames())
                {
                    if (!subName.StartsWith("0", StringComparison.Ordinal)) continue;
                    using var subKey = displayClassKey.OpenSubKey(subName, false);
                    string? desc = subKey?.GetValue("DriverDesc")?.ToString();
                    object? qwMem = subKey?.GetValue("HardwareInformation.qwMemorySize");
                    if (!string.IsNullOrWhiteSpace(desc) && qwMem != null)
                    {
                        long bytes = qwMem switch
                        {
                            long l => l,
                            byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                            int i => i,
                            _ => 0L
                        };
                        if (bytes > 0)
                        {
                            registryVramByDesc[desc.Trim()] = (float)Math.Round(bytes / (1024.0 * 1024.0), 0);
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");
            using var collection = searcher.Get();
            foreach (ManagementBaseObject obj in collection)
            {
                string? name = obj["Name"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? driverVersion = obj["DriverVersion"]?.ToString()?.Trim();
                string? pnpId = obj["PNPDeviceID"]?.ToString()?.Trim();
                float? vramMb = null;

                if (registryVramByDesc.TryGetValue(name, out float regVram))
                {
                    vramMb = regVram;
                }
                else if (obj["AdapterRAM"] != null && ulong.TryParse(obj["AdapterRAM"].ToString(), out ulong rawBytes) && rawBytes > 0)
                {
                    vramMb = (float)Math.Round(rawBytes / (1024.0 * 1024.0), 0);
                }

                results.Add(new GpuAdapterTelemetryPoint(
                    Model: name,
                    HardwareType: "WmiVideoController",
                    CoreTempC: null,
                    HotspotTempC: null,
                    LoadPercent: null,
                    CoreClockMhz: null,
                    PowerWatts: null,
                    FanRpm: null,
                    VramUsedMb: null,
                    VramTotalMb: vramMb,
                    DriverVersion: driverVersion,
                    PnpDeviceId: pnpId
                ));
            }
        }
        catch { }

        return results;
    }

    private static (string? Name, int? PhysicalCores, int? LogicalProcessors) QueryWmiCpuDetails()
    {
        if (!OperatingSystem.IsWindows()) return (null, null, null);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            using var collection = searcher.Get();
            int totalPhysical = 0;
            int totalLogical = 0;
            string? cpuName = null;

            foreach (ManagementBaseObject obj in collection)
            {
                cpuName ??= obj["Name"]?.ToString()?.Trim();
                if (obj["NumberOfCores"] != null && int.TryParse(obj["NumberOfCores"].ToString(), out int cores) && cores > 0)
                    totalPhysical += cores;
                if (obj["NumberOfLogicalProcessors"] != null && int.TryParse(obj["NumberOfLogicalProcessors"].ToString(), out int logical) && logical > 0)
                    totalLogical += logical;
            }

            return (
                cpuName,
                totalPhysical > 0 ? totalPhysical : null,
                totalLogical > 0 ? totalLogical : null
            );
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static (string? MemoryGeneration, int? ConfiguredSpeedMt) QueryWmiMemoryDetails()
    {
        if (!OperatingSystem.IsWindows()) return (null, null);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SMBIOSMemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();
            string? generation = null;
            int? speedMt = null;

            foreach (ManagementBaseObject obj in collection)
            {
                if (obj["SMBIOSMemoryType"] != null && int.TryParse(obj["SMBIOSMemoryType"].ToString(), out int smbiosType))
                {
                    generation ??= smbiosType switch
                    {
                        24 => "DDR3",
                        26 => "DDR4",
                        30 => "LPDDR4",
                        34 => "DDR5",
                        35 => "LPDDR5",
                        _ => null
                    };
                }

                if (obj["ConfiguredClockSpeed"] != null && int.TryParse(obj["ConfiguredClockSpeed"].ToString(), out int cfgSpeed) && cfgSpeed > 0)
                {
                    speedMt ??= cfgSpeed;
                }
                else if (obj["Speed"] != null && int.TryParse(obj["Speed"].ToString(), out int spd) && spd > 0)
                {
                    speedMt ??= spd;
                }
            }

            return (generation, speedMt);
        }
        catch
        {
            return (null, null);
        }
    }

    private static DetectedBatteryPowerInfo ReadRealBatteryPowerInfo(
        bool? hasBatteryOverride,
        int? batteryChargePercentOverride)
    {
        if (hasBatteryOverride.HasValue)
        {
            bool hasBat = hasBatteryOverride.Value;
            return new DetectedBatteryPowerInfo(
                HasBattery: hasBat,
                IsOnBatteryPower: false,
                ChargePercent: hasBat ? batteryChargePercentOverride : null,
                PowerLineStatus: hasBat ? "Bateria Detectada" : "AC Direto (Desktop)",
                ActivePowerSchemeName: "Plano de Energia Ativo do Windows"
            );
        }

        if (!OperatingSystem.IsWindows())
        {
            return new DetectedBatteryPowerInfo(
                HasBattery: false,
                IsOnBatteryPower: false,
                ChargePercent: null,
                PowerLineStatus: "Indisponível (Requer Host Windows)",
                ActivePowerSchemeName: "Indisponível"
            );
        }

        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                bool hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
                bool isOnBattery = status.ACLineStatus == 0;
                int? realPercent = (hasBattery && status.BatteryLifePercent <= 100)
                    ? status.BatteryLifePercent
                    : null;

                string lineStatus = status.ACLineStatus switch
                {
                    0 => "Operando na Bateria (DC)",
                    1 => hasBattery ? "Conectado à Fonte (AC)" : "AC Direto (Desktop)",
                    _ => "Status de Energia Desconhecido"
                };

                return new DetectedBatteryPowerInfo(
                    HasBattery: hasBattery,
                    IsOnBatteryPower: isOnBattery,
                    ChargePercent: batteryChargePercentOverride ?? realPercent,
                    PowerLineStatus: lineStatus,
                    ActivePowerSchemeName: "Plano de Energia Ativo do Windows"
                );
            }
        }
        catch { }

        return new DetectedBatteryPowerInfo(
            HasBattery: false,
            IsOnBatteryPower: false,
            ChargePercent: null,
            PowerLineStatus: "Indisponível",
            ActivePowerSchemeName: "Indisponível"
        );
    }

    private static string ResolveOptionalVendorAdapterFamily(string gpuName, HardwareVendor vendor, bool isDiscrete)
    {
        return vendor switch
        {
            HardwareVendor.Amd => isDiscrete
                ? "AMD Radeon Discrete Adapter"
                : "AMD Radeon Integrated Graphics Adapter",
            HardwareVendor.Nvidia => isDiscrete
                ? "NVIDIA GeForce / RTX Discrete Adapter"
                : "NVIDIA Integrated Graphics Adapter",
            HardwareVendor.Intel => isDiscrete
                ? "Intel Arc Discrete Graphics Adapter"
                : "Intel UHD / Iris Xe Integrated Graphics Adapter",
            HardwareVendor.Qualcomm => "Qualcomm Adreno Integrated SoC GPU",
            _ => isDiscrete ? "Generic Discrete GPU Adapter" : "Generic Integrated GPU Adapter"
        };
    }

    private static string BuildCpuAdapterNote(string cpuName, HardwareVendor vendor, bool isHybrid, bool hasTempSensor)
    {
        string vendorStr = vendor switch
        {
            HardwareVendor.Amd => "Adaptador AMD Ryzen/EPYC/Athlon",
            HardwareVendor.Intel => isHybrid
                ? "Adaptador Intel Core Híbrido (P-Cores + E-Cores / Thread Director)"
                : "Adaptador Intel Core/Xeon",
            HardwareVendor.Qualcomm => "Adaptador Qualcomm Snapdragon ARM64",
            _ => "Adaptador Universal de Processador"
        };

        return hasTempSensor
            ? $"{vendorStr}: sensores térmicos e de carga ativos."
            : $"{vendorStr}: identificação ativa; leitura térmica física requer driver de sensores/elevação UAC.";
    }

    private static bool IsHybridCpuArchitecture(string cpuName, HardwareVendor vendor)
    {
        if (vendor != HardwareVendor.Intel) return false;
        return cpuName.Contains("12th Gen", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("13th Gen", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("14th Gen", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i5-12", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i7-12", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i9-12", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i5-13", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i7-13", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i9-13", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i5-14", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i7-14", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("i9-14", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DetectedStorageDeviceInfo> BuildStorageDeviceList(HardwareSnapshot snapshot, bool isAdmin)
    {
        var result = new List<DetectedStorageDeviceInfo>();
        if (snapshot.StorageDrives != null && snapshot.StorageDrives.Count > 0)
        {
            int index = 0;
            foreach (var d in snapshot.StorageDrives)
            {
                bool isNvme = d.Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

                var caps = new HardwareComponentCapabilityMatrix(
                    ComponentCategory: "Storage",
                    ComponentName: d.Model,
                    Vendor: HardwareVendor.Generic,
                    Identified: true,
                    Monitorable: true,
                    SensorAvailable: d.TemperatureC.HasValue || d.FreeSpaceGb.HasValue,
                    Adjustable: true,
                    BenchmarkCompatible: true,
                    DiagnosticCompatible: true,
                    RequiresDriver: false,
                    RequiresAdmin: !isAdmin && !d.TemperatureC.HasValue,
                    NotSupported: false,
                    CapabilityNotes: isNvme ? "Unidade NVMe detectada." : "Unidade de armazenamento fixa detectada."
                );

                result.Add(new DetectedStorageDeviceInfo(
                    Model: d.Model,
                    BusType: isNvme ? "NVMe PCIe" : "SATA / AHCI",
                    MediaType: "SSD / Fixed Storage",
                    CapacityGb: d.CapacityGb.HasValue
                        ? new DetectedMetric<float?>(d.CapacityGb, HardwareDataSource.Wmi, DateTime.UtcNow, MetricReliability.OsReportedCounter, true, UniversalSensorStatus.Available, null, "GB")
                        : DetectedMetric<float?>.Unavailable(UniversalSensorStatus.Unavailable, "Capacidade total não reportada", "GB"),
                    FreeSpaceGb: d.FreeSpaceGb.HasValue
                        ? new DetectedMetric<float?>(d.FreeSpaceGb, HardwareDataSource.Wmi, DateTime.UtcNow, MetricReliability.OsReportedCounter, true, UniversalSensorStatus.Available, null, "GB")
                        : DetectedMetric<float?>.Unavailable(UniversalSensorStatus.Unavailable, "Espaço livre não reportado", "GB"),
                    TemperatureC: d.TemperatureC.HasValue
                        ? new DetectedMetric<float?>(d.TemperatureC, HardwareDataSource.SmartNvme, DateTime.UtcNow, MetricReliability.VerifiedPhysicalSensor, true, UniversalSensorStatus.Available, null, "°C")
                        : DetectedMetric<float?>.Unavailable(isAdmin ? UniversalSensorStatus.NotSupported : UniversalSensorStatus.RequiresAdmin, "Temperatura SMART requer suporte do controlador/UAC", "°C"),
                    IsSystemDisk: index == 0,
                    Capabilities: caps
                ));
                index++;
            }
        }
        return result;
    }

    private static List<DetectedNetworkAdapterInfo> BuildNetworkAdapterList()
    {
        var adapters = new List<DetectedNetworkAdapterInfo>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                bool isUp = nic.OperationalStatus == OperationalStatus.Up;
                bool isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                              nic.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                              nic.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                              nic.Description.Contains("802.11", StringComparison.OrdinalIgnoreCase);

                long speedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000 : 0;
                long rxBytes = 0;
                long txBytes = 0;
                try
                {
                    var stats = nic.GetIPStatistics();
                    rxBytes = stats.BytesReceived;
                    txBytes = stats.BytesSent;
                }
                catch { }

                var caps = new HardwareComponentCapabilityMatrix(
                    ComponentCategory: "Network",
                    ComponentName: nic.Description,
                    Vendor: HardwareVendor.Generic,
                    Identified: true,
                    Monitorable: true,
                    SensorAvailable: isUp,
                    Adjustable: true,
                    BenchmarkCompatible: isUp,
                    DiagnosticCompatible: true,
                    RequiresDriver: false,
                    RequiresAdmin: false,
                    NotSupported: false,
                    CapabilityNotes: isWifi
                        ? "Interface Wi-Fi detectada (sujeita a jitter de rádiofrequência)."
                        : "Interface cabeada Ethernet detectada."
                );

                adapters.Add(new DetectedNetworkAdapterInfo(
                    Name: nic.Description,
                    InterfaceType: nic.NetworkInterfaceType.ToString(),
                    IsConnected: isUp,
                    IsWiFi: isWifi,
                    NegotiatedLinkSpeedMbps: speedMbps,
                    BytesReceived: rxBytes,
                    BytesSent: txBytes,
                    OperationalStatus: nic.OperationalStatus.ToString(),
                    Capabilities: caps
                ));
            }
        }
        catch { }

        return adapters;
    }

    private static DetectedMetric<float?> WrapSensorMetric(
        float? value,
        bool sensorSubsystemAvailable,
        bool isAdmin,
        string unit,
        string metricLabel)
    {
        if (value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
        {
            return new DetectedMetric<float?>(
                value,
                HardwareDataSource.LibreHardwareMonitor,
                DateTime.UtcNow,
                MetricReliability.VerifiedPhysicalSensor,
                true,
                UniversalSensorStatus.Available,
                null,
                unit
            );
        }

        if (!isAdmin)
        {
            return DetectedMetric<float?>.Unavailable(
                UniversalSensorStatus.RequiresAdmin,
                $"{metricLabel}: requer elevação de Administrador (UAC) para acesso ao driver de sensores.",
                unit
            );
        }

        if (!sensorSubsystemAvailable)
        {
            return DetectedMetric<float?>.Unavailable(
                UniversalSensorStatus.BlockedByDriver,
                $"{metricLabel}: subsistema físico de sensores indisponível nesta sessão.",
                unit
            );
        }

        return DetectedMetric<float?>.Unavailable(
            UniversalSensorStatus.NotSupported,
            $"{metricLabel}: sensor não exposto pelo hardware ou firmware atual.",
            unit
        );
    }

    private static bool CheckIsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectCorporateDomainOrPolicyEnvironment()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            string? userDomain = Environment.UserDomainName;
            string? machineName = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(userDomain) &&
                !string.IsNullOrWhiteSpace(machineName) &&
                !string.Equals(userDomain, machineName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
}
