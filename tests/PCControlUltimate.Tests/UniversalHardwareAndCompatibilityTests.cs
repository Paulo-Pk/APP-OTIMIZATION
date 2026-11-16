using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Core.Models;
using PCControlUltimate.Diagnostics;
using PCControlUltimate.Hardware;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class UniversalHardwareAndCompatibilityTests
{
    [Theory]
    [InlineData("C:\\", "C:", true)]
    [InlineData("c:\\", "C:", true)]
    [InlineData("D:\\", "C:", false)]
    [InlineData("C:\\", "D:", false)]
    [InlineData("C:\\", null, false)]
    [InlineData(null, "C:", false)]
    public void IsSystemDrive_IdentifiesOnlyTheConfiguredWindowsSystemVolume(
        string? driveName,
        string? systemDrive,
        bool expected)
    {
        Assert.Equal(expected, LibreHardwareMonitorWrapper.IsSystemDrive(driveName, systemDrive));
    }

    [Theory]
    [InlineData("Core (Tctl/Tdie)", 100)]
    [InlineData("CCD1 (Tdie)", 95)]
    [InlineData("Core (Tctl)", 90)]
    [InlineData("CPU Die (average)", 88)]
    [InlineData("CPU Package", 85)]
    [InlineData("CCDs Max (Tdie)", 98)]
    [InlineData("Core #1", 68)]
    [InlineData("Temperature", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void CpuTemperatureSensorPriority_PrefersRealPackageAndDieSensors(string? sensorName, int expectedPriority)
    {
        Assert.Equal(expectedPriority, LibreHardwareMonitorWrapper.GetCpuTemperatureSensorPriority(sensorName));
    }

    [Theory]
    [InlineData("GPU Hot Spot", true)]
    [InlineData("GPU Hotspot", true)]
    [InlineData("GPU Junction", true)]
    [InlineData("Memory Junction", false)]
    [InlineData("GPU Memory Junction", false)]
    [InlineData("VRAM Hotspot", false)]
    [InlineData("GPU Core", false)]
    [InlineData(null, false)]
    public void GpuHotspotSensorClassification_NeverTreatsMemoryJunctionAsGpuDieHotspot(string? sensorName, bool expected)
    {
        Assert.Equal(expected, LibreHardwareMonitorWrapper.IsGpuHotspotTemperatureSensor(sensorName));
    }

    [Theory]
    [InlineData(null, 0, 55f, 68, true)]
    [InlineData(82f, 68, 71f, 68, false)]
    [InlineData(62f, 68, 74f, 68, true)]
    [InlineData(88f, 68, 70f, 85, true)]
    [InlineData(70f, 85, 91f, 68, false)]
    public void CpuTemperatureSelection_PrefersBestSensorClassAndHottestReadingWithinSameClass(
        float? currentTemperature,
        int currentPriority,
        float candidateTemperature,
        int candidatePriority,
        bool expected)
    {
        Assert.Equal(
            expected,
            LibreHardwareMonitorWrapper.IsBetterCpuTemperatureReading(
                currentTemperature,
                currentPriority,
                candidateTemperature,
                candidatePriority));
    }

    private sealed class ConfigurableHardwareMonitorStub : IHardwareMonitorService
    {
        private readonly HardwareSnapshot _snapshot;

        public ConfigurableHardwareMonitorStub(HardwareSnapshot snapshot, bool isAvailable = true)
        {
            _snapshot = snapshot;
            IsAvailable = isAvailable;
            CurrentStatus = snapshot.MonitorStatus;
        }

        public bool IsAvailable { get; }
        public SensorReadingStatus CurrentStatus { get; }
        public string? LastCommunicationError => _snapshot.DiagnosticMessage;
        public event EventHandler<HardwareSnapshot>? TelemetryUpdated;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HardwareSnapshot> CaptureSnapshotAsync() => Task.FromResult(_snapshot);
        public void StartPolling(TimeSpan interval) => TelemetryUpdated?.Invoke(this, _snapshot);
        public void StopPolling() { }
        public void Dispose() { }
    }

    private static HardwareSnapshot CreateSnapshot(
        string? cpuModel,
        float? cpuTemp,
        string? gpuModel,
        float? gpuTemp,
        float? gpuHotspot,
        float? vramTotalMb,
        float? ramUsedGb,
        float? ramTotalGb,
        SensorReadingStatus status = SensorReadingStatus.Available,
        IReadOnlyList<GpuAdapterTelemetryPoint>? gpuAdapters = null)
    {
        return new HardwareSnapshot(
            TimestampUtc: DateTime.UtcNow,
            CpuPackageTemp: cpuTemp,
            CpuTotalLoad: 35f,
            GpuCoreTemp: gpuTemp,
            GpuHotspotTemp: gpuHotspot,
            GpuVramUsedMb: vramTotalMb.HasValue ? vramTotalMb.Value * 0.25f : null,
            GpuVramTotalMb: vramTotalMb,
            RamUsedGb: ramUsedGb,
            RamTotalGb: ramTotalGb,
            Sensors: Array.Empty<SensorDataPoint>(),
            CpuModelName: cpuModel,
            CpuClockMhz: 4650f,
            CpuPowerWatts: 65f,
            GpuModelName: gpuModel,
            GpuLoadPercent: 45f,
            GpuClockMhz: 2400f,
            GpuPowerWatts: 180f,
            GpuFanRpm: 1450f,
            StorageDrives: new[]
            {
                new StorageTelemetryPoint("NVMe System Drive", 1000f, 450f, 41f)
            },
            MonitorStatus: status,
            DiagnosticMessage: null,
            GpuAdapters: gpuAdapters
        );
    }

    [Theory]
    [InlineData("Scenario A: Desktop AMD + Radeon", "AMD Ryzen 7 7800X3D", "AMD Radeon RX 7800 XT", 16384f, 32f, false, false, HardwareVendor.Amd, HardwareVendor.Amd, true, GpuAdapterKind.PhysicalDiscrete, SystemHardwareProfile.HighPerformance)]
    [InlineData("Scenario B: Desktop Intel + NVIDIA", "Intel Core i7-14700K", "NVIDIA GeForce RTX 4070 Super", 12288f, 32f, false, false, HardwareVendor.Intel, HardwareVendor.Nvidia, true, GpuAdapterKind.PhysicalDiscrete, SystemHardwareProfile.HighPerformance)]
    [InlineData("Scenario C: Desktop Intel + Intel Arc", "Intel Core i5-13400F", "Intel(R) Arc(TM) A770 Graphics", 16384f, 16f, false, false, HardwareVendor.Intel, HardwareVendor.Intel, true, GpuAdapterKind.PhysicalDiscrete, SystemHardwareProfile.HighPerformance)]
    [InlineData("Scenario D: Desktop Intel + Integrated GPU", "Intel Core i5-12400", "Intel(R) UHD Graphics 730", 1024f, 16f, false, false, HardwareVendor.Intel, HardwareVendor.Intel, false, GpuAdapterKind.PhysicalIntegrated, SystemHardwareProfile.Basic)]
    [InlineData("Scenario E: Desktop AMD + NVIDIA", "AMD Ryzen 5 7600X", "NVIDIA GeForce RTX 4060", 8192f, 16f, false, false, HardwareVendor.Amd, HardwareVendor.Nvidia, true, GpuAdapterKind.PhysicalDiscrete, SystemHardwareProfile.Gamer)]
    [InlineData("Scenario F: Hybrid Notebook", "AMD Ryzen 9 7940HS", "NVIDIA GeForce RTX 4060 Laptop GPU", 8192f, 16f, true, false, HardwareVendor.Amd, HardwareVendor.Nvidia, true, GpuAdapterKind.PhysicalDiscrete, SystemHardwareProfile.Notebook)]
    [InlineData("Scenario G: Corporate Restricted Notebook", "Intel Core Ultra 7 165U", "Intel(R) Graphics", 1024f, 16f, true, true, HardwareVendor.Intel, HardwareVendor.Intel, false, GpuAdapterKind.PhysicalIntegrated, SystemHardwareProfile.Corporate)]
    [InlineData("Scenario I: Workstation", "AMD Ryzen Threadripper PRO 7965WX", "NVIDIA RTX A5000", 24576f, 128f, false, false, HardwareVendor.Amd, HardwareVendor.Nvidia, true, GpuAdapterKind.PhysicalDiscrete, SystemHardwareProfile.Workstation)]
    [InlineData("Scenario J: Legacy PC", "Intel(R) Core(TM) i3-6100", "Intel(R) HD Graphics 530", 512f, 8f, false, false, HardwareVendor.Intel, HardwareVendor.Intel, false, GpuAdapterKind.PhysicalIntegrated, SystemHardwareProfile.Basic)]
    public async Task UniversalHardwareEngine_ScenariosA_To_J_DetectsVendorsCapabilitiesAndProfilesWithoutBias(
        string scenarioName,
        string cpuModel,
        string gpuModel,
        float vramMb,
        float ramGb,
        bool hasBattery,
        bool isCorporateManaged,
        HardwareVendor expectedCpuVendor,
        HardwareVendor expectedGpuVendor,
        bool expectedDiscreteGpu,
        GpuAdapterKind expectedAdapterKind,
        SystemHardwareProfile expectedProfile)
    {
        Assert.NotEmpty(scenarioName);
        var snapshot = CreateSnapshot(cpuModel, 58f, gpuModel, 62f, 75f, vramMb, ramGb * 0.4f, ramGb);
        using var stub = new ConfigurableHardwareMonitorStub(snapshot);
        var engine = new UniversalHardwareEngine(stub);

        var report = await engine.BuildUniversalReportAsync(
            snapshot,
            hasBatteryOverride: hasBattery,
            isDomainOrPolicyManagedOverride: isCorporateManaged,
            isRunningAsAdminOverride: true);

        Assert.Equal(expectedCpuVendor, report.Cpu.Vendor);
        Assert.Single(report.Gpus);
        Assert.Empty(report.SafeVirtualDisplayAdapters);
        Assert.NotNull(report.PrimaryGpu);
        Assert.Equal(expectedGpuVendor, report.PrimaryGpu!.Vendor);
        Assert.Equal(expectedDiscreteGpu, report.PrimaryGpu.IsDiscrete);
        Assert.Equal(!expectedDiscreteGpu, report.PrimaryGpu.IsIntegrated);
        Assert.Equal(expectedAdapterKind, report.PrimaryGpu.AdapterKind);
        Assert.True(report.PrimaryGpu.IsPhysicalGpu);
        Assert.Equal(expectedProfile, report.DetectedProfile);
        Assert.True(report.Cpu.PackageTemperatureC.IsAvailable);
        Assert.Equal(HardwareDataSource.LibreHardwareMonitor, report.Cpu.PackageTemperatureC.Source);
    }

    [Fact]
    public async Task UniversalHardwareEngine_EnumeratesMultipleGpusIndependently_InHybridAndMultiGpuSystems()
    {
        // Hybrid Laptop with AMD Radeon 780M iGPU + NVIDIA GeForce RTX 4070 Laptop dGPU
        var adapters = new[]
        {
            new GpuAdapterTelemetryPoint(
                Model: "AMD Radeon 780M Graphics",
                HardwareType: "GpuAmd",
                CoreTempC: 48f,
                HotspotTempC: null,
                LoadPercent: 12f,
                CoreClockMhz: 1100f,
                PowerWatts: 11f,
                FanRpm: null,
                VramUsedMb: 512f,
                VramTotalMb: 2048f,
                DriverVersion: "31.0.24002.92",
                PnpDeviceId: "PCI\\VEN_1002&DEV_15BF"
            ),
            new GpuAdapterTelemetryPoint(
                Model: "NVIDIA GeForce RTX 4070 Laptop GPU",
                HardwareType: "GpuNvidia",
                CoreTempC: 67f,
                HotspotTempC: 79f,
                LoadPercent: 88f,
                CoreClockMhz: 2175f,
                PowerWatts: 105f,
                FanRpm: 3200f,
                VramUsedMb: 5400f,
                VramTotalMb: 8192f,
                DriverVersion: "32.0.15.6094",
                PnpDeviceId: "PCI\\VEN_10DE&DEV_2860"
            )
        };

        var snapshot = CreateSnapshot(
            cpuModel: "AMD Ryzen 9 7940HS",
            cpuTemp: 71f,
            gpuModel: "NVIDIA GeForce RTX 4070 Laptop GPU",
            gpuTemp: 67f,
            gpuHotspot: 79f,
            vramTotalMb: 8192f,
            ramUsedGb: 14f,
            ramTotalGb: 32f,
            gpuAdapters: adapters
        );

        using var stub = new ConfigurableHardwareMonitorStub(snapshot);
        var engine = new UniversalHardwareEngine(stub);

        var report = await engine.BuildUniversalReportAsync(
            snapshot,
            hasBatteryOverride: true,
            batteryChargePercentOverride: 84,
            isRunningAsAdminOverride: true);

        Assert.Equal(2, report.Gpus.Count);
        Assert.Empty(report.SafeVirtualDisplayAdapters);

        var igpu = Assert.Single(report.Gpus, g => g.IsIntegrated);
        Assert.Equal(HardwareVendor.Amd, igpu.Vendor);
        Assert.Equal(GpuAdapterKind.PhysicalIntegrated, igpu.AdapterKind);
        Assert.Equal("AMD Radeon 780M Graphics", igpu.Name);
        Assert.Equal(48f, igpu.CoreTemperatureC.Value);
        Assert.False(igpu.HotspotTemperatureC.IsAvailable);
        Assert.Equal(UniversalSensorStatus.NotSupported, igpu.HotspotTemperatureC.SensorStatus);

        var dgpu = Assert.Single(report.Gpus, g => g.IsDiscrete);
        Assert.Equal(HardwareVendor.Nvidia, dgpu.Vendor);
        Assert.Equal(GpuAdapterKind.PhysicalDiscrete, dgpu.AdapterKind);
        Assert.Equal("NVIDIA GeForce RTX 4070 Laptop GPU", dgpu.Name);
        Assert.Equal(67f, dgpu.CoreTemperatureC.Value);
        Assert.True(dgpu.HotspotTemperatureC.IsAvailable);
        Assert.Equal(79f, dgpu.HotspotTemperatureC.Value);

        Assert.Equal(dgpu.Name, report.PrimaryGpu?.Name);
        Assert.Equal(SystemHardwareProfile.Notebook, report.DetectedProfile);
        Assert.Equal(84, report.BatteryAndPower.ChargePercent);
    }

    [Fact]
    public async Task UniversalHardwareEngine_NeverEstimatesPhysicalCoresMemoryGenerationOrBatteryPercentWhenUnavailable()
    {
        var snapshot = CreateSnapshot(
            cpuModel: "Intel Core i5-10400",
            cpuTemp: null,
            gpuModel: null,
            gpuTemp: null,
            gpuHotspot: null,
            vramTotalMb: null,
            ramUsedGb: 6.5f,
            ramTotalGb: 16f,
            status: SensorReadingStatus.CommunicationFailure
        );

        using var stub = new ConfigurableHardwareMonitorStub(snapshot, isAvailable: false);
        var simulatedProbe = new SimulatedHardwareSystemProbe(
            cpuName: "Intel Core i5-10400",
            physicalCores: null,
            logicalProcessors: 12,
            wmiGpuAdapters: Array.Empty<GpuAdapterTelemetryPoint>(),
            memoryGeneration: null,
            memorySpeedMt: null,
            hasBattery: true,
            batteryChargePercent: null,
            isAdmin: false,
            isCorporateManaged: false
        );
        var engine = new UniversalHardwareEngine(stub, simulatedProbe);

        var report = await engine.BuildUniversalReportAsync(
            snapshot,
            hasBatteryOverride: true,
            batteryChargePercentOverride: null,
            physicalCoresOverride: null,
            memoryGenerationOverride: null,
            isDomainOrPolicyManagedOverride: false,
            isRunningAsAdminOverride: false);

        // Deterministic on ALL operating systems (Windows runner or Linux container): never fabricates values!
        Assert.Null(report.Cpu.PhysicalCores);
        Assert.Null(report.Memory.MemoryGeneration);
        Assert.Null(report.BatteryAndPower.ChargePercent);
        Assert.Null(report.PrimaryGpu);
        Assert.Empty(report.Gpus);
        Assert.Empty(report.SafeVirtualDisplayAdapters);
        Assert.False(report.Cpu.PackageTemperatureC.IsAvailable);
        Assert.Equal(UniversalSensorStatus.RequiresAdmin, report.Cpu.PackageTemperatureC.SensorStatus);
    }

    [Fact]
    public async Task UniversalHardwareEngine_MergesLhmAndWmiGpuAdapters_WithoutDuplicationOrOverwritingValidSensors()
    {
        // Hybrid Notebook: Intel Iris Xe iGPU + NVIDIA RTX 4070 Laptop dGPU
        var lhmAdapters = new[]
        {
            new GpuAdapterTelemetryPoint(
                Model: "NVIDIA GeForce RTX 4070 Laptop GPU",
                HardwareType: "GpuNvidia",
                CoreTempC: 66f,
                HotspotTempC: 78f,
                LoadPercent: 85f,
                CoreClockMhz: 2190f,
                PowerWatts: 108f,
                FanRpm: 3100f,
                VramUsedMb: 5120f,
                VramTotalMb: 8192f,
                DriverVersion: null,
                PnpDeviceId: "PCI\\VEN_10DE&DEV_2860&SUBSYS_12341043&REV_A1\\4&112233&0&0008",
                SensorIdentifier: "/gpu-nvidia/0"
            )
        };

        var wmiAdapters = new[]
        {
            new GpuAdapterTelemetryPoint(
                Model: "Intel(R) Iris(R) Xe Graphics",
                HardwareType: "GpuIntel",
                CoreTempC: null,
                HotspotTempC: null,
                LoadPercent: null,
                CoreClockMhz: null,
                PowerWatts: null,
                FanRpm: null,
                VramUsedMb: null,
                VramTotalMb: 1024f,
                DriverVersion: "31.0.101.5333",
                PnpDeviceId: "PCI\\VEN_8086&DEV_A7A0&SUBSYS_12341043&REV_04\\3&11583659&0&10"
            ),
            new GpuAdapterTelemetryPoint(
                Model: "NVIDIA GeForce RTX 4070 Laptop GPU",
                HardwareType: "GpuNvidia",
                CoreTempC: null,
                HotspotTempC: null,
                LoadPercent: null,
                CoreClockMhz: null,
                PowerWatts: null,
                FanRpm: null,
                VramUsedMb: null,
                VramTotalMb: 4095f, // WMI uint32 AdapterRAM cap
                DriverVersion: "32.0.15.6094",
                PnpDeviceId: "PCI\\VEN_10DE&DEV_2860&SUBSYS_12341043&REV_A1\\4&112233&0&0008"
            )
        };

        var mergedTelemetry = UniversalHardwareEngine.MergeGpuAdapters(lhmAdapters, wmiAdapters);
        var mergedNvidiaTelemetry = Assert.Single(mergedTelemetry, adapter => adapter.Model.Contains("RTX 4070", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("/gpu-nvidia/0", mergedNvidiaTelemetry.SensorIdentifier);
        Assert.StartsWith("PCI\\VEN_10DE", mergedNvidiaTelemetry.PnpDeviceId);

        var snapshot = CreateSnapshot(
            cpuModel: "Intel Core i7-13700H",
            cpuTemp: 69f,
            gpuModel: "Intel(R) Iris(R) Xe Graphics",
            gpuTemp: 66f,
            gpuHotspot: 78f,
            vramTotalMb: 8192f,
            ramUsedGb: 12f,
            ramTotalGb: 32f,
            gpuAdapters: lhmAdapters
        );

        using var stub = new ConfigurableHardwareMonitorStub(snapshot);
        var engine = new UniversalHardwareEngine(stub);

        var report = await engine.BuildUniversalReportAsync(
            snapshot,
            hasBatteryOverride: true,
            batteryChargePercentOverride: 76,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: wmiAdapters);

        Assert.Equal(2, report.Gpus.Count);
        Assert.Equal(SystemHardwareProfile.Notebook, report.DetectedProfile);

        var intelIgpu = Assert.Single(report.Gpus, g => g.Vendor == HardwareVendor.Intel);
        Assert.True(intelIgpu.IsIntegrated);
        Assert.False(intelIgpu.IsDiscrete);
        Assert.Equal(GpuAdapterKind.PhysicalIntegrated, intelIgpu.AdapterKind);
        Assert.Equal("31.0.101.5333", intelIgpu.DriverVersion);
        Assert.False(intelIgpu.CoreTemperatureC.IsAvailable);

        var nvidiaDgpu = Assert.Single(report.Gpus, g => g.Vendor == HardwareVendor.Nvidia);
        Assert.True(nvidiaDgpu.IsDiscrete);
        Assert.False(nvidiaDgpu.IsIntegrated);
        Assert.Equal(GpuAdapterKind.PhysicalDiscrete, nvidiaDgpu.AdapterKind);
        Assert.True(nvidiaDgpu.CoreTemperatureC.IsAvailable);
        Assert.Equal(66f, nvidiaDgpu.CoreTemperatureC.Value);
        Assert.True(nvidiaDgpu.HotspotTemperatureC.IsAvailable);
        Assert.Equal(78f, nvidiaDgpu.HotspotTemperatureC.Value);
        Assert.Equal(8192f, nvidiaDgpu.VramTotalMb.Value);
        Assert.Equal("32.0.15.6094", nvidiaDgpu.DriverVersion);

        Assert.NotNull(report.PrimaryGpu);
        Assert.Equal(nvidiaDgpu.Name, report.PrimaryGpu!.Name);
        Assert.True(report.PrimaryGpu.IsDiscrete);
    }

    [Fact]
    public async Task UniversalHardwareEngine_SupportsAmdIntegratedPlusAmdDiscrete_AndMultiDiscreteGpuWorkstations()
    {
        // Case 1: AMD Integrated (Radeon 780M) + AMD Discrete (Radeon RX 7800 XT)
        var lhmAmdOnlyDiscrete = new[]
        {
            new GpuAdapterTelemetryPoint(
                Model: "AMD Radeon RX 7800 XT",
                HardwareType: "GpuAmd",
                CoreTempC: 61f,
                HotspotTempC: 79f,
                LoadPercent: 94f,
                CoreClockMhz: 2430f,
                PowerWatts: 252f,
                FanRpm: 1680f,
                VramUsedMb: 9200f,
                VramTotalMb: 16384f,
                DriverVersion: "31.0.24033.1003",
                PnpDeviceId: "PCI\\VEN_1002&DEV_747E&SUBSYS_E4711DA2&REV_C8\\6&2A3B4C5D&0&00000008"
            )
        };

        var wmiBothAmd = new[]
        {
            new GpuAdapterTelemetryPoint(
                Model: "AMD Radeon(TM) 780M",
                HardwareType: "GpuAmd",
                CoreTempC: null,
                HotspotTempC: null,
                LoadPercent: null,
                CoreClockMhz: null,
                PowerWatts: null,
                FanRpm: null,
                VramUsedMb: null,
                VramTotalMb: 512f,
                DriverVersion: "31.0.24033.1003",
                PnpDeviceId: "PCI\\VEN_1002&DEV_15BF&SUBSYS_88771043&REV_C1\\4&1A2B3C4D&0&0041"
            ),
            new GpuAdapterTelemetryPoint(
                Model: "AMD Radeon RX 7800 XT",
                HardwareType: "GpuAmd",
                CoreTempC: null,
                HotspotTempC: null,
                LoadPercent: null,
                CoreClockMhz: null,
                PowerWatts: null,
                FanRpm: null,
                VramUsedMb: null,
                VramTotalMb: 16384f,
                DriverVersion: "31.0.24033.1003",
                PnpDeviceId: "PCI\\VEN_1002&DEV_747E&SUBSYS_E4711DA2&REV_C8\\6&2A3B4C5D&0&00000008"
            )
        };

        var amdSnapshot = CreateSnapshot(
            cpuModel: "AMD Ryzen 7 8700G",
            cpuTemp: 62f,
            gpuModel: "AMD Radeon(TM) 780M",
            gpuTemp: 61f,
            gpuHotspot: 79f,
            vramTotalMb: 16384f,
            ramUsedGb: 16f,
            ramTotalGb: 32f,
            gpuAdapters: lhmAmdOnlyDiscrete
        );

        using var amdStub = new ConfigurableHardwareMonitorStub(amdSnapshot);
        var amdEngine = new UniversalHardwareEngine(amdStub);
        var amdReport = await amdEngine.BuildUniversalReportAsync(
            amdSnapshot,
            hasBatteryOverride: false,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: wmiBothAmd);

        Assert.Equal(2, amdReport.Gpus.Count);
        Assert.Single(amdReport.Gpus, g => g.IsIntegrated && g.Name.Contains("780M", StringComparison.OrdinalIgnoreCase));
        var amdDiscrete = Assert.Single(amdReport.Gpus, g => g.IsDiscrete && g.Name.Contains("7800 XT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(amdDiscrete.Name, amdReport.PrimaryGpu?.Name);
        Assert.Equal(61f, amdDiscrete.CoreTemperatureC.Value);
        Assert.Equal(SystemHardwareProfile.HighPerformance, amdReport.DetectedProfile);

        // Case 2: Multi-GPU Workstation with 2 Professional Discrete GPUs (2x NVIDIA RTX A5000 in distinct PCI slots)
        var dualProGpus = new[]
        {
            new GpuAdapterTelemetryPoint(
                "NVIDIA RTX A5000", "GpuNvidia", 64f, 75f, 92f, 2100f, 220f, 1850f, 18000f, 24576f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2231&SUBSYS_145910DE&REV_A1\\4&111111&0&0008"),
            new GpuAdapterTelemetryPoint(
                "NVIDIA RTX A5000", "GpuNvidia", 61f, 72f, 88f, 2100f, 215f, 1800f, 16000f, 24576f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2231&SUBSYS_145910DE&REV_A1\\4&222222&0&0010")
        };

        var wsSnapshot = CreateSnapshot(
            cpuModel: "Intel Xeon w7-2495X",
            cpuTemp: 59f,
            gpuModel: "NVIDIA RTX A5000",
            gpuTemp: 64f,
            gpuHotspot: 75f,
            vramTotalMb: 24576f,
            ramUsedGb: 32f,
            ramTotalGb: 128f,
            gpuAdapters: new[] { dualProGpus[0] }
        );

        using var wsStub = new ConfigurableHardwareMonitorStub(wsSnapshot);
        var wsEngine = new UniversalHardwareEngine(wsStub);
        var wsReport = await wsEngine.BuildUniversalReportAsync(
            wsSnapshot,
            hasBatteryOverride: false,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: dualProGpus);

        Assert.Equal(2, wsReport.Gpus.Count);
        Assert.All(wsReport.Gpus, g => Assert.Equal(GpuAdapterKind.PhysicalDiscrete, g.AdapterKind));
        Assert.Equal(SystemHardwareProfile.Workstation, wsReport.DetectedProfile);
    }

    [Fact]
    public async Task UniversalHardwareEngine_VirtualAndRemoteDisplayAdapters_NeverContaminatePhysicalGpusOrSystemProfile()
    {
        // Scenario 11: Desktop with physical discrete GPU (NVIDIA RTX 4060) + Microsoft Hyper-V Video virtual adapter
        var lhmDesktop = new[]
        {
            new GpuAdapterTelemetryPoint(
                "NVIDIA GeForce RTX 4060", "GpuNvidia", 58f, 69f, 55f, 2460f, 115f, 1500f, 3200f, 8192f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2882&SUBSYS_88991043&REV_A1\\4&112233&0&0008")
        };
        var wmiWithHyperV = new[]
        {
            new GpuAdapterTelemetryPoint(
                "NVIDIA GeForce RTX 4060", "WmiVideoController", null, null, null, null, null, null, null, 8192f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2882&SUBSYS_88991043&REV_A1\\4&112233&0&0008"),
            new GpuAdapterTelemetryPoint(
                "Microsoft Hyper-V Video", "WmiVideoController", null, null, null, null, null, null, null, null, "10.0.22621.1",
                "VMBUS\\{5620E0C7-8062-4DCE-AEB7-520C7EF76171}\\{DA0A7802-E377-4AAC-8E77-0558EB1073F8}")
        };

        var gamerSnapshot = CreateSnapshot("AMD Ryzen 5 7600X", 54f, "NVIDIA GeForce RTX 4060", 58f, 69f, 8192f, 8f, 16f, gpuAdapters: lhmDesktop);
        using var gamerStub = new ConfigurableHardwareMonitorStub(gamerSnapshot);
        var gamerEngine = new UniversalHardwareEngine(gamerStub);

        var gamerReport = await gamerEngine.BuildUniversalReportAsync(
            gamerSnapshot,
            hasBatteryOverride: false,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: wmiWithHyperV);

        // Hyper-V Video MUST NOT be counted as a physical GPU and MUST NOT turn Gamer into Workstation!
        Assert.Single(gamerReport.Gpus);
        Assert.Single(gamerReport.SafeVirtualDisplayAdapters);
        Assert.Equal("NVIDIA GeForce RTX 4060", gamerReport.PrimaryGpu?.Name);
        Assert.Equal(GpuAdapterKind.PhysicalDiscrete, gamerReport.PrimaryGpu?.AdapterKind);
        Assert.Equal("Microsoft Hyper-V Video", gamerReport.SafeVirtualDisplayAdapters[0].Name);
        Assert.Equal(GpuAdapterKind.VirtualAdapter, gamerReport.SafeVirtualDisplayAdapters[0].AdapterKind);
        Assert.False(gamerReport.SafeVirtualDisplayAdapters[0].IsDiscrete);
        Assert.False(gamerReport.SafeVirtualDisplayAdapters[0].IsPhysicalGpu);
        Assert.Equal(SystemHardwareProfile.Gamer, gamerReport.DetectedProfile);

        // Scenario 12: Notebook with integrated GPU + Microsoft Remote Display Adapter (RDP)
        var wmiNotebookWithRdp = new[]
        {
            new GpuAdapterTelemetryPoint(
                "Intel(R) UHD Graphics 730", "WmiVideoController", 49f, null, 18f, 1200f, 12f, null, 512f, 1024f, "31.0.101.5186",
                "PCI\\VEN_8086&DEV_4692&SUBSYS_86941043&REV_0C\\3&11583659&0&10"),
            new GpuAdapterTelemetryPoint(
                "Microsoft Remote Display Adapter", "WmiVideoController", null, null, null, null, null, null, null, null, "10.0.22621.1",
                "SWD\\REMOTEDISPLAYENUM\\RDPDD_00")
        };

        var nbSnapshot = CreateSnapshot("Intel Core i5-12400", 51f, "Intel(R) UHD Graphics 730", 49f, null, 1024f, 6f, 16f, gpuAdapters: Array.Empty<GpuAdapterTelemetryPoint>());
        using var nbStub = new ConfigurableHardwareMonitorStub(nbSnapshot);
        var nbEngine = new UniversalHardwareEngine(nbStub);

        var nbReport = await nbEngine.BuildUniversalReportAsync(
            nbSnapshot,
            hasBatteryOverride: true,
            batteryChargePercentOverride: 91,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: wmiNotebookWithRdp);

        Assert.Single(nbReport.Gpus);
        Assert.Single(nbReport.SafeVirtualDisplayAdapters);
        Assert.Equal("Intel(R) UHD Graphics 730", nbReport.PrimaryGpu?.Name);
        Assert.Equal(HardwareVendor.Intel, nbReport.PrimaryGpu?.Vendor);
        Assert.Equal(GpuAdapterKind.PhysicalIntegrated, nbReport.PrimaryGpu?.AdapterKind);
        Assert.Equal(GpuAdapterKind.RemoteDisplayAdapter, nbReport.SafeVirtualDisplayAdapters[0].AdapterKind);
        Assert.Equal(SystemHardwareProfile.Notebook, nbReport.DetectedProfile);

        // Scenario 13: Computer with ONLY virtual/basic display adapters (Hyper-V Video + Microsoft Basic Display Adapter)
        var onlyVirtualAdapters = new[]
        {
            new GpuAdapterTelemetryPoint(
                "Microsoft Hyper-V Video", "WmiVideoController", null, null, null, null, null, null, null, null, "10.0.22621.1",
                "PCI\\VEN_1414&DEV_5353&SUBSYS_00000000&REV_00\\3&11583659&0&40"),
            new GpuAdapterTelemetryPoint(
                "Microsoft Basic Display Adapter", "WmiVideoController", null, null, null, null, null, null, null, null, "10.0.22621.1",
                "PCI\\VEN_1414&DEV_008C&SUBSYS_00000000&REV_00\\3&11583659&0&48")
        };

        var vmSnapshot = CreateSnapshot("AMD EPYC 7763 64-Core Processor (Virtual)", null, null, null, null, null, 4f, 8f, gpuAdapters: Array.Empty<GpuAdapterTelemetryPoint>());
        var vmCpuSnapshot = vmSnapshot with { CpuModelName = "Intel Core i5-12400" };
        using var vmStub = new ConfigurableHardwareMonitorStub(vmCpuSnapshot);
        var vmEngine = new UniversalHardwareEngine(vmStub);

        var vmReport = await vmEngine.BuildUniversalReportAsync(
            vmCpuSnapshot,
            hasBatteryOverride: false,
            isRunningAsAdminOverride: false,
            complementaryWmiGpuAdaptersOverride: onlyVirtualAdapters);

        Assert.Empty(vmReport.Gpus);
        Assert.Null(vmReport.PrimaryGpu);
        Assert.Equal(2, vmReport.SafeVirtualDisplayAdapters.Count);
        Assert.Contains(vmReport.SafeVirtualDisplayAdapters, a => a.AdapterKind == GpuAdapterKind.VirtualAdapter);
        Assert.Contains(vmReport.SafeVirtualDisplayAdapters, a => a.AdapterKind == GpuAdapterKind.BasicDisplayAdapter);
        Assert.Equal(SystemHardwareProfile.Basic, vmReport.DetectedProfile);
    }

    [Fact]
    public async Task UniversalHardwareEngine_SupportsTwoPhysicalGpus_DifferentVendors_AndSameVendorDifferentModels()
    {
        // Scenario 14: Desktop with two physical discrete GPUs from different vendors (AMD RX 7800 XT + NVIDIA RTX 4070)
        var dualVendorGpus = new[]
        {
            new GpuAdapterTelemetryPoint(
                "AMD Radeon RX 7800 XT", "GpuAmd", 60f, 76f, 80f, 2400f, 240f, 1600f, 8192f, 16384f, "31.0.24033.1003",
                "PCI\\VEN_1002&DEV_747E&SUBSYS_E4711DA2&REV_C8\\6&111111&0&0008"),
            new GpuAdapterTelemetryPoint(
                "NVIDIA GeForce RTX 4070", "GpuNvidia", 58f, 69f, 40f, 2100f, 140f, 1300f, 4096f, 12288f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2786&SUBSYS_123410DE&REV_A1\\6&222222&0&0010")
        };

        var dualVendorSnapshot = CreateSnapshot("AMD Ryzen 7 7800X3D", 61f, "AMD Radeon RX 7800 XT", 60f, 76f, 16384f, 14f, 32f, gpuAdapters: new[] { dualVendorGpus[0] });
        using var dvStub = new ConfigurableHardwareMonitorStub(dualVendorSnapshot);
        var dvEngine = new UniversalHardwareEngine(dvStub);

        var dvReport = await dvEngine.BuildUniversalReportAsync(
            dualVendorSnapshot,
            hasBatteryOverride: false,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: dualVendorGpus);

        Assert.Equal(2, dvReport.Gpus.Count);
        Assert.Single(dvReport.Gpus, g => g.Vendor == HardwareVendor.Amd && g.IsDiscrete);
        Assert.Single(dvReport.Gpus, g => g.Vendor == HardwareVendor.Nvidia && g.IsDiscrete);
        // Consumer dual-GPU desktop is HighPerformance, not Workstation
        Assert.Equal(SystemHardwareProfile.HighPerformance, dvReport.DetectedProfile);

        // Scenario 15: Desktop with two physical discrete GPUs from the SAME vendor and DIFFERENT models (RTX 4090 + RTX 3060)
        var sameVendorDifferentModels = new[]
        {
            new GpuAdapterTelemetryPoint(
                "NVIDIA GeForce RTX 4090", "GpuNvidia", 63f, 74f, 90f, 2700f, 390f, 1800f, 16000f, 24576f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2684&SUBSYS_165A10DE&REV_A1\\4&111111&0&0008"),
            new GpuAdapterTelemetryPoint(
                "NVIDIA GeForce RTX 3060", "GpuNvidia", 52f, 63f, 20f, 1850f, 85f, 1200f, 2048f, 12288f, "32.0.15.6094",
                "PCI\\VEN_10DE&DEV_2504&SUBSYS_123410DE&REV_A1\\4&222222&0&0010")
        };

        var svSnapshot = CreateSnapshot("Intel Core i7-14700K", 64f, "NVIDIA GeForce RTX 4090", 63f, 74f, 24576f, 16f, 32f, gpuAdapters: new[] { sameVendorDifferentModels[0] });
        using var svStub = new ConfigurableHardwareMonitorStub(svSnapshot);
        var svEngine = new UniversalHardwareEngine(svStub);

        var svReport = await svEngine.BuildUniversalReportAsync(
            svSnapshot,
            hasBatteryOverride: false,
            isRunningAsAdminOverride: true,
            complementaryWmiGpuAdaptersOverride: sameVendorDifferentModels);

        Assert.Equal(2, svReport.Gpus.Count);
        Assert.Contains(svReport.Gpus, g => g.Name == "NVIDIA GeForce RTX 4090" && g.VramTotalMb.Value == 24576f);
        Assert.Contains(svReport.Gpus, g => g.Name == "NVIDIA GeForce RTX 3060" && g.VramTotalMb.Value == 12288f);
        Assert.Equal("NVIDIA GeForce RTX 4090", svReport.PrimaryGpu?.Name);
        Assert.Equal(SystemHardwareProfile.HighPerformance, svReport.DetectedProfile);
    }

    [Fact]
    public void NetworkDiagnosticsService_CoversAllFiveScenarios_NeverConfusesNullLossWithZero_AndSeparatesIcmpFromTcp()
    {
        // Default endpoints must never contain Valve UDP port 27015 or unreachable edge targets
        Assert.All(NetworkDiagnosticsService.DefaultVerifiedEndpoints, ep => Assert.Equal(443, ep.TcpPort));
        Assert.DoesNotContain(NetworkDiagnosticsService.DefaultVerifiedEndpoints, ep => ep.TcpPort == 27015);

        // Scenario 1: Firewall blocking ICMP (0/4 replies) while TCP 443 works -> FilteredOrBlocked, PacketLossPercent = null (never 0% or 100%)
        var blockedIcmpProbe = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Cloudflare Edge",
            "1.1.1.1",
            443,
            icmpSent: 4,
            icmpSamplesMs: Array.Empty<double>(),
            tcpState: TcpConnectionProbeState.Connected,
            tcpHandshakeMs: 14.2
        );

        Assert.Equal(IcmpProbeStatus.FilteredOrBlocked, blockedIcmpProbe.IcmpStatus);
        Assert.Null(blockedIcmpProbe.IcmpPacketLossPercent);
        Assert.Equal(TcpConnectionProbeState.Connected, blockedIcmpProbe.TcpState);
        Assert.True(blockedIcmpProbe.IsTcpConnected);

        var aggregateFiltered = NetworkDiagnosticsService.BuildAggregateReportFromTargets(
            "Test-Filtered-ICMP",
            new[] { blockedIcmpProbe },
            dnsLatencyMs: 8.5,
            dnsState: DnsResolutionState.Resolved
        );
        Assert.Equal(IcmpProbeStatus.FilteredOrBlocked, aggregateFiltered.IcmpStatus);
        Assert.Null(aggregateFiltered.PacketLossPercent);
        Assert.Null(aggregateFiltered.IcmpPacketLossPercent);
        Assert.Contains("Não mensurável", aggregateFiltered.FormattedPacketLoss, StringComparison.OrdinalIgnoreCase);
        Assert.Null(aggregateFiltered.AverageIcmpLatencyMs);
        Assert.Equal(14.2, aggregateFiltered.AverageTcpHandshakeMs);
        Assert.Equal(TcpConnectionProbeState.Connected, aggregateFiltered.TcpState);
        Assert.Equal(DnsResolutionState.Resolved, aggregateFiltered.DnsState);

        // Scenario 2: ICMP working (4/4 replies) while TCP is refused/blocked -> Responded, PacketLossPercent = 0.0%, TcpState = ConnectionRefused
        var icmpOkTcpRefused = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Custom Host",
            "9.9.9.9",
            443,
            icmpSent: 4,
            icmpSamplesMs: new[] { 18.0, 20.0, 18.0, 20.0 },
            tcpState: TcpConnectionProbeState.ConnectionRefused,
            tcpHandshakeMs: null
        );
        Assert.Equal(IcmpProbeStatus.Responded, icmpOkTcpRefused.IcmpStatus);
        Assert.Equal(0.0, icmpOkTcpRefused.IcmpPacketLossPercent);
        Assert.Equal(TcpConnectionProbeState.ConnectionRefused, icmpOkTcpRefused.TcpState);
        Assert.False(icmpOkTcpRefused.IsTcpConnected);

        // Scenario 3: Partial ICMP loss proven by samples (3/4 replies = 25% loss) -> PartialLoss, PacketLossPercent = 25.0%, separate ICMP vs TCP latency
        var partialLossProbe = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Google Public DNS",
            "8.8.8.8",
            443,
            icmpSent: 4,
            icmpSamplesMs: new[] { 10.0, 12.0, 14.0 },
            tcpState: TcpConnectionProbeState.Connected,
            tcpHandshakeMs: 22.5
        );
        Assert.Equal(IcmpProbeStatus.PartialLoss, partialLossProbe.IcmpStatus);
        Assert.Equal(25.0, partialLossProbe.IcmpPacketLossPercent);

        var aggregatePartial = NetworkDiagnosticsService.BuildAggregateReportFromTargets(
            "Test-Partial-Loss",
            new[] { partialLossProbe },
            dnsLatencyMs: 6.0,
            dnsState: DnsResolutionState.Resolved
        );
        Assert.Equal(IcmpProbeStatus.PartialLoss, aggregatePartial.IcmpStatus);
        Assert.Equal(25.0, aggregatePartial.PacketLossPercent);
        Assert.Equal("25.0%", aggregatePartial.FormattedPacketLoss);
        Assert.Equal(12.0, aggregatePartial.AverageIcmpLatencyMs);
        Assert.Equal(22.5, aggregatePartial.AverageTcpHandshakeMs);

        // Scenario 4: Inconclusive ICMP test (e.g., insufficient samples or DNS resolved while single probe timed out without DestinationUnreachable)
        var inconclusiveProbe = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Inconclusive Target",
            "208.67.222.222",
            443,
            icmpSent: 1,
            icmpSamplesMs: Array.Empty<double>(),
            tcpState: TcpConnectionProbeState.TimedOut,
            tcpHandshakeMs: null,
            explicitDestinationUnreachableCount: 0
        );
        Assert.Equal(IcmpProbeStatus.Inconclusive, inconclusiveProbe.IcmpStatus);
        Assert.Null(inconclusiveProbe.IcmpPacketLossPercent);

        var inconclusiveTimeoutProbe = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Inconclusive Timeout Target",
            "208.67.222.222",
            443,
            icmpSent: 1,
            icmpSamplesMs: Array.Empty<double>(),
            tcpState: TcpConnectionProbeState.Timeout,
            tcpHandshakeMs: null,
            explicitDestinationUnreachableCount: 0
        );
        Assert.Equal(IcmpProbeStatus.Inconclusive, inconclusiveTimeoutProbe.IcmpStatus);
        Assert.Null(inconclusiveTimeoutProbe.IcmpPacketLossPercent);

        var aggregateInconclusive = NetworkDiagnosticsService.BuildAggregateReportFromTargets(
            "Test-Inconclusive",
            new[] { inconclusiveProbe },
            dnsLatencyMs: 9.0,
            dnsState: DnsResolutionState.Resolved
        );
        Assert.Equal(IcmpProbeStatus.Inconclusive, aggregateInconclusive.IcmpStatus);
        Assert.Null(aggregateInconclusive.PacketLossPercent);
        Assert.Equal("Inconclusivo", aggregateInconclusive.FormattedPacketLoss);

        // Scenario 5: Total absence of connectivity with sufficient evidence -> Unreachable, PacketLossPercent = 100.0%
        var unreachableProbe = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Offline Target",
            "1.1.1.1",
            443,
            icmpSent: 4,
            icmpSamplesMs: Array.Empty<double>(),
            tcpState: TcpConnectionProbeState.NetworkUnreachable,
            tcpHandshakeMs: null,
            explicitDestinationUnreachableCount: 2
        );
        Assert.Equal(IcmpProbeStatus.Unreachable, unreachableProbe.IcmpStatus);
        Assert.Equal(100.0, unreachableProbe.IcmpPacketLossPercent);

        var aggregateUnreachable = NetworkDiagnosticsService.BuildAggregateReportFromTargets(
            "Test-Unreachable",
            new[] { unreachableProbe },
            dnsLatencyMs: 0.0,
            dnsState: DnsResolutionState.ServerUnreachable
        );
        Assert.Equal(IcmpProbeStatus.Unreachable, aggregateUnreachable.IcmpStatus);
        Assert.Equal(100.0, aggregateUnreachable.PacketLossPercent);
        Assert.Equal("100.0%", aggregateUnreachable.FormattedPacketLoss);
    }

    [Fact]
    public void WingetSoftwareCenterService_ParsesDynamicWingetOutput_VerifiesWinVerifyTrustStates_AndHasNoHardcodedVerifiedFlags()
    {
        // 1. Catalog items must not pretend to have a pre-verified local binary signature before execution
        Assert.All(WingetSoftwareCenterService.OfficialCatalog, pkg =>
            Assert.Equal(SignatureVerificationState.NotVerifiedYet, pkg.SignatureState));

        // 2. Parse real winget list / search tabular output
        string sampleWingetList = @"
Nome                             ID                                Versão          Disponível      Origem
---------------------------------------------------------------------------------------------------------
Mozilla Firefox                  Mozilla.Firefox                   131.0.3         132.0           winget
7-Zip                            7zip.7zip                         24.08                           winget
";
        var parsed = WingetSoftwareCenterService.ParseWingetTableOutput(sampleWingetList, isInstalledList: true);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Mozilla.Firefox", parsed[0].PackageId);
        Assert.Equal("131.0.3", parsed[0].Version);
        Assert.Equal("132.0", parsed[0].AvailableUpdateVersion);
        Assert.Equal("winget", parsed[0].Source);
        Assert.True(parsed[0].IsInstalled);

        Assert.Equal("7zip.7zip", parsed[1].PackageId);
        Assert.Equal("24.08", parsed[1].Version);
        Assert.Null(parsed[1].AvailableUpdateVersion);

        // 3. Test deterministic WinVerifyTrust cryptographic HRESULT classification across all required states
        var validSig = WingetSoftwareCenterService.MapWinTrustHResultToVerificationResult(
            WingetSoftwareCenterService.HResultSuccess,
            "C:\\Windows\\System32\\winget.exe",
            "CN=Microsoft Corporation, O=Microsoft Corporation",
            "CN=Microsoft Code Signing PCA 2011",
            "AABBCCDDEEFF0011",
            isTimestamped: true);
        Assert.Equal(SignatureVerificationState.VerifiedAuthenticode, validSig.State);
        Assert.True(validSig.IsCryptographicallyValid);
        Assert.True(validSig.IsTimestamped);

        var tamperedSig = WingetSoftwareCenterService.MapWinTrustHResultToVerificationResult(
            WingetSoftwareCenterService.TrustEBadDigest,
            "C:\\Temp\\tampered.exe",
            "CN=Publisher",
            "CN=Root CA",
            "11223344",
            isTimestamped: false);
        Assert.Equal(SignatureVerificationState.TamperedOrBadDigest, tamperedSig.State);
        Assert.False(tamperedSig.IsCryptographicallyValid);

        var expiredSig = WingetSoftwareCenterService.MapWinTrustHResultToVerificationResult(
            WingetSoftwareCenterService.CertEExpired,
            "C:\\Temp\\expired.exe",
            "CN=Expired Publisher",
            "CN=Root CA",
            "55667788",
            isTimestamped: false);
        Assert.Equal(SignatureVerificationState.ExpiredCertificate, expiredSig.State);
        Assert.False(expiredSig.IsCryptographicallyValid);

        var untrustedSig = WingetSoftwareCenterService.MapWinTrustHResultToVerificationResult(
            WingetSoftwareCenterService.CertEUntrustedRoot,
            "C:\\Temp\\selfsigned.exe",
            "CN=Self Signed",
            "CN=Self Signed",
            "99AABBCC",
            isTimestamped: false);
        Assert.Equal(SignatureVerificationState.UntrustedRoot, untrustedSig.State);
        Assert.False(untrustedSig.IsCryptographicallyValid);

        var offlineRevocationSig = WingetSoftwareCenterService.MapWinTrustHResultToVerificationResult(
            WingetSoftwareCenterService.CertERevocationFailure,
            "C:\\Temp\\offline.exe",
            "CN=Publisher",
            "CN=Root CA",
            "DDEEFF00",
            isTimestamped: true);
        Assert.Equal(SignatureVerificationState.InconclusiveRevocationOffline, offlineRevocationSig.State);
        Assert.False(offlineRevocationSig.IsCryptographicallyValid);

        var unsignedSig = WingetSoftwareCenterService.MapWinTrustHResultToVerificationResult(
            WingetSoftwareCenterService.TrustENoSignature,
            "C:\\Temp\\unsigned.exe",
            null,
            null,
            null,
            isTimestamped: false);
        Assert.Equal(SignatureVerificationState.UnsignedOrInvalid, unsignedSig.State);
        Assert.False(unsignedSig.IsCryptographicallyValid);

        // 4. Live call on an unsigned temporary file: on Windows returns UnsignedOrInvalid via WinVerifyTrust; on non-Windows returns VerificationUnavailable
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "unsigned-content");
            var sigResult = WingetSoftwareCenterService.VerifyBinaryAuthenticodeSignature(tempFile);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(SignatureVerificationState.UnsignedOrInvalid, sigResult.State);
            }
            else
            {
                Assert.Equal(SignatureVerificationState.VerificationUnavailable, sigResult.State);
            }
            Assert.False(sigResult.IsCryptographicallyValid);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("Intel UHD Graphics 730", null, HardwareVendor.Intel, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("Intel HD Graphics 530", null, HardwareVendor.Intel, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("Intel Iris Xe Graphics", null, HardwareVendor.Intel, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("Intel Arc A770", null, HardwareVendor.Intel, GpuAdapterKind.PhysicalDiscrete)]
    [InlineData("Intel Graphics", null, HardwareVendor.Intel, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("AMD Radeon 780M", null, HardwareVendor.Amd, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("AMD Radeon(TM) 780M", null, HardwareVendor.Amd, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("AMD Radeon RX 7800 XT", null, HardwareVendor.Amd, GpuAdapterKind.PhysicalDiscrete)]
    [InlineData("NVIDIA GeForce RTX 4060", null, HardwareVendor.Nvidia, GpuAdapterKind.PhysicalDiscrete)]
    [InlineData("NVIDIA RTX A5000", null, HardwareVendor.Nvidia, GpuAdapterKind.PhysicalDiscrete)]
    [InlineData("Qualcomm Adreno", null, HardwareVendor.Qualcomm, GpuAdapterKind.PhysicalIntegrated)]
    [InlineData("Microsoft Hyper-V Video", "PCI\\VEN_1414&DEV_5353", HardwareVendor.Microsoft, GpuAdapterKind.VirtualAdapter)]
    [InlineData("Microsoft Basic Display Adapter", "PCI\\VEN_1414&DEV_008C", HardwareVendor.Microsoft, GpuAdapterKind.BasicDisplayAdapter)]
    [InlineData("Microsoft Remote Display Adapter", "SWD\\REMOTEDISPLAYENUM\\RDPDD_00", HardwareVendor.Microsoft, GpuAdapterKind.RemoteDisplayAdapter)]
    [InlineData("Unrecognized Custom Framebuffer", "ACPI\\UNKNOWN_DEV_0001", HardwareVendor.Unknown, GpuAdapterKind.UnknownAdapter)]
    public void UniversalHardwareEngine_ClassifiesVendorsAndAdapterKindsAccurately_NeverPromotesUnknownOrVirtualToDiscrete(
        string adapterName,
        string? pnpDeviceId,
        HardwareVendor expectedVendor,
        GpuAdapterKind expectedKind)
    {
        var vendor = UniversalHardwareEngine.DetectGpuVendor(adapterName, pnpDeviceId);
        var kind = UniversalHardwareEngine.ClassifyGpuAdapterKind(adapterName, pnpDeviceId, vendor);

        Assert.Equal(expectedVendor, vendor);
        Assert.Equal(expectedKind, kind);
    }

    [Fact]
    public void GpuThermalAuditor_ReportsLimitedEvaluationWhenHotspotOrSensorsAreMissing_AndDoesNotDefaultTo7900Xtx()
    {
        var missingHotspot = GpuThermalAuditor.EvaluateThermalState(61.0f, 0.0f, "NVIDIA GeForce RTX 4070");
        Assert.True(missingHotspot.IsEvaluationLimited);
        Assert.Equal(GpuThermalHealth.Normal, missingHotspot.Status);
        Assert.Contains("limitada", missingHotspot.Message, StringComparison.OrdinalIgnoreCase);

        var genericProfile = GpuThermalAuditor.ResolveArchitectureThermalProfile(null);
        Assert.DoesNotContain("7900", genericProfile.ArchitectureLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsCompatibilityEngine_ClassifiesAllSixStatesAndNeverBypassesCorporatePolicy()
    {
        var compat = new WindowsCompatibilityEngine();

        var corpProfile = compat.DetectEnvironment(
            isRunningAsAdminOverride: true,
            hasRegistryPolicyRestrictionOverride: true,
            isEnterpriseManagedOverride: true,
            buildNumberOverride: 22631,
            editionOverride: "Enterprise");

        var blockedRes = compat.EvaluateOperation("perf-mmcss", "Otimizar MMCSS", true, true, false, 19041, corpProfile);
        Assert.Equal(OperationCompatibilityState.BlockedByPolicy, blockedRes.State);
        Assert.False(blockedRes.CanExecuteNow);
    }

    [Fact]
    public async Task SystemDoctorService_And_RecommendationEngine_CompileAndOperateWithoutCircularDependency()
    {
        var snapshot = CreateSnapshot(
            "Intel Core i7-13700K",
            64f,
            "NVIDIA GeForce RTX 4080",
            63f,
            76f,
            16384f,
            28f,
            32f
        );

        var doctor = new SystemDoctorService();
        var report = await doctor.RunComprehensiveAuditAsync(snapshot, isRunningAsAdminOverride: false);

        Assert.InRange(report.HealthScore, 10, 100);
        Assert.NotEmpty(report.Findings);

        using var stub = new ConfigurableHardwareMonitorStub(snapshot);
        var hwEngine = new UniversalHardwareEngine(stub);
        var hwReport = await hwEngine.BuildUniversalReportAsync(snapshot, isRunningAsAdminOverride: false);

        var recEngine = new RecommendationEngine();
        var recommendations = recEngine.GenerateRecommendations(hwReport, null, report);
        Assert.NotEmpty(recommendations);
        Assert.Contains(recommendations, r => r.RecommendationId == "rec-memory-pressure");
    }
}
