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
    string? DiagnosticMessage = null
);