using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Windows.Services;

/// <summary>
/// Network Intelligence & Diagnostics Service (§7):
/// Strictly separates:
/// 1) ICMP Echo Packet Loss (IcmpProbeStatus: Responded, PartialLoss, FilteredOrBlocked, Unreachable)
/// 2) TCP Connection Outcome (TcpConnectionProbeState: Connected, Timeout, ConnectionRefused, FilteredPort, ConnectionFailure)
/// 3) DNS Resolution Outcome (DnsResolutionState: Resolved, DnsResolutionFailure, CacheHit)
/// Never uses TCP handshake results to fabricate or override ICMP packet loss, and never probes UDP-only ports over TCP.
/// </summary>
public class NetworkDiagnosticsService
{
    public record VerifiedDiagnosticEndpoint(string Name, string IpAddress, int TcpPort, string DnsHostname);

    public static IReadOnlyList<VerifiedDiagnosticEndpoint> DefaultVerifiedEndpoints { get; } =
    [
        new("Cloudflare Edge Anycast", "1.1.1.1", 443, "one.one.one.one"),
        new("Google Public Edge", "8.8.8.8", 443, "dns.google"),
        new("Quad9 Security Edge", "9.9.9.9", 443, "dns.quad9.net"),
        new("OpenDNS Cisco Umbrella", "208.67.222.222", 443, "resolver1.opendns.com")
    ];

    /// <summary>
    /// Evaluates a single target's ICMP and TCP outcomes independently.
    /// When ICMP fails (0 responses) while TCP connects, ICMP is classified as FilteredOrBlocked (inconclusive, null loss%),
    /// never as 0% ICMP loss and never conflated with TCP failure.
    /// </summary>
    public static NetworkTargetProbeResult EvaluateTargetProbeOutcome(
        string targetName,
        string hostOrIp,
        int tcpPort,
        int icmpSent,
        IReadOnlyList<double> icmpSamplesMs,
        TcpConnectionProbeState tcpState,
        double? tcpHandshakeMs)
    {
        int sent = Math.Max(1, icmpSent);
        int received = icmpSamplesMs?.Count ?? 0;
        int lost = Math.Max(0, sent - received);
        bool isTcpConnected = tcpState == TcpConnectionProbeState.Connected;

        IcmpProbeStatus icmpStatus;
        double? icmpLossPct;
        double? icmpAvgMs = null;

        if (received > 0 && icmpSamplesMs != null)
        {
            icmpAvgMs = Math.Round(icmpSamplesMs.Average(), 1);
            double loss = Math.Round((double)lost / sent * 100.0, 1);
            icmpLossPct = loss;
            icmpStatus = loss == 0.0 ? IcmpProbeStatus.Responded : IcmpProbeStatus.PartialLoss;
        }
        else if (isTcpConnected)
        {
            // ICMP did not respond, but the host is reachable via TCP: ICMP is filtered/blocked (inconclusive loss%)
            icmpStatus = IcmpProbeStatus.FilteredOrBlocked;
            icmpLossPct = null;
        }
        else
        {
            icmpStatus = IcmpProbeStatus.Unreachable;
            icmpLossPct = 100.0;
        }

        string icmpText = icmpStatus switch
        {
            IcmpProbeStatus.Responded => $"{icmpAvgMs:F1} ms (Perda ICMP: 0%)",
            IcmpProbeStatus.PartialLoss => $"{icmpAvgMs:F1} ms (Perda ICMP: {icmpLossPct:F1}%)",
            IcmpProbeStatus.FilteredOrBlocked => "Inconclusivo / Filtrado por Firewall (Sem resposta ICMP Echo)",
            _ => "Inacessível (100% perda ICMP)"
        };

        string tcpText = isTcpConnected && tcpHandshakeMs.HasValue
            ? $"Connected ({tcpHandshakeMs:F1} ms)"
            : tcpState.ToString();

        return new NetworkTargetProbeResult(
            TargetName: targetName,
            HostOrIp: hostOrIp,
            TcpPort: tcpPort,
            IcmpStatus: icmpStatus,
            IcmpLatencyMs: icmpAvgMs,
            IcmpPacketLossPercent: icmpLossPct,
            TcpHandshakeMs: tcpHandshakeMs,
            TcpState: tcpState,
            IsTcpConnected: isTcpConnected,
            StatusDescription: $"ICMP: {icmpText} | TCP:{tcpPort}: {tcpText}"
        );
    }

    public async Task<NetworkDiagnosticsReport> RunDiagnosticsAsync(
        string targetHost = "1.1.1.1",
        int sampleCount = 6,
        CancellationToken cancellationToken = default)
    {
        var icmpSamples = new List<double>();
        int icmpSent = Math.Max(3, sampleCount);

        using var ping = new Ping();
        for (int i = 0; i < icmpSent; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(targetHost, 1500);
                if (reply.Status == IPStatus.Success)
                {
                    icmpSamples.Add(reply.RoundtripTime);
                }
            }
            catch
            {
                // ICMP send exception recorded as unreceived sample
            }

            if (i < icmpSent - 1)
                await Task.Delay(50, cancellationToken);
        }

        var tcpResult = await ProbeTcpEndpointAsync(targetHost, 443, 1800, cancellationToken);
        var dnsResult = await MeasureDnsResolutionAsync("steampowered.com", cancellationToken);

        var targetProbe = EvaluateTargetProbeOutcome(
            $"Alvo {targetHost}",
            targetHost,
            443,
            icmpSent,
            icmpSamples,
            tcpResult.State,
            tcpResult.LatencyMs
        );

        return BuildAggregateReportFromTargets(
            targetHost,
            new[] { targetProbe },
            dnsResult.LatencyMs,
            dnsResult.State
        );
    }

    /// <summary>
    /// Multi-target real network diagnostic across verified TCP/HTTPS and ICMP endpoints.
    /// Keeps ICMP loss, TCP handshake states, and DNS resolution strictly separated.
    /// </summary>
    public async Task<NetworkDiagnosticsReport> RunRealDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var targetResults = new List<NetworkTargetProbeResult>();

        foreach (var endpoint in DefaultVerifiedEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int epIcmpSent = 2;
            var epIcmpSamples = new List<double>();

            using (var ping = new Ping())
            {
                for (int i = 0; i < epIcmpSent; i++)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(endpoint.IpAddress, 1200);
                        if (reply.Status == IPStatus.Success)
                        {
                            epIcmpSamples.Add(reply.RoundtripTime);
                        }
                    }
                    catch { }
                }
            }

            var tcpProbe = await ProbeTcpEndpointAsync(endpoint.IpAddress, endpoint.TcpPort, 1500, cancellationToken);

            var probeOutcome = EvaluateTargetProbeOutcome(
                endpoint.Name,
                endpoint.IpAddress,
                endpoint.TcpPort,
                epIcmpSent,
                epIcmpSamples,
                tcpProbe.State,
                tcpProbe.LatencyMs
            );

            targetResults.Add(probeOutcome);
        }

        var dnsProbe = await MeasureDnsResolutionAsync("steampowered.com", cancellationToken);

        return BuildAggregateReportFromTargets(
            "Multi-Target (Cloudflare / Google / Quad9 / OpenDNS)",
            targetResults,
            dnsProbe.LatencyMs,
            dnsProbe.State
        );
    }

    public static NetworkDiagnosticsReport BuildAggregateReportFromTargets(
        string summaryLabel,
        IReadOnlyList<NetworkTargetProbeResult> targetResults,
        double? dnsLatencyMs,
        DnsResolutionState dnsState)
    {
        if (targetResults == null || targetResults.Count == 0)
        {
            return new NetworkDiagnosticsReport(
                summaryLabel,
                0,
                0,
                100.0,
                false,
                "Nenhum alvo testado.",
                dnsLatencyMs,
                Array.Empty<NetworkTargetProbeResult>(),
                null,
                0.0,
                IcmpProbeStatus.NotTested,
                null,
                TcpConnectionProbeState.ConnectionFailure,
                dnsState
            );
        }

        int tcpConnectedCount = targetResults.Count(t => t.IsTcpConnected);
        double tcpReachabilityPct = Math.Round((double)tcpConnectedCount / targetResults.Count * 100.0, 1);

        var icmpRespondedTargets = targetResults
            .Where(t => t.IcmpStatus is IcmpProbeStatus.Responded or IcmpProbeStatus.PartialLoss && t.IcmpPacketLossPercent.HasValue)
            .ToList();

        IcmpProbeStatus overallIcmpStatus;
        double? overallIcmpLossPercent;

        if (icmpRespondedTargets.Count > 0)
        {
            double avgIcmpLoss = Math.Round(icmpRespondedTargets.Average(t => t.IcmpPacketLossPercent!.Value), 1);
            overallIcmpLossPercent = avgIcmpLoss;
            overallIcmpStatus = avgIcmpLoss == 0.0 ? IcmpProbeStatus.Responded : IcmpProbeStatus.PartialLoss;
        }
        else if (tcpConnectedCount > 0)
        {
            // ICMP blocked/filtered across all targets while TCP connected: ICMP loss is inconclusive (null), never derived from TCP!
            overallIcmpStatus = IcmpProbeStatus.FilteredOrBlocked;
            overallIcmpLossPercent = null;
        }
        else
        {
            overallIcmpStatus = IcmpProbeStatus.Unreachable;
            overallIcmpLossPercent = 100.0;
        }

        var icmpLatencies = targetResults.Where(t => t.IcmpLatencyMs.HasValue).Select(t => t.IcmpLatencyMs!.Value).ToList();
        var tcpLatencies = targetResults.Where(t => t.TcpHandshakeMs.HasValue).Select(t => t.TcpHandshakeMs!.Value).ToList();

        // Use ICMP latency when ICMP responded; if ICMP is filtered, report TCP handshake latency separately
        var latencySamples = icmpLatencies.Count > 0 ? icmpLatencies : tcpLatencies;
        double avgLatency = latencySamples.Count > 0 ? Math.Round(latencySamples.Average(), 1) : 0.0;
        double jitter = CalculateJitter(latencySamples);

        var overallTcpState = tcpConnectedCount > 0
            ? TcpConnectionProbeState.Connected
            : targetResults[0].TcpState;

        bool isStable = (icmpLatencies.Count > 0 || tcpConnectedCount > 0) &&
                        avgLatency < 85.0 &&
                        jitter < 15.0 &&
                        (overallIcmpLossPercent ?? 0.0) < 2.0 &&
                        dnsState != DnsResolutionState.DnsResolutionFailure;

        var finding = BuildStructuredFinding(
            summaryLabel,
            avgLatency,
            jitter,
            overallIcmpStatus,
            overallIcmpLossPercent,
            overallTcpState,
            tcpReachabilityPct,
            dnsState
        );

        string icmpSummaryStr = overallIcmpStatus == IcmpProbeStatus.FilteredOrBlocked
            ? "ICMP: Inconclusivo/Filtrado"
            : $"Perda ICMP: {overallIcmpLossPercent ?? 100.0:F1}% ({overallIcmpStatus})";

        string summary = $"{icmpSummaryStr} | TCP: {overallTcpState} ({tcpReachabilityPct:F0}% conectados) | DNS: {dnsState} ({(dnsLatencyMs.HasValue ? $"{dnsLatencyMs:F1} ms" : "Falha")}) | RTT: {avgLatency:F1} ms | Jitter: {jitter:F1} ms";

        return new NetworkDiagnosticsReport(
            TargetHost: summaryLabel,
            AverageLatencyMs: avgLatency,
            JitterMs: jitter,
            PacketLossPercent: overallIcmpLossPercent ?? 0.0,
            IsStableForGaming: isStable,
            DiagnosticSummary: summary,
            DnsResolutionMs: dnsLatencyMs,
            TargetResults: targetResults,
            StructuredFinding: finding,
            TcpReachabilityPercent: tcpReachabilityPct,
            IcmpStatus: overallIcmpStatus,
            IcmpPacketLossPercent: overallIcmpLossPercent,
            TcpState: overallTcpState,
            DnsState: dnsState
        );
    }

    public static async Task<(double? LatencyMs, TcpConnectionProbeState State)> ProbeTcpEndpointAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            await socket.ConnectAsync(host, port, timeoutCts.Token);
            sw.Stop();
            return (Math.Round(sw.Elapsed.TotalMilliseconds, 1), TcpConnectionProbeState.Connected);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, TcpConnectionProbeState.Timeout);
        }
        catch (SocketException sockEx)
        {
            return sockEx.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => (null, TcpConnectionProbeState.ConnectionRefused),
                SocketError.TimedOut => (null, TcpConnectionProbeState.Timeout),
                SocketError.AccessDenied or SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    (null, TcpConnectionProbeState.FilteredPort),
                _ => (null, TcpConnectionProbeState.ConnectionFailure)
            };
        }
        catch
        {
            return (null, TcpConnectionProbeState.ConnectionFailure);
        }
    }

    public static async Task<(double? LatencyMs, DnsResolutionState State)> MeasureDnsResolutionAsync(
        string hostname,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, ct);
            sw.Stop();
            if (addresses == null || addresses.Length == 0)
            {
                return (null, DnsResolutionState.DnsResolutionFailure);
            }

            double ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            var state = ms < 2.0 ? DnsResolutionState.CacheHit : DnsResolutionState.Resolved;
            return (ms, state);
        }
        catch
        {
            return (null, DnsResolutionState.DnsResolutionFailure);
        }
    }

    public static double CalculateJitter(IReadOnlyList<double> samples)
    {
        if (samples == null || samples.Count <= 1) return 0.0;
        double sumDiffs = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            sumDiffs += Math.Abs(samples[i] - samples[i - 1]);
        }
        return Math.Round(sumDiffs / (samples.Count - 1), 1);
    }

    private static StructuredNetworkDiagnosticFinding BuildStructuredFinding(
        string testedScope,
        double avgLatencyMs,
        double jitterMs,
        IcmpProbeStatus icmpStatus,
        double? icmpLossPercent,
        TcpConnectionProbeState tcpState,
        double tcpReachabilityPct,
        DnsResolutionState dnsState)
    {
        if (dnsState == DnsResolutionState.DnsResolutionFailure)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: "Falha de resolução de nomes DNS (DnsResolutionFailure)",
                EvidenceFound: $"Estado DNS: {dnsState} | Estado TCP: {tcpState} | Estado ICMP: {icmpStatus}",
                ProbableCause: "Servidor DNS configurado indisponível, bloqueio na porta UDP 53 ou queda de link.",
                RecommendedSolution: "Verifique a configuração de DNS do adaptador (ex.: 1.1.1.1 ou 8.8.8.8).",
                VerificationSteps: "Execute novamente o benchmark DNS no Network Lab."
            );
        }

        if (icmpStatus == IcmpProbeStatus.FilteredOrBlocked && tcpState == TcpConnectionProbeState.Connected)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: "ICMP Echo bloqueado/filtrado (Inconclusivo); conexão TCP:443 operante",
                EvidenceFound: $"Estado ICMP: {icmpStatus} (Perda ICMP: Inconclusiva) | Estado TCP: {tcpState} ({tcpReachabilityPct:F0}%) | RTT TCP: {avgLatencyMs:F1} ms",
                ProbableCause: "Firewall local, roteador ou provedor bloqueando pacotes ICMP Echo Request sem afetar tráfego TCP/HTTPS.",
                RecommendedSolution: "Nenhuma intervenção obrigatória; o bloqueio ICMP não impede conexões TCP, mas torna a perda ICMP inconclusiva.",
                VerificationSteps: "Verifique regras de ICMP no firewall caso deseje telemetria via Ping."
            );
        }

        if (tcpState != TcpConnectionProbeState.Connected && icmpStatus == IcmpProbeStatus.Unreachable)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Inacessibilidade ICMP ({icmpStatus}) e falha TCP ({tcpState})",
                EvidenceFound: $"Estado ICMP: {icmpStatus} (100% perda) | Estado TCP: {tcpState} | Estado DNS: {dnsState}",
                ProbableCause: "Sem conectividade externa à internet ou bloqueio de saída no gateway.",
                RecommendedSolution: "Verifique o cabo Ethernet/Wi-Fi e o gateway padrão.",
                VerificationSteps: "Reexecute o teste no Network Lab."
            );
        }

        if ((icmpLossPercent ?? 0.0) >= 2.0)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Perda parcial de pacotes ICMP ({icmpLossPercent:F1}%)",
                EvidenceFound: $"Estado ICMP: {icmpStatus} ({icmpLossPercent:F1}%) | TCP: {tcpState} | RTT: {avgLatencyMs:F1} ms | Jitter: {jitterMs:F1} ms",
                ProbableCause: "Congestionamento na rede local, interferência Wi-Fi ou saturação no provedor.",
                RecommendedSolution: "Prefira cabo Ethernet e feche downloads em segundo plano.",
                VerificationSteps: "Execute nova bateria ICMP e confirme perda 0%."
            );
        }

        return new StructuredNetworkDiagnosticFinding(
            WhatWasTested: testedScope,
            WhatFailed: "Nenhuma falha detectada",
            EvidenceFound: $"Estado ICMP: {icmpStatus} (Perda: {icmpLossPercent ?? 0.0:F1}%) | TCP: {tcpState} ({tcpReachabilityPct:F0}%) | DNS: {dnsState} | RTT: {avgLatencyMs:F1} ms | Jitter: {jitterMs:F1} ms",
            ProbableCause: "Rota direta saudável com respostas ICMP, TCP e DNS independentes verificadas.",
            RecommendedSolution: "Mantenha a rota direta atual.",
            VerificationSteps: "Monitoramento contínuo disponível no Network Lab."
        );
    }
}
