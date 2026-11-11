using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PCControlUltimate.Core.Models;

public enum IcmpProbeStatus
{
    Responded,
    PartialLoss,
    FilteredOrBlocked,
    Unreachable,
    Inconclusive,
    NotTested
}

public enum TcpConnectionProbeState
{
    Connected,
    Timeout,
    TimedOut,
    ConnectionRefused,
    FilteredPort,
    NetworkUnreachable,
    ConnectionFailure
}

public enum DnsResolutionState
{
    Resolved,
    DnsResolutionFailure,
    ServerUnreachable,
    CacheHit
}

public record StructuredNetworkDiagnosticFinding(
    string WhatWasTested,
    string WhatFailed,
    string EvidenceFound,
    string ProbableCause,
    string RecommendedSolution,
    string VerificationSteps
);

public record NetworkEndpointTestResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icmpRoundtripMs")] string IcmpRoundtripMs,
    [property: JsonPropertyName("tcpConnectMs")] string TcpConnectMs,
    [property: JsonPropertyName("note")] string Note
);

public record DnsQueryResult(
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("resolutionMs")] long ResolutionMs
);

public record ExternalThroughputResult(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("downloadMbps")] double? DownloadMbps,
    [property: JsonPropertyName("bytesTransferred")] long BytesTransferred,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("sourceUrl")] string SourceUrl,
    [property: JsonPropertyName("errorReason")] string? ErrorReason = null
);

public record NetworkTargetProbeResult(
    string TargetName,
    string HostOrIp,
    int TcpPort,
    IcmpProbeStatus IcmpStatus,
    double? IcmpLatencyMs,
    double? IcmpPacketLossPercent,
    double? TcpHandshakeMs,
    TcpConnectionProbeState TcpState,
    bool IsTcpConnected,
    string StatusDescription
)
{
    public bool IsReachable => IcmpStatus is IcmpProbeStatus.Responded or IcmpProbeStatus.PartialLoss || IsTcpConnected;
}

public record stringTargetSummary(string Value)
{
    public static implicit operator string(stringTargetSummary s) => s.Value;
    public static implicit operator stringTargetSummary(string s) => new(s);
    public override string ToString() => Value;
}

/// <summary>
/// Shared network diagnostic report contract in PCControlUltimate.Core.Models.
/// Keeps ICMP packet loss strictly separated from TCP connection states (Connected, Timeout, ConnectionRefused, FilteredPort)
/// and DNS resolution state (Resolved, DnsResolutionFailure, CacheHit).
/// PacketLossPercent is nullable (double?): when ICMP is FilteredOrBlocked or Inconclusive, PacketLossPercent is null
/// and FormattedPacketLoss returns "Não mensurável (Inconclusivo)" instead of inventing 0% or 100%.
/// </summary>
public record NetworkDiagnosticsReport(
    stringTargetSummary TargetHost,
    double AverageLatencyMs,
    double JitterMs,
    [property: JsonPropertyName("packetLoss")] double? PacketLossPercent,
    bool IsStableForGaming,
    string DiagnosticSummary,
    double? DnsResolutionMs = null,
    IReadOnlyList<NetworkTargetProbeResult>? TargetResults = null,
    StructuredNetworkDiagnosticFinding? StructuredFinding = null,
    double TcpReachabilityPercent = 100.0,
    IcmpProbeStatus IcmpStatus = IcmpProbeStatus.Responded,
    double? IcmpPacketLossPercent = null,
    TcpConnectionProbeState TcpState = TcpConnectionProbeState.Connected,
    DnsResolutionState DnsState = DnsResolutionState.Resolved,
    double? AverageIcmpLatencyMs = null,
    double? AverageTcpHandshakeMs = null,
    [property: JsonPropertyName("endpointDetails")] IReadOnlyList<NetworkEndpointTestResult>? EndpointDetails = null,
    [property: JsonPropertyName("dnsChecks")] IReadOnlyList<DnsQueryResult>? DnsChecks = null
)
{
    public string FormattedPacketLoss => PacketLossPercent.HasValue
        ? $"{PacketLossPercent.Value:F1}%"
        : IcmpStatus == IcmpProbeStatus.FilteredOrBlocked
            ? "Não mensurável (ICMP Filtrado)"
            : "Inconclusivo";
}
