using System;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Core.Interfaces;

public interface IHardwareMonitorService : IDisposable
{
    bool IsAvailable { get; }
    SensorReadingStatus CurrentStatus { get; }
    string? LastCommunicationError { get; }
    event EventHandler<HardwareSnapshot>? TelemetryUpdated;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<HardwareSnapshot> CaptureSnapshotAsync();
    void StartPolling(TimeSpan interval);
    void StopPolling();
}