using System;
using System.Collections.Generic;

namespace PCControlUltimate.Core.Models;

public enum IcmpProbeStatus
{
    Responded,
    PartialLoss,
    FilteredOrBlocked,
    Unreachable,
    NotTested
}

public enum TcpConnectionProbeState
{
    Connected,
    Timeout,
    ConnectionRefused,
    FilteredPort,
    ConnectionFailure
}

public enum DnsResolutionState
{
    Resolved,
    DnsResolutionFailure,
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
/// When ICMP is blocked/filtered, IcmpStatus is FilteredOrBlocked and IcmpPacketLossPercent is null (inconclusive).
/// </summary>
public record NetworkDiagnosticsReport(
    stringTargetSummary TargetHost,
    double AverageLatencyMs,
    double JitterMs,
    double PacketLossPercent,
    bool IsStableForGaming,
    string DiagnosticSummary,
    double? DnsResolutionMs = null,
    IReadOnlyList<NetworkTargetProbeResult>? TargetResults = null,
    StructuredNetworkDiagnosticFinding? StructuredFinding = null,
    double TcpReachabilityPercent = 100.0,
    IcmpProbeStatus IcmpStatus = IcmpProbeStatus.Responded,
    double? IcmpPacketLossPercent = null,
    TcpConnectionProbeState TcpState = TcpConnectionProbeState.Connected,
    DnsResolutionState DnsState = DnsResolutionState.Resolved
);
