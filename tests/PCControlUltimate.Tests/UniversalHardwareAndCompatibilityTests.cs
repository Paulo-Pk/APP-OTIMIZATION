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
    [InlineData("Scenario A: Desktop AMD + Radeon", "AMD Ryzen 7 7800X3D", "AMD Radeon RX 7800 XT", 16384f, 32f, false, false, HardwareVendor.Amd, HardwareVendor.Amd, true, SystemHardwareProfile.HighPerformance)]
    [InlineData("Scenario B: Desktop Intel + NVIDIA", "Intel Core i7-14700K", "NVIDIA GeForce RTX 4070 Super", 12288f, 32f, false, false, HardwareVendor.Intel, HardwareVendor.Nvidia, true, SystemHardwareProfile.HighPerformance)]
    [InlineData("Scenario C: Desktop Intel + Intel Arc", "Intel Core i5-13400F", "Intel(R) Arc(TM) A770 Graphics", 16384f, 16f, false, false, HardwareVendor.Intel, HardwareVendor.Intel, true, SystemHardwareProfile.HighPerformance)]
    [InlineData("Scenario D: Desktop Intel + Integrated GPU", "Intel Core i5-12400", "Intel(R) UHD Graphics 730", 1024f, 16f, false, false, HardwareVendor.Intel, HardwareVendor.Intel, false, SystemHardwareProfile.Basic)]
    [InlineData("Scenario E: Desktop AMD + NVIDIA", "AMD Ryzen 5 7600X", "NVIDIA GeForce RTX 4060", 8192f, 16f, false, false, HardwareVendor.Amd, HardwareVendor.Nvidia, true, SystemHardwareProfile.Gamer)]
    [InlineData("Scenario F: Hybrid Notebook", "AMD Ryzen 9 7940HS", "NVIDIA GeForce RTX 4060 Laptop GPU", 8192f, 16f, true, false, HardwareVendor.Amd, HardwareVendor.Nvidia, true, SystemHardwareProfile.Notebook)]
    [InlineData("Scenario G: Corporate Restricted Notebook", "Intel Core Ultra 7 165U", "Intel(R) Graphics", 1024f, 16f, true, true, HardwareVendor.Intel, HardwareVendor.Intel, false, SystemHardwareProfile.Corporate)]
    [InlineData("Scenario I: Workstation", "AMD Ryzen Threadripper PRO 7965WX", "NVIDIA RTX A5000", 24576f, 128f, false, false, HardwareVendor.Amd, HardwareVendor.Nvidia, true, SystemHardwareProfile.Workstation)]
    [InlineData("Scenario J: Legacy PC", "Intel(R) Core(TM) i3-6100", "Intel(R) HD Graphics 530", 512f, 8f, false, false, HardwareVendor.Intel, HardwareVendor.Intel, false, SystemHardwareProfile.Basic)]
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
        Assert.NotNull(report.PrimaryGpu);
        Assert.Equal(expectedGpuVendor, report.PrimaryGpu!.Vendor);
        Assert.Equal(expectedDiscreteGpu, report.PrimaryGpu.IsDiscrete);
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

        var igpu = Assert.Single(report.Gpus, g => g.IsIntegrated);
        Assert.Equal(HardwareVendor.Amd, igpu.Vendor);
        Assert.Equal("AMD Radeon 780M Graphics", igpu.Name);
        Assert.Equal(48f, igpu.CoreTemperatureC.Value);
        Assert.False(igpu.HotspotTemperatureC.IsAvailable);
        Assert.Equal(UniversalSensorStatus.NotSupported, igpu.HotspotTemperatureC.SensorStatus);

        var dgpu = Assert.Single(report.Gpus, g => g.IsDiscrete);
        Assert.Equal(HardwareVendor.Nvidia, dgpu.Vendor);
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
        var engine = new UniversalHardwareEngine(stub);

        var report = await engine.BuildUniversalReportAsync(
            snapshot,
            hasBatteryOverride: true,
            batteryChargePercentOverride: null,
            physicalCoresOverride: null,
            memoryGenerationOverride: null,
            isDomainOrPolicyManagedOverride: false,
            isRunningAsAdminOverride: false);

        // When WMI/SMBIOS/Battery readings are unavailable in a non-Windows test session, values must be null, never estimated
        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(report.Cpu.PhysicalCores);
            Assert.Null(report.Memory.MemoryGeneration);
        }
        Assert.Null(report.BatteryAndPower.ChargePercent);
        Assert.Null(report.PrimaryGpu);
        Assert.Empty(report.Gpus);
        Assert.False(report.Cpu.PackageTemperatureC.IsAvailable);
        Assert.Equal(UniversalSensorStatus.RequiresAdmin, report.Cpu.PackageTemperatureC.SensorStatus);
    }

    [Fact]
    public void NetworkDiagnosticsService_SeparatesIcmpLossFromTcpStates_AndEliminates50PercentArtificialLossBug()
    {
        // 1. Default endpoints must never contain Valve UDP port 27015 or unreachable edge targets
        Assert.All(NetworkDiagnosticsService.DefaultVerifiedEndpoints, ep => Assert.Equal(443, ep.TcpPort));
        Assert.DoesNotContain(NetworkDiagnosticsService.DefaultVerifiedEndpoints, ep => ep.TcpPort == 27015);

        // 2. When ICMP is blocked (0 replies) but TCP 443 connects, ICMP status must be FilteredOrBlocked and IcmpPacketLossPercent must be null (inconclusive), never 0% or 100% from TCP!
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

        // 3. Aggregate report with blocked ICMP and connected TCP keeps IcmpStatus = FilteredOrBlocked and IcmpPacketLossPercent = null
        var aggregateFiltered = NetworkDiagnosticsService.BuildAggregateReportFromTargets(
            "Test-Filtered-ICMP",
            new[] { blockedIcmpProbe },
            dnsLatencyMs: 8.5,
            dnsState: DnsResolutionState.Resolved
        );
        Assert.Equal(IcmpProbeStatus.FilteredOrBlocked, aggregateFiltered.IcmpStatus);
        Assert.Null(aggregateFiltered.IcmpPacketLossPercent);
        Assert.Equal(TcpConnectionProbeState.Connected, aggregateFiltered.TcpState);
        Assert.Equal(DnsResolutionState.Resolved, aggregateFiltered.DnsState);

        // 4. When ICMP responds (4/4) and TCP refuses connection (ConnectionRefused), ICMP loss is 0% while TCP state is ConnectionRefused
        var icmpOkTcpRefused = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Custom Host",
            "9.9.9.9",
            443,
            icmpSent: 4,
            icmpSamplesMs: new[] { 18.0, 19.0, 18.5, 19.2 },
            tcpState: TcpConnectionProbeState.ConnectionRefused,
            tcpHandshakeMs: null
        );
        Assert.Equal(IcmpProbeStatus.Responded, icmpOkTcpRefused.IcmpStatus);
        Assert.Equal(0.0, icmpOkTcpRefused.IcmpPacketLossPercent);
        Assert.Equal(TcpConnectionProbeState.ConnectionRefused, icmpOkTcpRefused.TcpState);
        Assert.False(icmpOkTcpRefused.IsTcpConnected);
    }

    [Fact]
    public void WingetSoftwareCenterService_ParsesDynamicWingetOutput_VerifiesRealSignatures_AndHasNoHardcodedVerifiedFlags()
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

        // 3. Authenticode verification on an unsigned temporary file must return UnsignedOrInvalid, never fake success
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "unsigned-content");
            var sigResult = WingetSoftwareCenterService.VerifyBinaryAuthenticodeSignature(tempFile);
            Assert.Equal(SignatureVerificationState.UnsignedOrInvalid, sigResult.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
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
