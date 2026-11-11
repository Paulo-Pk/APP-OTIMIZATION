using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Hardware;

/// <summary>
/// Universal Hardware Engine (§3 & §5):
/// - Enumerates adapters from LibreHardwareMonitor AND independently queries Windows WMI/Registry video adapters.
/// - Merges results by normalized PNPDeviceID (PCI VEN/DEV/SUBSYS) and canonical model name, avoiding duplicates
///   while preserving valid LHM sensors and never overwriting valid readings with null.
/// - Never presumes an integrated GPU is primary when a discrete GPU is installed.
/// - Never invents or estimates values for CPU physical cores, RAM generation, or battery percentage.
/// </summary>
public class UniversalHardwareEngine
{
    private static readonly Regex PciVenDevRegex = new(
        @"VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}(?:&SUBSYS_[0-9A-F]{8})?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

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
        int? memorySpeedMtOverride = null,
        IReadOnlyList<GpuAdapterTelemetryPoint>? complementaryWmiGpuAdaptersOverride = null)
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
        int? physicalCores = physicalCoresOverride ?? wmiPhysicalCores;
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

        // 2. Independent Multi-GPU Enumeration & Merging (Always combines LHM + WMI/Registry adapters)
        var gpus = EnumerateAllGpusIndependently(
            snapshot,
            sensorSubsystemAvailable,
            isAdmin,
            complementaryWmiGpuAdaptersOverride
        );

        // Never presume an integrated GPU is primary when a discrete GPU is present
        var primaryGpu = SelectPrimaryGpuAdapter(gpus);

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

    public static DetectedGpuAdapterInfo? SelectPrimaryGpuAdapter(IReadOnlyList<DetectedGpuAdapterInfo> gpus)
    {
        if (gpus == null || gpus.Count == 0) return null;

        // Always prioritize discrete GPUs over integrated GPUs, then by available sensors, then by VRAM capacity
        return gpus
            .OrderByDescending(g => g.IsDiscrete)
            .ThenByDescending(g => g.Capabilities.SensorAvailable)
            .ThenByDescending(g => g.VramTotalMb.Value ?? 0f)
            .FirstOrDefault();
    }

    /// <summary>
    /// Merges GPU adapters enumerated by LibreHardwareMonitor with GPU adapters enumerated via WMI/Registry.
    /// - Deduplicates by normalized PCI PNPDeviceID (VEN_xxxx&DEV_xxxx) or canonical model name.
    /// - Preserves all valid sensor values from LHM without overwriting them with null values from WMI.
    /// - Adds any GPU detected by WMI that LHM missed (e.g., sleeping dGPU in hybrid laptop or secondary adapter).
    /// </summary>
    public static IReadOnlyList<GpuAdapterTelemetryPoint> MergeGpuAdapters(
        IReadOnlyList<GpuAdapterTelemetryPoint>? lhmAdapters,
        IReadOnlyList<GpuAdapterTelemetryPoint>? wmiAdapters)
    {
        var merged = new List<GpuAdapterTelemetryPoint>();

        if (lhmAdapters != null)
        {
            foreach (var lhm in lhmAdapters)
            {
                if (!string.IsNullOrWhiteSpace(lhm.Model))
                {
                    merged.Add(lhm);
                }
            }
        }

        if (wmiAdapters == null || wmiAdapters.Count == 0)
        {
            return merged;
        }

        var matchedLhmIndices = new HashSet<int>();

        foreach (var wmi in wmiAdapters)
        {
            if (string.IsNullOrWhiteSpace(wmi.Model)) continue;

            int matchIndex = FindMatchingAdapterIndex(merged, matchedLhmIndices, wmi);
            if (matchIndex >= 0)
            {
                matchedLhmIndices.Add(matchIndex);
                var existing = merged[matchIndex];

                // Merge without ever overwriting valid LHM sensor readings with null from WMI
                merged[matchIndex] = new GpuAdapterTelemetryPoint(
                    Model: !string.IsNullOrWhiteSpace(existing.Model) ? existing.Model : wmi.Model,
                    HardwareType: !string.IsNullOrWhiteSpace(existing.HardwareType) ? existing.HardwareType : wmi.HardwareType,
                    CoreTempC: existing.CoreTempC ?? wmi.CoreTempC,
                    HotspotTempC: existing.HotspotTempC ?? wmi.HotspotTempC,
                    LoadPercent: existing.LoadPercent ?? wmi.LoadPercent,
                    CoreClockMhz: existing.CoreClockMhz ?? wmi.CoreClockMhz,
                    PowerWatts: existing.PowerWatts ?? wmi.PowerWatts,
                    FanRpm: existing.FanRpm ?? wmi.FanRpm,
                    VramUsedMb: existing.VramUsedMb ?? wmi.VramUsedMb,
                    VramTotalMb: existing.VramTotalMb ?? wmi.VramTotalMb,
                    DriverVersion: !string.IsNullOrWhiteSpace(existing.DriverVersion) ? existing.DriverVersion : wmi.DriverVersion,
                    PnpDeviceId: PreferPnpDeviceId(existing.PnpDeviceId, wmi.PnpDeviceId)
                );
            }
            else
            {
                merged.Add(wmi);
            }
        }

        return merged;
    }

    private static int FindMatchingAdapterIndex(
        List<GpuAdapterTelemetryPoint> existingList,
        HashSet<int> alreadyMatchedIndices,
        GpuAdapterTelemetryPoint candidate)
    {
        string? candidatePciKey = ExtractNormalizedPciKey(candidate.PnpDeviceId);

        // Pass 1: Match by exact or normalized PCI VEN_xxxx&DEV_xxxx key
        if (!string.IsNullOrEmpty(candidatePciKey))
        {
            for (int i = 0; i < existingList.Count; i++)
            {
                if (alreadyMatchedIndices.Contains(i)) continue;
                string? existingPciKey = ExtractNormalizedPciKey(existingList[i].PnpDeviceId);
                if (!string.IsNullOrEmpty(existingPciKey) &&
                    string.Equals(existingPciKey, candidatePciKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        // Pass 2: Match by full PnpDeviceId if neither had a standard PCI VEN/DEV substring
        if (!string.IsNullOrWhiteSpace(candidate.PnpDeviceId))
        {
            for (int i = 0; i < existingList.Count; i++)
            {
                if (alreadyMatchedIndices.Contains(i)) continue;
                if (!string.IsNullOrWhiteSpace(existingList[i].PnpDeviceId) &&
                    string.Equals(existingList[i].PnpDeviceId!.Trim(), candidate.PnpDeviceId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        // Pass 3: Match by canonical GPU model name (only if the existing item did not have a conflicting PCI key)
        string candidateCanonical = NormalizeGpuModelName(candidate.Model);
        for (int i = 0; i < existingList.Count; i++)
        {
            if (alreadyMatchedIndices.Contains(i)) continue;
            string? existingPciKey = ExtractNormalizedPciKey(existingList[i].PnpDeviceId);
            if (!string.IsNullOrEmpty(existingPciKey) && !string.IsNullOrEmpty(candidatePciKey) &&
                !string.Equals(existingPciKey, candidatePciKey, StringComparison.OrdinalIgnoreCase))
            {
                // Different physical PCI devices (e.g., two identical GPUs in a multi-GPU workstation with different instance IDs)
                continue;
            }

            if (!string.IsNullOrWhiteSpace(existingList[i].PnpDeviceId) &&
                !string.IsNullOrWhiteSpace(candidate.PnpDeviceId) &&
                existingList[i].PnpDeviceId!.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) &&
                candidate.PnpDeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(existingList[i].PnpDeviceId!.Trim(), candidate.PnpDeviceId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Distinct full PCI instance paths (e.g., two identical cards in different PCIe slots) -> do not merge!
                continue;
            }

            string existingCanonical = NormalizeGpuModelName(existingList[i].Model);
            if (string.Equals(existingCanonical, candidateCanonical, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static string? ExtractNormalizedPciKey(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        var match = PciVenDevRegex.Match(pnpDeviceId);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static string NormalizeGpuModelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return name
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Series", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string? PreferPnpDeviceId(string? existingId, string? wmiId)
    {
        if (!string.IsNullOrWhiteSpace(wmiId) && wmiId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
            return wmiId;
        return !string.IsNullOrWhiteSpace(existingId) ? existingId : wmiId;
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
               gpuName.Contains("Radeon 740M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 680M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 660M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 890M", StringComparison.OrdinalIgnoreCase) ||
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

        if (isCorporateManaged)
        {
            return (
                SystemHardwareProfile.Corporate,
                "Ambiente gerenciado por políticas corporativas ou domínio detectado. Operações restritas por GPO operam em modo seguro somente-leitura."
            );
        }

        if (cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Xeon", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RTX A", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
            gpus.Count(g => g.IsDiscrete) >= 2 ||
            (ramGb >= 64f && cpu.LogicalProcessors >= 24))
        {
            return (
                SystemHardwareProfile.Workstation,
                $"Estação de trabalho detectada ({cpu.Name}, {ramGb:F0} GB RAM, {gpus.Count} GPU(s)). Foco em estabilidade sustentada e cargas multi-thread."
            );
        }

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

        if (hasDiscreteGpu && (vramMb >= 12000f || (ramGb >= 32f && cpu.LogicalProcessors >= 16)))
        {
            return (
                SystemHardwareProfile.HighPerformance,
                $"Desktop de alto desempenho detectado ({cpu.Name} + {primaryGpu?.Name}, {gpus.Count} GPU(s) instalada(s))."
            );
        }

        if (hasDiscreteGpu)
        {
            return (
                SystemHardwareProfile.Gamer,
                $"Sistema Gamer com GPU dedicada detectado ({primaryGpu?.Name}). Foco em estabilidade de frametime (1% Low), prioridade MMCSS e baixa latência de rede."
            );
        }

        return (
            SystemHardwareProfile.Basic,
            "Computador com vídeo integrado ou sem GPU dedicada detectado. Foco em responsividade do Windows, redução de consumo de RAM e inicialização rápida."
        );
    }

    private static List<DetectedGpuAdapterInfo> EnumerateAllGpusIndependently(
        HardwareSnapshot snapshot,
        bool sensorSubsystemAvailable,
        bool isAdmin,
        IReadOnlyList<GpuAdapterTelemetryPoint>? complementaryWmiGpuAdaptersOverride)
    {
        var lhmPoints = new List<GpuAdapterTelemetryPoint>();

        if (snapshot.GpuAdapters != null && snapshot.GpuAdapters.Count > 0)
        {
            lhmPoints.AddRange(snapshot.GpuAdapters);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.GpuModelName))
        {
            lhmPoints.Add(new GpuAdapterTelemetryPoint(
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

        // Always query complementary Windows WMI + Registry adapters (or use provided complementary list in tests)
        // so that GPUs not exposed by LibreHardwareMonitor are still enumerated and merged by PNPDeviceID!
        IReadOnlyList<GpuAdapterTelemetryPoint> wmiPoints;
        if (complementaryWmiGpuAdaptersOverride != null)
        {
            wmiPoints = complementaryWmiGpuAdaptersOverride;
        }
        else if (OperatingSystem.IsWindows())
        {
            wmiPoints = QueryWmiAndRegistryGpuAdapters();
        }
        else
        {
            wmiPoints = Array.Empty<GpuAdapterTelemetryPoint>();
        }

        var mergedPoints = MergeGpuAdapters(lhmPoints, wmiPoints);
        var list = new List<DetectedGpuAdapterInfo>();

        foreach (var pt in mergedPoints)
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
                CapabilityNotes: $"Adaptador independente ({archFamily}): {(isDiscrete ? "GPU Dedicada" : "GPU Integrada")}. " +
                                 (hasCoreTemp ? "Sensores físicos ativos." : "Componente identificado via WMI; sensores físicos indisponíveis ou adaptador em economia de energia.")
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

        // Order so discrete GPUs come before integrated GPUs
        return list
            .OrderByDescending(g => g.IsDiscrete)
            .ThenByDescending(g => g.Capabilities.SensorAvailable)
            .ThenByDescending(g => g.VramTotalMb.Value ?? 0f)
            .ToList();
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
            : $"{vendorStr}: componente identificado; sensores térmicos físicos requerem driver/elevação UAC.";
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
