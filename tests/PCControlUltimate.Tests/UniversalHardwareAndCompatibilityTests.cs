using System;
using System.Collections.Generic;
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
        SensorReadingStatus status = SensorReadingStatus.Available)
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
            DiagnosticMessage: null
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
    public async Task UniversalHardwareEngine_ScenarioH_K_M_N_HandlesNoGpu_NonAdmin_UnavailableSensors_AndIncompleteDrivers()
    {
        // Scenario H + K + M + N: No dedicated GPU, non-admin session, sensors unavailable
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
            hasBatteryOverride: false,
            isDomainOrPolicyManagedOverride: false,
            isRunningAsAdminOverride: false);

        Assert.Null(report.PrimaryGpu);
        Assert.Empty(report.Gpus);
        Assert.False(report.Cpu.PackageTemperatureC.IsAvailable);
        Assert.Equal(UniversalSensorStatus.RequiresAdmin, report.Cpu.PackageTemperatureC.SensorStatus);
        Assert.Equal(SystemHardwareProfile.Basic, report.DetectedProfile);
    }

    [Fact]
    public void WindowsCompatibilityEngine_ClassifiesAllSixStatesAndNeverBypassesCorporatePolicy()
    {
        var compat = new WindowsCompatibilityEngine();

        // 1. BlockedByPolicy when Group Policy restricts Registry
        var corpProfile = compat.DetectEnvironment(
            isRunningAsAdminOverride: true,
            hasRegistryPolicyRestrictionOverride: true,
            isEnterpriseManagedOverride: true,
            buildNumberOverride: 22631,
            editionOverride: "Enterprise");

        var blockedRes = compat.EvaluateOperation("perf-mmcss", "Otimizar MMCSS", true, true, false, 19041, corpProfile);
        Assert.Equal(OperationCompatibilityState.BlockedByPolicy, blockedRes.State);
        Assert.False(blockedRes.CanExecuteNow);

        // 2. RequiresAdmin when Standard User attempts HKLM tweak
        var stdProfile = compat.DetectEnvironment(
            isRunningAsAdminOverride: false,
            hasRegistryPolicyRestrictionOverride: false,
            isEnterpriseManagedOverride: false,
            buildNumberOverride: 22631,
            editionOverride: "Professional");

        var adminRes = compat.EvaluateOperation("perf-mmcss", "Otimizar MMCSS", true, true, false, 19041, stdProfile);
        Assert.Equal(OperationCompatibilityState.RequiresAdmin, adminRes.State);
        Assert.True(adminRes.RequiresElevationPrompt);

        // 3. NotSupported on older OS build
        var legacyProfile = compat.DetectEnvironment(
            isRunningAsAdminOverride: true,
            hasRegistryPolicyRestrictionOverride: false,
            isEnterpriseManagedOverride: false,
            buildNumberOverride: 14393,
            editionOverride: "Home");

        var unsupportedRes = compat.EvaluateOperation("game-hags", "HAGS", true, true, false, 19041, legacyProfile);
        Assert.Equal(OperationCompatibilityState.NotSupported, unsupportedRes.State);

        // 4. Compatible on supported Admin Windows 10/11
        var adminProfile = compat.DetectEnvironment(
            isRunningAsAdminOverride: true,
            hasRegistryPolicyRestrictionOverride: false,
            isEnterpriseManagedOverride: false,
            buildNumberOverride: 26100,
            editionOverride: "Professional");

        var okRes = compat.EvaluateOperation("perf-mmcss", "Otimizar MMCSS", true, true, false, 19041, adminProfile);
        Assert.Equal(OperationCompatibilityState.Compatible, okRes.State);
        Assert.True(okRes.CanExecuteNow);
    }

    [Fact]
    public void WingetSoftwareCenterService_CoversAll14CategoriesAndEnforcesUserConfirmation()
    {
        var service = new WingetSoftwareCenterService();
        var distinctCategories = WingetSoftwareCenterService.OfficialCatalog
            .Select(p => p.Category)
            .Distinct()
            .ToList();

        Assert.Equal(14, distinctCategories.Count);
        Assert.NotEmpty(service.SearchCatalog("Firefox"));
        Assert.NotEmpty(service.SearchCatalog(category: SoftwarePackageCategory.Gaming));
    }

    [Fact]
    public async Task SystemDoctorService_And_RecommendationEngine_OperateInReadOnlyModeAndRequireConfirmationForRepairs()
    {
        var snapshot = CreateSnapshot(
            "Intel Core i7-13700K",
            64f,
            "NVIDIA GeForce RTX 4080",
            63f,
            76f,
            16384f,
            28f, // >80% of 32GB -> triggers memory recommendation
            32f
        );

        var doctor = new SystemDoctorService();
        var report = await doctor.RunComprehensiveAuditAsync(snapshot, isRunningAsAdminOverride: false);

        Assert.InRange(report.HealthScore, 10, 100);
        Assert.NotEmpty(report.Findings);

        // Unconfirmed repair must always be rejected
        var unconfirmedRepair = await doctor.ExecuteVerifiedRepairToolAsync("sfc-scannow", userConfirmed: false);
        Assert.False(unconfirmedRepair.Success);

        using var stub = new ConfigurableHardwareMonitorStub(snapshot);
        var hwEngine = new UniversalHardwareEngine(stub);
        var hwReport = await hwEngine.BuildUniversalReportAsync(snapshot, isRunningAsAdminOverride: false);

        var recEngine = new RecommendationEngine();
        var recommendations = recEngine.GenerateRecommendations(hwReport, null, report);
        Assert.NotEmpty(recommendations);
        Assert.Contains(recommendations, r => r.RecommendationId == "rec-memory-pressure");
    }
}
