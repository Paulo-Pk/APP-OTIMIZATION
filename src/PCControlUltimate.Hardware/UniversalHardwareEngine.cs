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
/// Production Windows hardware probe querying real WMI, Registry Display Class, Win32 Power Status, and Network Interfaces.
/// </summary>
public sealed class WindowsWmiHardwareProbe : IHardwareSystemProbe
{
    public bool IsIsolatedSimulation => false;

    public bool IsRunningAsAdministrator() => UniversalHardwareEngine.CheckIsRunningAsAdministrator();

    public bool IsCorporateDomainOrPolicyManaged() => UniversalHardwareEngine.DetectCorporateDomainOrPolicyEnvironment();

    public (string? Name, int? PhysicalCores, int? LogicalProcessors) QueryCpuDetails() =>
        UniversalHardwareEngine.QueryWmiCpuDetails();

    public IReadOnlyList<GpuAdapterTelemetryPoint> QueryGpuAdapters() =>
        OperatingSystem.IsWindows()
            ? UniversalHardwareEngine.QueryWmiAndRegistryGpuAdapters()
            : Array.Empty<GpuAdapterTelemetryPoint>();

    public (string? MemoryGeneration, int? ConfiguredSpeedMt) QueryMemoryDetails() =>
        UniversalHardwareEngine.QueryWmiMemoryDetails();

    public DetectedBatteryPowerInfo QueryBatteryPowerInfo(bool? hasBatteryOverride, int? batteryChargePercentOverride) =>
        UniversalHardwareEngine.ReadRealBatteryPowerInfo(hasBatteryOverride, batteryChargePercentOverride);

    public IReadOnlyList<DetectedNetworkAdapterInfo> QueryNetworkAdapters() =>
        UniversalHardwareEngine.BuildNetworkAdapterList();
}

/// <summary>
/// Deterministic, isolated hardware probe for unit and regression tests.
/// Never queries host WMI, Registry, Win32 Power Status, or host environment variables.
/// </summary>
public sealed class SimulatedHardwareSystemProbe : IHardwareSystemProbe
{
    private readonly string? _cpuName;
    private readonly int? _physicalCores;
    private readonly int? _logicalProcessors;
    private readonly IReadOnlyList<GpuAdapterTelemetryPoint> _wmiGpuAdapters;
    private readonly string? _memoryGeneration;
    private readonly int? _memorySpeedMt;
    private readonly bool _hasBattery;
    private readonly int? _batteryChargePercent;
    private readonly bool _isAdmin;
    private readonly bool _isCorporateManaged;
    private readonly IReadOnlyList<DetectedNetworkAdapterInfo> _networkAdapters;

    public SimulatedHardwareSystemProbe(
        string? cpuName = null,
        int? physicalCores = null,
        int? logicalProcessors = null,
        IReadOnlyList<GpuAdapterTelemetryPoint>? wmiGpuAdapters = null,
        string? memoryGeneration = null,
        int? memorySpeedMt = null,
        bool hasBattery = false,
        int? batteryChargePercent = null,
        bool isAdmin = true,
        bool isCorporateManaged = false,
        IReadOnlyList<DetectedNetworkAdapterInfo>? networkAdapters = null)
    {
        _cpuName = cpuName;
        _physicalCores = physicalCores;
        _logicalProcessors = logicalProcessors;
        _wmiGpuAdapters = wmiGpuAdapters ?? Array.Empty<GpuAdapterTelemetryPoint>();
        _memoryGeneration = memoryGeneration;
        _memorySpeedMt = memorySpeedMt;
        _hasBattery = hasBattery;
        _batteryChargePercent = batteryChargePercent;
        _isAdmin = isAdmin;
        _isCorporateManaged = isCorporateManaged;
        _networkAdapters = networkAdapters ?? Array.Empty<DetectedNetworkAdapterInfo>();
    }

    public bool IsIsolatedSimulation => true;

    public bool IsRunningAsAdministrator() => _isAdmin;

    public bool IsCorporateDomainOrPolicyManaged() => _isCorporateManaged;

    public (string? Name, int? PhysicalCores, int? LogicalProcessors) QueryCpuDetails() =>
        (_cpuName, _physicalCores, _logicalProcessors);

    public IReadOnlyList<GpuAdapterTelemetryPoint> QueryGpuAdapters() => _wmiGpuAdapters;

    public (string? MemoryGeneration, int? ConfiguredSpeedMt) QueryMemoryDetails() =>
        (_memoryGeneration, _memorySpeedMt);

    public DetectedBatteryPowerInfo QueryBatteryPowerInfo(bool? hasBatteryOverride, int? batteryChargePercentOverride)
    {
        bool hasBat = hasBatteryOverride ?? _hasBattery;
        int? pct = batteryChargePercentOverride ?? _batteryChargePercent;
        return new DetectedBatteryPowerInfo(
            HasBattery: hasBat,
            IsOnBatteryPower: false,
            ChargePercent: hasBat ? pct : null,
            PowerLineStatus: hasBat ? "Bateria Detectada" : "AC Direto (Desktop)",
            ActivePowerSchemeName: "Plano de Energia Ativo do Windows"
        );
    }

    public IReadOnlyList<DetectedNetworkAdapterInfo> QueryNetworkAdapters() => _networkAdapters;
}

/// <summary>
/// Universal Hardware Engine (§3 & §5):
/// - Injectable IHardwareSystemProbe architecture: uses WindowsWmiHardwareProbe in production and isolated simulation when
///   a simulated snapshot or SimulatedHardwareSystemProbe is provided, preventing host WMI contamination in CI tests.
/// - Explicitly classifies adapters into PhysicalDiscrete, PhysicalIntegrated, VirtualAdapter, RemoteDisplayAdapter,
///   BasicDisplayAdapter, and UnknownAdapter.
/// - Never counts virtual/remote/basic display adapters (e.g., Microsoft Hyper-V Video) as physical GPUs.
/// - Merges physical adapters by normalized PNPDeviceID / PCI instance path without overwriting valid LHM sensors with null.
/// - Never invents or estimates values for CPU physical cores, RAM generation, or battery percentage.
/// </summary>
public class UniversalHardwareEngine
{
    private static readonly Regex PciVenDevRegex = new(
        @"VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}(?:&SUBSYS_[0-9A-F]{8})?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private readonly IHardwareMonitorService _monitorService;
    private readonly IHardwareSystemProbe _systemProbe;
    private readonly bool _hasExplicitCustomProbe;

    public UniversalHardwareEngine(
        IHardwareMonitorService monitorService,
        IHardwareSystemProbe? systemProbe = null)
    {
        _monitorService = monitorService;
        _hasExplicitCustomProbe = systemProbe != null;
        _systemProbe = systemProbe ?? new WindowsWmiHardwareProbe();
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
        IReadOnlyList<GpuAdapterTelemetryPoint>? complementaryWmiGpuAdaptersOverride = null,
        int? logicalProcessorsOverride = null)
    {
        // When an existingSnapshot is explicitly passed (or an isolated probe was injected), operate in isolated mode
        // so unit tests never silently query the host runner's real WMI (e.g., Microsoft Hyper-V Video on GitHub Actions).
        bool useIsolatedMode = _systemProbe.IsIsolatedSimulation ||
                               (existingSnapshot != null && !_hasExplicitCustomProbe);

        IHardwareSystemProbe activeProbe = useIsolatedMode && !_systemProbe.IsIsolatedSimulation
            ? new SimulatedHardwareSystemProbe(
                cpuName: existingSnapshot?.CpuModelName,
                physicalCores: physicalCoresOverride,
                logicalProcessors: logicalProcessorsOverride,
                wmiGpuAdapters: complementaryWmiGpuAdaptersOverride ?? Array.Empty<GpuAdapterTelemetryPoint>(),
                memoryGeneration: memoryGenerationOverride,
                memorySpeedMt: memorySpeedMtOverride,
                hasBattery: hasBatteryOverride ?? false,
                batteryChargePercent: batteryChargePercentOverride,
                isAdmin: isRunningAsAdminOverride ?? true,
                isCorporateManaged: isDomainOrPolicyManagedOverride ?? false)
            : _systemProbe;

        var snapshot = existingSnapshot ?? await _monitorService.CaptureSnapshotAsync();
        DateTime now = DateTime.UtcNow;
        bool isAdmin = isRunningAsAdminOverride ?? activeProbe.IsRunningAsAdministrator();
        bool sensorSubsystemAvailable = snapshot.IsHardwareMonitorAvailable;

        // 1. CPU Identification & Capabilities (Never divides threads by 2 to guess physical cores)
        var (probeCpuName, probePhysicalCores, probeLogicalProcessors) = activeProbe.QueryCpuDetails();
        string rawCpuName = !string.IsNullOrWhiteSpace(snapshot.CpuModelName)
            ? snapshot.CpuModelName!
            : !string.IsNullOrWhiteSpace(probeCpuName)
                ? probeCpuName!
                : useIsolatedMode
                    ? "Indisponível"
                    : Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Indisponível";

        var cpuVendor = DetectCpuVendor(rawCpuName);
        string cpuArch = RuntimeInformation.OSArchitecture.ToString();
        int logicalCores = logicalProcessorsOverride
                           ?? probeLogicalProcessors
                           ?? (useIsolatedMode ? InferKnownCpuLogicalProcessors(rawCpuName) : Math.Max(1, Environment.ProcessorCount));
        int? physicalCores = physicalCoresOverride ?? probePhysicalCores;
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

        // 2. Independent Multi-GPU Enumeration & Merging (Separates physical GPUs from virtual/remote/basic display adapters)
        var wmiGpuCandidates = complementaryWmiGpuAdaptersOverride ?? activeProbe.QueryGpuAdapters();
        var (physicalGpus, virtualDisplayAdapters) = EnumerateAllGpusIndependently(
            snapshot,
            sensorSubsystemAvailable,
            isAdmin,
            wmiGpuCandidates
        );

        // Select primary GPU strictly among physical eligible GPUs (discrete prioritized over integrated)
        var primaryGpu = SelectPrimaryGpuAdapter(physicalGpus);

        // 3. Memory Identification (Queries real SMBIOSMemoryType and ConfiguredClockSpeed; null when unavailable)
        float? ramUsed = snapshot.RamUsedGb;
        float? ramTotal = snapshot.RamTotalGb;
        float? ramAvail = (ramTotal.HasValue && ramUsed.HasValue) ? Math.Max(0f, ramTotal.Value - ramUsed.Value) : null;
        float? ramPct = (ramTotal.HasValue && ramTotal.Value > 0 && ramUsed.HasValue)
            ? (float)Math.Round(ramUsed.Value / ramTotal.Value * 100.0, 1)
            : null;

        var (smbiosGeneration, smbiosSpeedMt) = activeProbe.QueryMemoryDetails();
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
        var networkList = activeProbe.QueryNetworkAdapters();

        // 6. Battery & Power (Reads real Win32 SYSTEM_POWER_STATUS in production; never hardcodes 100%)
        var batteryInfo = activeProbe.QueryBatteryPowerInfo(hasBatteryOverride, batteryChargePercentOverride);

        // 7. Automatic System Profile Classification (§5.1) — Uses only physical GPUs, never virtual adapters
        bool isCorporateManaged = isDomainOrPolicyManagedOverride ?? activeProbe.IsCorporateDomainOrPolicyManaged();
        var (profile, justification) = ClassifySystemHardwareProfile(
            cpuInfo,
            physicalGpus,
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
        capabilityMatrix.AddRange(physicalGpus.Select(g => g.Capabilities));
        capabilityMatrix.AddRange(storageList.Select(s => s.Capabilities));
        capabilityMatrix.AddRange(networkList.Select(n => n.Capabilities));

        return new UniversalHardwareReport(
            CapturedAtUtc: now,
            DetectedProfile: profile,
            ProfileJustification: justification,
            IsRunningAsAdmin: isAdmin,
            Cpu: cpuInfo,
            Gpus: physicalGpus,
            PrimaryGpu: primaryGpu,
            Memory: memoryInfo,
            StorageDevices: storageList,
            NetworkAdapters: networkList,
            BatteryAndPower: batteryInfo,
            CapabilityMatrix: capabilityMatrix,
            VirtualDisplayAdapters: virtualDisplayAdapters
        );
    }

    /// <summary>
    /// Selects the primary GPU strictly among physical GPUs (PhysicalDiscrete prioritized over PhysicalIntegrated).
    /// Virtual, remote, basic, and unknown adapters are never eligible to be selected as PrimaryGpu.
    /// </summary>
    public static DetectedGpuAdapterInfo? SelectPrimaryGpuAdapter(IReadOnlyList<DetectedGpuAdapterInfo> gpus)
    {
        if (gpus == null || gpus.Count == 0) return null;

        return gpus
            .Where(g => g.IsPhysicalGpu)
            .OrderByDescending(g => g.IsDiscrete && g.AdapterKind == GpuAdapterKind.PhysicalDiscrete)
            .ThenByDescending(g => g.Capabilities.SensorAvailable)
            .ThenByDescending(g => g.VramTotalMb.Value ?? 0f)
            .FirstOrDefault();
    }

    /// <summary>
    /// Merges GPU adapters enumerated by LibreHardwareMonitor with GPU adapters enumerated via WMI/Registry.
    /// - Deduplicates by PCI instance path and normalized PCI VEN_xxxx&DEV_xxxx or canonical model name.
    /// - Never merges two distinct physical GPUs that have different PCI instance paths or different VEN/DEV IDs.
    /// - Preserves all valid sensor values from LHM without overwriting them with null values from WMI.
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
                // Also prefer the richer VRAM value if WMI hit the 4GB uint32 cap while LHM reported > 4GB
                float? mergedVramTotal = (existing.VramTotalMb.HasValue && wmi.VramTotalMb.HasValue)
                    ? Math.Max(existing.VramTotalMb.Value, wmi.VramTotalMb.Value)
                    : (existing.VramTotalMb ?? wmi.VramTotalMb);

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
                    VramTotalMb: mergedVramTotal,
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

    public static int FindMatchingAdapterIndex(
        List<GpuAdapterTelemetryPoint> existingList,
        HashSet<int> alreadyMatchedIndices,
        GpuAdapterTelemetryPoint candidate)
    {
        // Pass 1: Exact full PNPDeviceID match (highest confidence)
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

        string? candidatePciKey = ExtractNormalizedPciKey(candidate.PnpDeviceId);

        // Pass 2: Match by normalized PCI VEN_xxxx&DEV_xxxx key ONLY if the two entries do not have conflicting PCI slot/instance IDs
        if (!string.IsNullOrEmpty(candidatePciKey))
        {
            for (int i = 0; i < existingList.Count; i++)
            {
                if (alreadyMatchedIndices.Contains(i)) continue;
                if (HasConflictingPciInstanceId(existingList[i].PnpDeviceId, candidate.PnpDeviceId))
                {
                    // Two physical cards in different PCIe slots -> never merge!
                    continue;
                }

                string? existingPciKey = ExtractNormalizedPciKey(existingList[i].PnpDeviceId);
                if (!string.IsNullOrEmpty(existingPciKey) &&
                    string.Equals(existingPciKey, candidatePciKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        // Pass 3: Match by canonical GPU model name ONLY if neither PCI VEN/DEV nor PCI instance path conflicts
        string candidateCanonical = NormalizeGpuModelName(candidate.Model);
        for (int i = 0; i < existingList.Count; i++)
        {
            if (alreadyMatchedIndices.Contains(i)) continue;

            if (HasConflictingPciInstanceId(existingList[i].PnpDeviceId, candidate.PnpDeviceId))
            {
                continue;
            }

            string? existingPciKey = ExtractNormalizedPciKey(existingList[i].PnpDeviceId);
            if (!string.IsNullOrEmpty(existingPciKey) && !string.IsNullOrEmpty(candidatePciKey) &&
                !string.Equals(existingPciKey, candidatePciKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string existingCanonical = NormalizeGpuModelName(existingList[i].Model);
            if (!string.IsNullOrEmpty(existingCanonical) &&
                string.Equals(existingCanonical, candidateCanonical, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool HasConflictingPciInstanceId(string? pnpIdA, string? pnpIdB)
    {
        string? instanceA = ExtractPciSlotInstanceId(pnpIdA);
        string? instanceB = ExtractPciSlotInstanceId(pnpIdB);
        if (string.IsNullOrEmpty(instanceA) || string.IsNullOrEmpty(instanceB))
        {
            return false;
        }
        return !string.Equals(instanceA, instanceB, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPciSlotInstanceId(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        string trimmed = pnpDeviceId.Trim();
        if (!trimmed.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) return null;

        // Format: PCI\VEN_xxxx&DEV_xxxx&SUBSYS_xxxxxxxx&REV_xx\<InstanceId>
        int firstSlash = trimmed.IndexOf('\\');
        if (firstSlash < 0 || firstSlash + 1 >= trimmed.Length) return null;
        int secondSlash = trimmed.IndexOf('\\', firstSlash + 1);
        if (secondSlash < 0 || secondSlash + 1 >= trimmed.Length) return null;

        return trimmed.Substring(secondSlash + 1).Trim();
    }

    public static string? ExtractNormalizedPciKey(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        var match = PciVenDevRegex.Match(pnpDeviceId);
        if (!match.Success) return null;

        // Normalize to VEN_xxxx&DEV_xxxx so short and long PCI strings for the same device match
        string upper = match.Value.ToUpperInvariant();
        int subsysIdx = upper.IndexOf("&SUBSYS_", StringComparison.Ordinal);
        return subsysIdx > 0 ? upper.Substring(0, subsysIdx) : upper;
    }

    private static string NormalizeGpuModelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return name
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Series", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Graphics", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string? PreferPnpDeviceId(string? existingId, string? wmiId)
    {
        if (!string.IsNullOrWhiteSpace(wmiId) && wmiId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
            return wmiId;
        return !string.IsNullOrWhiteSpace(existingId) ? existingId : wmiId;
    }

    public static HardwareVendor DetectCpuVendor(string? cpuName, string? pnpDeviceId = null)
    {
        if (!string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            if (pnpDeviceId.Contains("AuthenticAMD", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("VEN_1022", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Amd;

            if (pnpDeviceId.Contains("GenuineIntel", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Intel;

            if (pnpDeviceId.Contains("QCOM", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("VEN_5143", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Qualcomm;
        }

        if (string.IsNullOrWhiteSpace(cpuName) || cpuName == "Indisponível")
            return HardwareVendor.Unknown;

        if (cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Athlon", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Amd;

        if (cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Core(TM)", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Core i", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Xeon", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Pentium", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Celeron", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Intel;

        if (cpuName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("Oryon", StringComparison.OrdinalIgnoreCase))
            return HardwareVendor.Qualcomm;

        return HardwareVendor.Unknown;
    }

    public static HardwareVendor DetectGpuVendor(
        string? gpuName,
        string? pnpDeviceId = null,
        string? hardwareType = null)
    {
        // 1. Check reliable PCI Vendor ID in PNPDeviceID when available
        if (!string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            if (pnpDeviceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Nvidia;
            if (pnpDeviceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("VEN_1022", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Amd;
            if (pnpDeviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Intel;
            if (pnpDeviceId.Contains("VEN_1414", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("VMBUS", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Microsoft;
            if (pnpDeviceId.Contains("VEN_5143", StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains("QCOM", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Qualcomm;
        }

        // 2. Check explicit GPU name patterns
        if (!string.IsNullOrWhiteSpace(gpuName))
        {
            if (gpuName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Microsoft;

            if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Tesla", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Nvidia;

            if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("FirePro", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Instinct", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Amd;

            if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Arc(TM)", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Intel;

            if (gpuName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Qualcomm;
        }

        // 3. Check LibreHardwareMonitor HardwareType tag
        if (!string.IsNullOrWhiteSpace(hardwareType))
        {
            if (hardwareType.Contains("GpuNvidia", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Nvidia;
            if (hardwareType.Contains("GpuAmd", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Amd;
            if (hardwareType.Contains("GpuIntel", StringComparison.OrdinalIgnoreCase))
                return HardwareVendor.Intel;
        }

        return HardwareVendor.Unknown;
    }

    /// <summary>
    /// Explicitly classifies a display/GPU adapter into one of 6 states:
    /// PhysicalDiscrete, PhysicalIntegrated, VirtualAdapter, RemoteDisplayAdapter, BasicDisplayAdapter, or UnknownAdapter.
    /// Never assumes an unknown or virtual adapter is a discrete GPU.
    /// </summary>
    public static GpuAdapterKind ClassifyGpuAdapterKind(
        string? gpuName,
        string? pnpDeviceId = null,
        HardwareVendor? resolvedVendor = null,
        string? hardwareType = null)
    {
        string name = gpuName?.Trim() ?? string.Empty;
        string pnp = pnpDeviceId?.Trim() ?? string.Empty;
        var vendor = resolvedVendor ?? DetectGpuVendor(name, pnp, hardwareType);

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(pnp))
            return GpuAdapterKind.UnknownAdapter;

        // 1. Remote Display Adapters (RDP, RemoteFX, Citrix, Teradici)
        if (name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RDPDD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RDPUDD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RemoteFX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Citrix Indirect", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Teradici", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("RDP", StringComparison.OrdinalIgnoreCase))
        {
            return GpuAdapterKind.RemoteDisplayAdapter;
        }

        // 2. Basic Display / Fallback OS Adapters
        if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Standard VGA", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_1414&DEV_008C", StringComparison.OrdinalIgnoreCase))
        {
            return GpuAdapterKind.BasicDisplayAdapter;
        }

        // 3. Virtual / Hypervisor / Screen Mirroring Adapters (Hyper-V, VMware, VirtualBox, QEMU, Parsec, IDD)
        if (name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("VBox", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("QEMU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("VirtIO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Red Hat QXL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Parsec Virtual", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("IddSampleDriver", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Mirror Driver", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Spacedesk", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LuminonCore", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_1414&DEV_5353", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_15AD", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_80EE", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_1AF4", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_1B36", StringComparison.OrdinalIgnoreCase) ||
            pnp.StartsWith("VMBUS\\", StringComparison.OrdinalIgnoreCase) ||
            pnp.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
            pnp.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase) ||
            pnp.StartsWith("UMB\\", StringComparison.OrdinalIgnoreCase) ||
            vendor == HardwareVendor.Microsoft)
        {
            return GpuAdapterKind.VirtualAdapter;
        }

        // 4. Physical Integrated GPUs
        if (IsIntegratedGpu(name))
        {
            return GpuAdapterKind.PhysicalIntegrated;
        }

        // 5. Physical Discrete GPUs — requires positive evidence of a real GPU vendor/family
        if (vendor is HardwareVendor.Nvidia or HardwareVendor.Amd or HardwareVendor.Intel &&
            HasPositiveDiscreteGpuEvidence(name, pnp, vendor, hardwareType))
        {
            return GpuAdapterKind.PhysicalDiscrete;
        }

        // 6. Unknown / Unverified Adapter — remains UnknownAdapter, never assumed to be a discrete GPU
        return GpuAdapterKind.UnknownAdapter;
    }

    private static bool HasPositiveDiscreteGpuEvidence(
        string name,
        string pnp,
        HardwareVendor vendor,
        string? hardwareType)
    {
        if (vendor == HardwareVendor.Nvidia)
        {
            return name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Tesla", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Titan", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                   pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(hardwareType, "GpuNvidia", StringComparison.OrdinalIgnoreCase);
        }

        if (vendor == HardwareVendor.Amd)
        {
            return name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon R9", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon R7", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("FirePro", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Instinct", StringComparison.OrdinalIgnoreCase) ||
                   (!IsIntegratedGpu(name) && (pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(hardwareType, "GpuAmd", StringComparison.OrdinalIgnoreCase)));
        }

        if (vendor == HardwareVendor.Intel)
        {
            return name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Iris Xe MAX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Data Center GPU", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("DG1", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("DG2", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static bool IsIntegratedGpu(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return false;
        if (LibreHardwareMonitorWrapper.IsIntegratedGpuName(gpuName)) return true;

        if (gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Iris Xe MAX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return gpuName.Contains("Radeon 780M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 760M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 740M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 680M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 660M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 890M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 880M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon 610M", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon Vega", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Intel(R) Graphics", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Equals("Intel Graphics", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProfessionalWorkstationGpu(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return false;
        return gpuName.Contains("RTX A", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("FirePro", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Tesla", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Instinct", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("H100", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("A100", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("RTX 6000", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("RTX 5000 Ada", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("RTX 4000 Ada", StringComparison.OrdinalIgnoreCase);
    }

    public static (SystemHardwareProfile Profile, string Justification) ClassifySystemHardwareProfile(
        DetectedCpuInfo cpu,
        IReadOnlyList<DetectedGpuAdapterInfo> gpus,
        DetectedGpuAdapterInfo? primaryGpu,
        DetectedMemoryInfo memory,
        bool hasBattery,
        bool isCorporateManaged)
    {
        // Only physical GPUs participate in hardware profile classification
        var physicalGpus = (gpus ?? Array.Empty<DetectedGpuAdapterInfo>())
            .Where(g => g.IsPhysicalGpu)
            .ToList();

        var physicalDiscreteGpus = physicalGpus
            .Where(g => g.IsDiscrete && g.AdapterKind == GpuAdapterKind.PhysicalDiscrete)
            .ToList();

        var physicalIntegratedGpus = physicalGpus
            .Where(g => g.IsIntegrated && g.AdapterKind == GpuAdapterKind.PhysicalIntegrated)
            .ToList();

        var effectivePrimaryGpu = primaryGpu != null && primaryGpu.IsPhysicalGpu
            ? primaryGpu
            : SelectPrimaryGpuAdapter(physicalGpus);

        float ramGb = memory.TotalGb.Value ?? 0f;
        float vramMb = effectivePrimaryGpu?.VramTotalMb.Value ?? 0f;
        bool hasDiscreteGpu = physicalDiscreteGpus.Count > 0;
        bool hasIntegratedGpu = physicalIntegratedGpus.Count > 0;
        bool isHybridMultiGpu = hasDiscreteGpu && hasIntegratedGpu;
        string cpuName = cpu.Name ?? string.Empty;

        // 1. Corporate: Domain or Group Policy restricted environment
        if (isCorporateManaged)
        {
            return (
                SystemHardwareProfile.Corporate,
                "Ambiente gerenciado por políticas corporativas ou domínio detectado. Operações restritas por GPO operam em modo seguro somente-leitura."
            );
        }

        // 2. Notebook: Portable device with battery (checked before Workstation/Gamer so hybrid laptops are never misclassified as Desktop/Workstation)
        if (hasBattery)
        {
            string gpuNote = isHybridMultiGpu
                ? $"com gráficos híbridos iGPU + dGPU ({physicalGpus.Count} adaptadores físicos: {string.Join(" + ", physicalGpus.Select(g => g.Name))})"
                : hasDiscreteGpu
                    ? $"com GPU dedicada ({effectivePrimaryGpu?.Name})"
                    : "com gráficos integrados de eficiência energética";
            return (
                SystemHardwareProfile.Notebook,
                $"Dispositivo portátil com bateria detectado {gpuNote}. Otimizações priorizam eficiência térmica, autonomia e alternância gráfica."
            );
        }

        // 3. Workstation: Real professional CPU (Threadripper/Xeon/EPYC), professional GPU (RTX A / Quadro / Radeon PRO),
        //    multiple professional GPUs, or >= 64 GB RAM with >= 24 logical processors
        bool isWorkstationCpu = cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
                                cpuName.Contains("Xeon", StringComparison.OrdinalIgnoreCase) ||
                                cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase);

        bool hasProfessionalGpu = physicalDiscreteGpus.Any(g => IsProfessionalWorkstationGpu(g.Name));
        int professionalGpuCount = physicalDiscreteGpus.Count(g => IsProfessionalWorkstationGpu(g.Name));

        if (isWorkstationCpu ||
            hasProfessionalGpu ||
            professionalGpuCount >= 2 ||
            (ramGb >= 64f && cpu.LogicalProcessors >= 24))
        {
            return (
                SystemHardwareProfile.Workstation,
                $"Estação de trabalho profissional detectada ({cpu.Name}, {ramGb:F0} GB RAM, {physicalGpus.Count} GPU(s) física(s)). Foco em estabilidade sustentada e cargas multi-thread."
            );
        }

        // 4. HighPerformance: Desktop with discrete GPU and (>= 12 GB VRAM, >= 32 GB RAM, or multiple discrete consumer GPUs)
        if (hasDiscreteGpu && (vramMb >= 12000f || ramGb >= 32f || physicalDiscreteGpus.Count >= 2))
        {
            return (
                SystemHardwareProfile.HighPerformance,
                $"Desktop de alto desempenho detectado ({cpu.Name} + {effectivePrimaryGpu?.Name}, {physicalGpus.Count} GPU(s) física(s) instalada(s))."
            );
        }

        // 5. Gamer: Desktop with physical discrete GPU
        if (hasDiscreteGpu)
        {
            return (
                SystemHardwareProfile.Gamer,
                $"Sistema Gamer com GPU física dedicada detectado ({effectivePrimaryGpu?.Name}). Foco em estabilidade de frametime (1% Low), prioridade MMCSS e baixa latência de rede."
            );
        }

        // 6. Basic: Integrated graphics or display-only adapter
        return (
            SystemHardwareProfile.Basic,
            "Computador com vídeo integrado ou sem GPU física dedicada detectado. Foco em responsividade do Windows, redução de consumo de RAM e inicialização rápida."
        );
    }

    public static (List<DetectedGpuAdapterInfo> PhysicalGpus, List<DetectedGpuAdapterInfo> VirtualDisplayAdapters) EnumerateAllGpusIndependently(
        HardwareSnapshot snapshot,
        bool sensorSubsystemAvailable,
        bool isAdmin,
        IReadOnlyList<GpuAdapterTelemetryPoint>? complementaryWmiGpuAdapters)
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

        var wmiPoints = complementaryWmiGpuAdapters ?? Array.Empty<GpuAdapterTelemetryPoint>();
        var mergedPoints = MergeGpuAdapters(lhmPoints, wmiPoints);

        var physicalList = new List<DetectedGpuAdapterInfo>();
        var virtualOrDisplayList = new List<DetectedGpuAdapterInfo>();

        foreach (var pt in mergedPoints)
        {
            if (string.IsNullOrWhiteSpace(pt.Model)) continue;

            string name = pt.Model.Trim();
            var vendor = DetectGpuVendor(name, pt.PnpDeviceId, pt.HardwareType);
            var adapterKind = ClassifyGpuAdapterKind(name, pt.PnpDeviceId, vendor, pt.HardwareType);

            bool isIntegrated = adapterKind == GpuAdapterKind.PhysicalIntegrated;
            bool isDiscrete = adapterKind == GpuAdapterKind.PhysicalDiscrete;
            bool isPhysical = isIntegrated || isDiscrete;

            string archFamily = ResolveOptionalVendorAdapterFamily(name, vendor, adapterKind);

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

            var vramTotalMetric = pt.VramTotalMb.HasValue && isPhysical
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
                ComponentCategory: isPhysical ? "GPU" : "VirtualDisplayAdapter",
                ComponentName: name,
                Vendor: vendor,
                Identified: vendor != HardwareVendor.Unknown,
                Monitorable: isPhysical,
                SensorAvailable: isPhysical && (hasCoreTemp || pt.LoadPercent.HasValue),
                Adjustable: false,
                BenchmarkCompatible: isPhysical,
                DiagnosticCompatible: true,
                RequiresDriver: isPhysical && !hasCoreTemp && !pt.LoadPercent.HasValue,
                RequiresAdmin: isPhysical && !isAdmin && !hasCoreTemp,
                NotSupported: !isPhysical,
                CapabilityNotes: isPhysical
                    ? $"Adaptador físico ({archFamily}): {(isDiscrete ? "GPU Dedicada" : "GPU Integrada")}. " +
                      (hasCoreTemp ? "Sensores físicos ativos." : "Componente físico identificado; sensores térmicos indisponíveis ou adaptador em economia de energia.")
                    : $"Adaptador não-físico ({adapterKind}): ignorado na contagem de GPUs físicas e na classificação de perfil."
            );

            var adapterInfo = new DetectedGpuAdapterInfo(
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
                PnpDeviceId: pt.PnpDeviceId,
                AdapterKind: adapterKind
            );

            if (isPhysical)
            {
                physicalList.Add(adapterInfo);
            }
            else
            {
                virtualOrDisplayList.Add(adapterInfo);
            }
        }

        // Order physical GPUs so discrete GPUs come before integrated GPUs
        var orderedPhysical = physicalList
            .OrderByDescending(g => g.IsDiscrete && g.AdapterKind == GpuAdapterKind.PhysicalDiscrete)
            .ThenByDescending(g => g.Capabilities.SensorAvailable)
            .ThenByDescending(g => g.VramTotalMb.Value ?? 0f)
            .ToList();

        return (orderedPhysical, virtualOrDisplayList);
    }

    [SupportedOSPlatform("windows")]
    internal static List<GpuAdapterTelemetryPoint> QueryWmiAndRegistryGpuAdapters()
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

    internal static (string? Name, int? PhysicalCores, int? LogicalProcessors) QueryWmiCpuDetails()
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

    internal static (string? MemoryGeneration, int? ConfiguredSpeedMt) QueryWmiMemoryDetails()
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

    internal static DetectedBatteryPowerInfo ReadRealBatteryPowerInfo(
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

    private static int InferKnownCpuLogicalProcessors(string? cpuName)
    {
        if (string.IsNullOrWhiteSpace(cpuName)) return 8;
        if (cpuName.Contains("7965WX", StringComparison.OrdinalIgnoreCase)) return 48;
        if (cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase)) return 32;
        if (cpuName.Contains("14700", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("13700", StringComparison.OrdinalIgnoreCase)) return 24;
        if (cpuName.Contains("7800X3D", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("7940HS", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("8700G", StringComparison.OrdinalIgnoreCase)) return 16;
        if (cpuName.Contains("7600X", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("12400", StringComparison.OrdinalIgnoreCase) ||
            cpuName.Contains("10400", StringComparison.OrdinalIgnoreCase)) return 12;
        if (cpuName.Contains("i3-6100", StringComparison.OrdinalIgnoreCase)) return 4;
        return 8;
    }

    private static string ResolveOptionalVendorAdapterFamily(string gpuName, HardwareVendor vendor, GpuAdapterKind adapterKind)
    {
        if (adapterKind is GpuAdapterKind.VirtualAdapter or GpuAdapterKind.RemoteDisplayAdapter or GpuAdapterKind.BasicDisplayAdapter)
        {
            return adapterKind switch
            {
                GpuAdapterKind.RemoteDisplayAdapter => "Remote Display Protocol Adapter",
                GpuAdapterKind.BasicDisplayAdapter => "OS Basic Display Fallback Adapter",
                _ => "Virtual / Hypervisor Display Adapter"
            };
        }

        if (adapterKind == GpuAdapterKind.UnknownAdapter)
        {
            return "Unclassified Display Adapter";
        }

        bool isDiscrete = adapterKind == GpuAdapterKind.PhysicalDiscrete;
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

    internal static List<DetectedNetworkAdapterInfo> BuildNetworkAdapterList()
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

    internal static bool CheckIsRunningAsAdministrator()
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

    internal static bool DetectCorporateDomainOrPolicyEnvironment()
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
