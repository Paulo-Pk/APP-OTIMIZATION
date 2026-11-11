using System;
using System.Collections.Generic;

namespace PCControlUltimate.Core.Models;

public enum HardwareVendor
{
    Unknown,
    Intel,
    Amd,
    Nvidia,
    Qualcomm,
    Microsoft,
    Generic
}

public enum HardwareDataSource
{
    LibreHardwareMonitor,
    Wmi,
    DxgiWin32,
    SmartNvme,
    NetworkInterface,
    PowerManager,
    RegistryHardwareMap,
    Unavailable
}

public enum MetricReliability
{
    VerifiedPhysicalSensor,
    OsReportedCounter,
    Unavailable
}

public enum UniversalSensorStatus
{
    Available,
    Unavailable,
    BlockedByDriver,
    RequiresAdmin,
    NotSupported
}

public enum SystemHardwareProfile
{
    Basic,
    Corporate,
    Notebook,
    Gamer,
    Workstation,
    HighPerformance
}

public enum OperationCompatibilityState
{
    Compatible,
    CompatibleWithLimitations,
    RequiresAdmin,
    BlockedByPolicy,
    NotSupported,
    UnexpectedFailure
}

/// <summary>
/// Represents any hardware metric with full provenance, timestamp, reliability, and limitation metadata (§3.3).
/// Never fabricates or estimates values when unavailable.
/// </summary>
public record DetectedMetric<T>(
    T? Value,
    HardwareDataSource Source,
    DateTime UpdatedAtUtc,
    MetricReliability Reliability,
    bool IsAvailable,
    UniversalSensorStatus SensorStatus,
    string? Limitations = null,
    string Unit = ""
)
{
    public static DetectedMetric<T> Unavailable(
        UniversalSensorStatus status = UniversalSensorStatus.Unavailable,
        string? limitation = null,
        string unit = "") =>
        new(
            default,
            HardwareDataSource.Unavailable,
            DateTime.UtcNow,
            MetricReliability.Unavailable,
            false,
            status,
            limitation ?? "Métrica indisponível neste hardware ou sessão.",
            unit
        );
}

/// <summary>
/// Explicit capability matrix per hardware component (§3.4).
/// </summary>
public record HardwareComponentCapabilityMatrix(
    string ComponentCategory,
    string ComponentName,
    HardwareVendor Vendor,
    bool Identified,
    bool Monitorable,
    bool SensorAvailable,
    bool Adjustable,
    bool BenchmarkCompatible,
    bool DiagnosticCompatible,
    bool RequiresDriver,
    bool RequiresAdmin,
    bool NotSupported,
    string CapabilityNotes
);

public enum GpuAdapterKind
{
    PhysicalDiscrete,
    PhysicalIntegrated,
    VirtualAdapter,
    RemoteDisplayAdapter,
    BasicDisplayAdapter,
    UnknownAdapter
}

/// <summary>
/// Injectable system hardware probe abstraction so unit tests never silently query host WMI/Registry/Win32
/// while production on Windows queries real WMI, Registry, SMBIOS, and Power status.
/// </summary>
public interface IHardwareSystemProbe
{
    bool IsIsolatedSimulation { get; }
    bool IsRunningAsAdministrator();
    bool IsCorporateDomainOrPolicyManaged();
    (string? Name, int? PhysicalCores, int? LogicalProcessors) QueryCpuDetails();
    IReadOnlyList<PCControlUltimate.Core.Interfaces.GpuAdapterTelemetryPoint> QueryGpuAdapters();
    (string? MemoryGeneration, int? ConfiguredSpeedMt) QueryMemoryDetails();
    DetectedBatteryPowerInfo QueryBatteryPowerInfo(bool? hasBatteryOverride, int? batteryChargePercentOverride);
    IReadOnlyList<DetectedNetworkAdapterInfo> QueryNetworkAdapters();
}

public record DetectedGpuAdapterInfo(
    string Name,
    HardwareVendor Vendor,
    string ArchitectureFamily,
    bool IsIntegrated,
    bool IsDiscrete,
    DetectedMetric<float?> CoreTemperatureC,
    DetectedMetric<float?> HotspotTemperatureC,
    DetectedMetric<float?> CoreLoadPercent,
    DetectedMetric<float?> CoreClockMhz,
    DetectedMetric<float?> PowerWatts,
    DetectedMetric<float?> VramUsedMb,
    DetectedMetric<float?> VramTotalMb,
    DetectedMetric<float?> FanRpm,
    string? DriverVersion,
    HardwareComponentCapabilityMatrix Capabilities,
    string? PnpDeviceId = null,
    GpuAdapterKind AdapterKind = GpuAdapterKind.PhysicalDiscrete
)
{
    public bool IsPhysicalGpu => AdapterKind is GpuAdapterKind.PhysicalDiscrete or GpuAdapterKind.PhysicalIntegrated;
    public bool IsVirtualOrDisplayOnly => AdapterKind is GpuAdapterKind.VirtualAdapter or GpuAdapterKind.RemoteDisplayAdapter or GpuAdapterKind.BasicDisplayAdapter;
}

public record DetectedCpuInfo(
    string Name,
    HardwareVendor Vendor,
    string Architecture,
    int? PhysicalCores,
    int LogicalProcessors,
    DetectedMetric<float?> PackageTemperatureC,
    DetectedMetric<float?> TotalLoadPercent,
    DetectedMetric<float?> CoreClockMhz,
    DetectedMetric<float?> PackagePowerWatts,
    bool IsHybridArchitecture,
    HardwareComponentCapabilityMatrix Capabilities
);

public record DetectedMemoryInfo(
    DetectedMetric<float?> TotalGb,
    DetectedMetric<float?> UsedGb,
    DetectedMetric<float?> AvailableGb,
    DetectedMetric<float?> UsagePercent,
    string? MemoryGeneration,
    int? ConfiguredSpeedMt,
    HardwareComponentCapabilityMatrix Capabilities
);

public record DetectedStorageDeviceInfo(
    string Model,
    string BusType,
    string MediaType,
    DetectedMetric<float?> CapacityGb,
    DetectedMetric<float?> FreeSpaceGb,
    DetectedMetric<float?> TemperatureC,
    bool IsSystemDisk,
    HardwareComponentCapabilityMatrix Capabilities
);

public record DetectedNetworkAdapterInfo(
    string Name,
    string InterfaceType,
    bool IsConnected,
    bool IsWiFi,
    long NegotiatedLinkSpeedMbps,
    long BytesReceived,
    long BytesSent,
    string OperationalStatus,
    HardwareComponentCapabilityMatrix Capabilities
);

public record DetectedBatteryPowerInfo(
    bool HasBattery,
    bool IsOnBatteryPower,
    int? ChargePercent,
    string PowerLineStatus,
    string ActivePowerSchemeName
);

public record UniversalHardwareReport(
    DateTime CapturedAtUtc,
    SystemHardwareProfile DetectedProfile,
    string ProfileJustification,
    bool IsRunningAsAdmin,
    DetectedCpuInfo Cpu,
    IReadOnlyList<DetectedGpuAdapterInfo> Gpus,
    DetectedGpuAdapterInfo? PrimaryGpu,
    DetectedMemoryInfo Memory,
    IReadOnlyList<DetectedStorageDeviceInfo> StorageDevices,
    IReadOnlyList<DetectedNetworkAdapterInfo> NetworkAdapters,
    DetectedBatteryPowerInfo BatteryAndPower,
    IReadOnlyList<HardwareComponentCapabilityMatrix> CapabilityMatrix,
    IReadOnlyList<DetectedGpuAdapterInfo>? VirtualDisplayAdapters = null
)
{
    public IReadOnlyList<DetectedGpuAdapterInfo> SafeVirtualDisplayAdapters =>
        VirtualDisplayAdapters ?? Array.Empty<DetectedGpuAdapterInfo>();
}
