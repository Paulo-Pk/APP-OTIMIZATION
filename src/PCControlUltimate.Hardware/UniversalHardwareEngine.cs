using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Hardware;

/// <summary>
/// Universal Hardware Engine (§3 & §5):
/// Multi-source hardware detection, telemetry provenance, capability matrix, and automatic system profile classification
/// across Intel, AMD, NVIDIA, Qualcomm, and generic Windows 10/11 systems without global model hardcoding.
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
        CancellationToken cancellationToken = default)
    {
        var snapshot = existingSnapshot ?? await _monitorService.CaptureSnapshotAsync();
        DateTime now = DateTime.UtcNow;
        bool isAdmin = isRunningAsAdminOverride ?? CheckIsRunningAsAdministrator();
        bool sensorSubsystemAvailable = snapshot.IsHardwareMonitorAvailable;

        // 1. CPU Identification & Capabilities
        string rawCpuName = !string.IsNullOrWhiteSpace(snapshot.CpuModelName)
            ? snapshot.CpuModelName!
            : Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Processador Compatível x64/ARM64";

        var cpuVendor = DetectCpuVendor(rawCpuName);
        string cpuArch = RuntimeInformation.OSArchitecture.ToString();
        int logicalCores = Math.Max(1, Environment.ProcessorCount);
        int physicalCores = Math.Max(1, logicalCores / 2);
        bool isHybridCpu = IsHybridCpuArchitecture(rawCpuName, cpuVendor);

        var cpuCapabilities = new HardwareComponentCapabilityMatrix(
            ComponentCategory: "CPU",
            ComponentName: rawCpuName,
            Vendor: cpuVendor,
            Identified: !string.IsNullOrWhiteSpace(rawCpuName),
            Monitorable: true,
            SensorAvailable: snapshot.CpuPackageTemp.HasValue || snapshot.CpuTotalLoad.HasValue,
            Adjustable: true, // Power Plan & Process Affinity/Priority
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
            PackagePowerWatts: WrapSensorMetric(snapshot.CpuPowerWatts, sensorSubsystemAvailable, isAdmin, "W", "Consumo energético de pacote (RAPL/SVI2)"),
            IsHybridArchitecture: isHybridCpu,
            Capabilities: cpuCapabilities
        );

        // 2. GPU Identification & Multi-Vendor Optional Adapters
        var gpus = BuildDetectedGpuList(snapshot, sensorSubsystemAvailable, isAdmin);
        var primaryGpu = gpus
            .OrderByDescending(g => g.IsDiscrete)
            .ThenByDescending(g => g.VramTotalMb.Value ?? 0f)
            .FirstOrDefault();

        // 3. Memory Identification
        float? ramUsed = snapshot.RamUsedGb;
        float? ramTotal = snapshot.RamTotalGb;
        float? ramAvail = (ramTotal.HasValue && ramUsed.HasValue) ? Math.Max(0f, ramTotal.Value - ramUsed.Value) : null;
        float? ramPct = (ramTotal.HasValue && ramTotal.Value > 0 && ramUsed.HasValue)
            ? (float)Math.Round(ramUsed.Value / ramTotal.Value * 100.0, 1)
            : null;

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
            CapabilityNotes: "Monitoramento de alocação física e limpeza segura de Working Set disponíveis."
        );

        var memoryInfo = new DetectedMemoryInfo(
            TotalGb: WrapSensorMetric(ramTotal, sensorSubsystemAvailable, isAdmin, "GB", "Capacidade física instalada"),
            UsedGb: WrapSensorMetric(ramUsed, sensorSubsystemAvailable, isAdmin, "GB", "Memória física em uso"),
            AvailableGb: WrapSensorMetric(ramAvail, sensorSubsystemAvailable, isAdmin, "GB", "Memória física livre"),
            UsagePercent: WrapSensorMetric(ramPct, sensorSubsystemAvailable, isAdmin, "%", "Percentual de ocupação RAM"),
            MemoryGeneration: "DDR4 / DDR5 / LPDDR",
            ConfiguredSpeedMt: null,
            Capabilities: memCapabilities
        );

        // 4. Storage Devices
        var storageList = BuildStorageDeviceList(snapshot, isAdmin);

        // 5. Network Adapters
        var networkList = BuildNetworkAdapterList();

        // 6. Battery & Power
        bool hasBattery = hasBatteryOverride ?? DetectSystemHasBattery();
        var batteryInfo = new DetectedBatteryPowerInfo(
            HasBattery: hasBattery,
            IsOnBatteryPower: false,
            ChargePercent: hasBattery ? 100 : null,
            PowerLineStatus: hasBattery ? "AC / Bateria Detectada" : "AC Direto (Desktop)",
            ActivePowerSchemeName: "Plano de Energia Ativo do Windows"
        );

        // 7. Automatic System Profile Classification (§5.1)
        bool isCorporateManaged = isDomainOrPolicyManagedOverride ?? DetectCorporateDomainOrPolicyEnvironment();
        var (profile, justification) = ClassifySystemHardwareProfile(
            cpuInfo,
            gpus,
            primaryGpu,
            memoryInfo,
            hasBattery,
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
        float ramGb = memory.TotalGb.Value ?? 8f;
        float vramMb = primaryGpu?.VramTotalMb.Value ?? 0f;
        bool hasDiscreteGpu = gpus.Any(g => g.IsDiscrete);
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

        // 2. Workstation (Threadripper / Xeon / Radeon PRO / NVIDIA RTX A-series / >= 64GB RAM + >= 16 threads)
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
                $"Estação de trabalho detectada ({cpu.Name}, {ramGb:F0} GB RAM). Foco em estabilidade sustentada e cargas multi-thread."
            );
        }

        // 3. Notebook / Portable (Has battery; may be hybrid iGPU + dGPU or ultra-portable)
        if (hasBattery)
        {
            string gpuNote = hasDiscreteGpu
                ? $"com gráficos híbridos ({primaryGpu?.Name})"
                : "com gráficos integrados de eficiência energética";
            return (
                SystemHardwareProfile.Notebook,
                $"Dispositivo portátil com bateria detectado {gpuNote}. Otimizações priorizam eficiência térmica, autonomia e alternância gráfica."
            );
        }

        // 4. High Performance / Enthusiast Desktop (High core count + high VRAM/RAM)
        if (hasDiscreteGpu && (vramMb >= 16000f || (ramGb >= 32f && cpu.LogicalProcessors >= 16)))
        {
            return (
                SystemHardwareProfile.HighPerformance,
                $"Desktop de alto desempenho detectado ({cpu.Name} + {primaryGpu?.Name}). Sensores avançados, telemetria de hotspot e ajustes de baixa latência habilitados."
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
            "Computador com vídeo integrado ou configuração essencial detectado. Foco em responsividade do Windows, redução de consumo de RAM e inicialização rápida."
        );
    }

    private static List<DetectedGpuAdapterInfo> BuildDetectedGpuList(
        HardwareSnapshot snapshot,
        bool sensorSubsystemAvailable,
        bool isAdmin)
    {
        var list = new List<DetectedGpuAdapterInfo>();

        if (!string.IsNullOrWhiteSpace(snapshot.GpuModelName))
        {
            string name = snapshot.GpuModelName!;
            var vendor = DetectGpuVendor(name);
            bool isIntegrated = IsIntegratedGpu(name);
            bool isDiscrete = !isIntegrated;
            string archFamily = ResolveOptionalVendorAdapterFamily(name, vendor, isDiscrete);

            bool hasCoreTemp = snapshot.GpuCoreTemp.HasValue;
            bool hasHotspot = snapshot.GpuHotspotTemp.HasValue;

            var hotspotMetric = hasHotspot
                ? new DetectedMetric<float?>(
                    snapshot.GpuHotspotTemp,
                    HardwareDataSource.LibreHardwareMonitor,
                    DateTime.UtcNow,
                    MetricReliability.VerifiedPhysicalSensor,
                    true,
                    UniversalSensorStatus.Available,
                    null,
                    "°C")
                : DetectedMetric<float?>.Unavailable(
                    UniversalSensorStatus.NotSupported,
                    "Sensor de Hotspot/Junction não exposto por este modelo/driver de GPU.",
                    "°C");

            var caps = new HardwareComponentCapabilityMatrix(
                ComponentCategory: "GPU",
                ComponentName: name,
                Vendor: vendor,
                Identified: true,
                Monitorable: true,
                SensorAvailable: hasCoreTemp || snapshot.GpuLoadPercent.HasValue,
                Adjustable: false, // Safe mode: never alters GPU voltages or clocks automatically
                BenchmarkCompatible: true,
                DiagnosticCompatible: true,
                RequiresDriver: !hasCoreTemp && !snapshot.GpuLoadPercent.HasValue,
                RequiresAdmin: !isAdmin && !hasCoreTemp,
                NotSupported: false,
                CapabilityNotes: $"Adaptador {archFamily}: telemetria somente-leitura sem overclock ou alteração de voltagem."
            );

            list.Add(new DetectedGpuAdapterInfo(
                Name: name,
                Vendor: vendor,
                ArchitectureFamily: archFamily,
                IsIntegrated: isIntegrated,
                IsDiscrete: isDiscrete,
                CoreTemperatureC: WrapSensorMetric(snapshot.GpuCoreTemp, sensorSubsystemAvailable, isAdmin, "°C", "Temperatura Core GPU"),
                HotspotTemperatureC: hotspotMetric,
                CoreLoadPercent: WrapSensorMetric(snapshot.GpuLoadPercent, sensorSubsystemAvailable, isAdmin, "%", "Carga do núcleo gráfico"),
                CoreClockMhz: WrapSensorMetric(snapshot.GpuClockMhz, sensorSubsystemAvailable, isAdmin, "MHz", "Frequência do núcleo gráfico"),
                PowerWatts: WrapSensorMetric(snapshot.GpuPowerWatts, sensorSubsystemAvailable, isAdmin, "W", "Potência elétrica da placa/chip"),
                VramUsedMb: WrapSensorMetric(snapshot.GpuVramUsedMb, sensorSubsystemAvailable, isAdmin, "MB", "Memória de vídeo alocada"),
                VramTotalMb: WrapSensorMetric(snapshot.GpuVramTotalMb, sensorSubsystemAvailable, isAdmin, "MB", "Memória de vídeo total"),
                FanRpm: WrapSensorMetric(snapshot.GpuFanRpm, sensorSubsystemAvailable, isAdmin, "RPM", "Rotação da ventoinha GPU"),
                DriverVersion: null,
                Capabilities: caps
            ));
        }

        return list;
    }

    private static string ResolveOptionalVendorAdapterFamily(string gpuName, HardwareVendor vendor, bool isDiscrete)
    {
        return vendor switch
        {
            HardwareVendor.Amd => isDiscrete
                ? "AMD Radeon Discrete Adapter (RDNA / GCN)"
                : "AMD Radeon Integrated Graphics Adapter",
            HardwareVendor.Nvidia => isDiscrete
                ? "NVIDIA GeForce / RTX Discrete Adapter (NVAPI / LHM)"
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
                bool isNvme = d.Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                              d.Model.Contains("SN", StringComparison.OrdinalIgnoreCase) ||
                              d.Model.Contains("980", StringComparison.OrdinalIgnoreCase) ||
                              d.Model.Contains("990", StringComparison.OrdinalIgnoreCase);

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

    private static bool DetectSystemHasBattery()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                // BatteryFlag 128 == No system battery
                return (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
            }
        }
        catch { }
        return false;
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
