using System;
using System.Collections.Generic;

namespace PCControlUltimate.Core.Models;

public enum SensorReadingStatus
{
    Available,
    SensorNotSupported,
    ReadingUnavailable,
    CommunicationFailure
}

public record SensorDataPoint(
    string Identifier,
    string Name,
    string HardwareType,
    string SensorType,
    float? Value,
    string Unit,
    bool IsAvailable,
    SensorReadingStatus Status = SensorReadingStatus.Available
)
{
    public string HardwareCategory => HardwareType;
    public string FormattedValue => IsAvailable && Value.HasValue
        ? $"{Value.Value:F1} {Unit}".Trim()
        : Status switch
        {
            SensorReadingStatus.SensorNotSupported => "Sensor não suportado",
            SensorReadingStatus.CommunicationFailure => "Falha de comunicação",
            _ => "Leitura indisponível (N/D)"
        };
}

public record StorageTelemetryPoint(
    string Model,
    float? CapacityGb,
    float? FreeSpaceGb,
    float? TemperatureC
);

/// <summary>
/// Per-adapter GPU telemetry point so systems with multiple GPUs (e.g. Hybrid iGPU + dGPU or Multi-GPU)
/// preserve independent sensors, clocks, VRAM, and status for every installed adapter.
/// </summary>
public record GpuAdapterTelemetryPoint(
    string Model,
    string HardwareType,
    float? CoreTempC,
    float? HotspotTempC,
    float? LoadPercent,
    float? CoreClockMhz,
    float? PowerWatts,
    float? FanRpm,
    float? VramUsedMb,
    float? VramTotalMb,
    string? DriverVersion = null,
    string? PnpDeviceId = null
);

public record HardwareSnapshot(
    DateTime TimestampUtc,
    float? CpuPackageTemp,
    float? CpuTotalLoad,
    float? GpuCoreTemp,
    float? GpuHotspotTemp,
    float? GpuVramUsedMb,
    float? GpuVramTotalMb,
    float? RamUsedGb,
    float? RamTotalGb,
    IReadOnlyList<SensorDataPoint> Sensors,
    string? CpuModelName = null,
    float? CpuClockMhz = null,
    float? CpuPowerWatts = null,
    string? GpuModelName = null,
    float? GpuLoadPercent = null,
    float? GpuClockMhz = null,
    float? GpuPowerWatts = null,
    float? GpuFanRpm = null,
    IReadOnlyList<StorageTelemetryPoint>? StorageDrives = null,
    SensorReadingStatus MonitorStatus = SensorReadingStatus.Available,
    string? DiagnosticMessage = null,
    IReadOnlyList<GpuAdapterTelemetryPoint>? GpuAdapters = null
)
{
    public SensorReadingStatus Status => MonitorStatus;
    public string DiagnosticReason => DiagnosticMessage ?? "N/D";
    public bool IsHardwareMonitorAvailable => MonitorStatus == SensorReadingStatus.Available;
}