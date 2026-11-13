using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Diagnostics;

public record SystemStabilityEventItem(
    DateTime TimestampUtc,
    int EventId,
    string Source,
    string EntryType,
    string Message
);

public record SystemStabilityAuditReport(
    DateTime AuditedAtUtc,
    int LookbackDays,
    bool EventLogAccessible,
    bool IsSystemStable,
    IReadOnlyList<SystemStabilityEventItem> CriticalEvents,
    string DiagnosticSummary
);

/// <summary>
/// Audits Windows System Event Log for real hardware/stability events:
/// - Kernel-Power (Event ID 41: unexpected shutdown/reboot)
/// - WHEA-Logger (Event IDs 1, 17, 18, 19, 46: hardware errors)
/// - Display (Event ID 4101: GPU driver timeout/TDR recovery)
/// Never fabricates events; reports when Event Log access is unavailable or restricted.
/// </summary>
public class SystemStabilityAuditor
{
    public Task<SystemStabilityAuditReport> AuditRecentEventsAsync(
        int lookbackDays = 7,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int days = Math.Clamp(lookbackDays, 1, 90);
        DateTime cutoffUtc = DateTime.UtcNow.AddDays(-days);
        var events = new List<SystemStabilityEventItem>();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new SystemStabilityAuditReport(
                AuditedAtUtc: DateTime.UtcNow,
                LookbackDays: days,
                EventLogAccessible: false,
                IsSystemStable: false,
                CriticalEvents: events,
                DiagnosticSummary: "Não foi possível verificar a estabilidade: a leitura do Windows Event Viewer requer sistema operacional Windows nativo."
            ));
        }

        try
        {
            ReadWindowsSystemEventLog(cutoffUtc, events, cancellationToken);
            bool stable = events.Count == 0;
            string summary = stable
                ? $"Nenhum evento crítico (Kernel-Power 41, WHEA-Logger ou Display 4101) registrado nos últimos {days} dias."
                : $"Foram encontrados {events.Count} evento(s) crítico(s) de estabilidade nos últimos {days} dias.";

            return Task.FromResult(new SystemStabilityAuditReport(
                AuditedAtUtc: DateTime.UtcNow,
                LookbackDays: days,
                EventLogAccessible: true,
                IsSystemStable: stable,
                CriticalEvents: events,
                DiagnosticSummary: summary
            ));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is a caller control signal, not an Event Viewer access failure.
            // Preserve it so the diagnostic workflow can stop promptly and report cancellation.
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SystemStabilityAuditReport(
                AuditedAtUtc: DateTime.UtcNow,
                LookbackDays: days,
                EventLogAccessible: false,
                IsSystemStable: false,
                CriticalEvents: events,
                DiagnosticSummary: $"Estabilidade não verificada: acesso ao log de eventos 'System' restrito ou indisponível nesta sessão: {ex.Message}"
            ));
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReadWindowsSystemEventLog(
        DateTime cutoffUtc,
        List<SystemStabilityEventItem> results,
        CancellationToken cancellationToken)
    {
        using var sysLog = new EventLog("System");
        int count = sysLog.Entries.Count;
        int inspected = 0;

        for (int i = count - 1; i >= 0 && inspected < 1500; i--, inspected++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventLogEntry entry;
            try
            {
                entry = sysLog.Entries[i];
            }
            catch
            {
                continue;
            }

            DateTime entryUtc = entry.TimeGenerated.ToUniversalTime();
            if (entryUtc < cutoffUtc)
                break;

            int eventId = (int)(entry.InstanceId & 0xFFFF);
            string source = entry.Source ?? string.Empty;

            bool isKernelPower41 = eventId == 41 && source.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase);
            // Only classify the hardware-error IDs documented by this auditor.
            // Other WHEA informational/operational events must not become false alarms.
            bool isWhea = source.Contains("WHEA-Logger", StringComparison.OrdinalIgnoreCase) &&
                eventId is 1 or 17 or 18 or 19 or 46;
            bool isDisplayTdr = eventId == 4101 && source.Contains("Display", StringComparison.OrdinalIgnoreCase);

            if (isKernelPower41 || isWhea || isDisplayTdr)
            {
                results.Add(new SystemStabilityEventItem(
                    TimestampUtc: entryUtc,
                    EventId: eventId,
                    Source: source,
                    EntryType: entry.EntryType.ToString(),
                    Message: string.IsNullOrWhiteSpace(entry.Message)
                        ? $"Evento {source} (ID {eventId})"
                        : entry.Message.Trim()
                ));

                if (results.Count >= 25)
                    break;
            }
        }
    }
}