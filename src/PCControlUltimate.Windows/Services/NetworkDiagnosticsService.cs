using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Windows.Services;

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
    double? IcmpLatencyMs,
    double IcmpPacketLossPercent,
    double? TcpHandshakeMs,
    TcpConnectionProbeState TcpState,
    bool IsReachable,
    string StatusDescription
);

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
    double TcpReachabilityPercent = 100.0
);

public record stringTargetSummary(string Value)
{
    public static implicit operator string(stringTargetSummary s) => s.Value;
    public static implicit operator stringTargetSummary(string s) => new(s);
    public override string ToString() => Value;
}

/// <summary>
/// Network Intelligence & Diagnostics Service (§7):
/// Eliminates the 50% artificial packet loss bug by strictly separating:
/// 1) ICMP Echo Packet Loss (multi-sample Ping)
/// 2) TCP Handshake Reachability (Connected, Timeout, ConnectionRefused, FilteredPort, ConnectionFailure) against verified TCP/HTTPS endpoints
/// 3) DNS Resolution Latency (Resolved, DnsResolutionFailure, CacheHit)
/// Never classifies a refused/closed UDP game port as ICMP packet loss.
/// </summary>
public class NetworkDiagnosticsService
{
    private record VerifiedDiagnosticEndpoint(string Name, string IpAddress, int TcpPort, string DnsHostname);

    private static readonly VerifiedDiagnosticEndpoint[] DefaultVerifiedEndpoints =
    [
        new("Cloudflare Edge Anycast", "1.1.1.1", 443, "one.one.one.one"),
        new("Google Public Edge", "8.8.8.8", 443, "dns.google"),
        new("Quad9 Security Edge", "9.9.9.9", 443, "dns.quad9.net"),
        new("OpenDNS Cisco Umbrella", "208.67.222.222", 443, "resolver1.opendns.com")
    ];

    public async Task<NetworkDiagnosticsReport> RunDiagnosticsAsync(
        string targetHost = "1.1.1.1",
        int sampleCount = 6,
        CancellationToken cancellationToken = default)
    {
        var icmpSamples = new List<double>();
        int icmpSent = Math.Max(3, sampleCount);
        int icmpLost = 0;

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
                else
                {
                    icmpLost++;
                }
            }
            catch
            {
                icmpLost++;
            }

            if (i < icmpSent - 1)
                await Task.Delay(50, cancellationToken);
        }

        // Also test TCP 443 handshake separately from ICMP packet loss
        var tcpResult = await ProbeTcpEndpointAsync(targetHost, 443, 1800, cancellationToken);
        var dnsResult = await MeasureDnsResolutionAsync("steampowered.com", cancellationToken);

        // If ICMP is filtered by local/ISP firewall but TCP 443 succeeds, do NOT report 100% network packet loss;
        // use multi-sample TCP RTT as fallback and report that ICMP Echo is filtered.
        double packetLossPercent;
        if (icmpSamples.Count > 0)
        {
            packetLossPercent = Math.Round((double)icmpLost / icmpSent * 100.0, 1);
        }
        else if (tcpResult.State == TcpConnectionProbeState.Connected && tcpResult.LatencyMs.HasValue)
        {
            icmpSamples.Add(tcpResult.LatencyMs.Value);
            packetLossPercent = 0.0;
        }
        else
        {
            packetLossPercent = 100.0;
        }

        if (icmpSamples.Count == 0)
        {
            var failureFinding = new StructuredNetworkDiagnosticFinding(
                WhatWasTested: $"ICMP Echo ({icmpSent} amostras) e Handshake TCP:443 para {targetHost}",
                WhatFailed: $"Sem resposta ICMP e conexão TCP falhou ({tcpResult.State})",
                EvidenceFound: $"0/{icmpSent} respostas ICMP recebidas; estado TCP: {tcpResult.State}.",
                ProbableCause: "Sem conectividade externa à internet, cabo/Wi-Fi desconectado ou bloqueio de firewall de saída.",
                RecommendedSolution: "Verifique a conexão física do adaptador de rede, o gateway padrão (roteador) e regras de firewall.",
                VerificationSteps: "Execute novamente o diagnóstico de rede e confirme latência ICMP/TCP < 80 ms com 0% de perda."
            );

            return new NetworkDiagnosticsReport(
                targetHost,
                0,
                0,
                100.0,
                false,
                failureFinding.EvidenceFound,
                dnsResult.LatencyMs,
                null,
                failureFinding,
                0.0
            );
        }

        double avgLatency = icmpSamples.Average();
        double jitter = CalculateJitter(icmpSamples);
        bool isStable = avgLatency < 65.0 && jitter < 12.0 && packetLossPercent < 1.0;

        var finding = BuildStructuredFinding(
            $"ICMP Echo ({icmpSent} amostras) + TCP:443 ({targetHost}) + DNS",
            avgLatency,
            jitter,
            packetLossPercent,
            tcpResult.State,
            dnsResult.State
        );

        string summary = isStable
            ? $"Conexão estável (Latência: {avgLatency:F1} ms | Jitter: {jitter:F1} ms | Perda ICMP: {packetLossPercent:F1}% | TCP: {tcpResult.State})"
            : $"Instabilidade detectada (Latência: {avgLatency:F1} ms | Jitter: {jitter:F1} ms | Perda ICMP: {packetLossPercent:F1}%). {finding.RecommendedSolution}";

        return new NetworkDiagnosticsReport(
            targetHost,
            Math.Round(avgLatency, 1),
            Math.Round(jitter, 1),
            packetLossPercent,
            isStable,
            summary,
            dnsResult.LatencyMs,
            null,
            finding,
            tcpResult.State == TcpConnectionProbeState.Connected ? 100.0 : 0.0
        );
    }

    /// <summary>
    /// Multi-target real network diagnostic across verified TCP/HTTPS and ICMP endpoints.
    /// Fixes the historical 50% artificial packet loss caused by probing UDP-only game ports over TCP.
    /// </summary>
    public async Task<NetworkDiagnosticsReport> RunRealDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var targetResults = new List<NetworkTargetProbeResult>();
        var allLatencies = new List<double>();
        int totalIcmpSent = 0;
        int totalIcmpLost = 0;
        int tcpConnectedTargets = 0;

        foreach (var endpoint in DefaultVerifiedEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Measure ICMP Echo (2 samples per endpoint = 8 total samples)
            double? endpointIcmpAvg = null;
            int epIcmpSent = 2;
            int epIcmpLost = 0;
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
                        else
                        {
                            epIcmpLost++;
                        }
                    }
                    catch
                    {
                        epIcmpLost++;
                    }
                }
            }

            if (epIcmpSamples.Count > 0)
            {
                endpointIcmpAvg = Math.Round(epIcmpSamples.Average(), 1);
                allLatencies.AddRange(epIcmpSamples);
            }

            totalIcmpSent += epIcmpSent;
            totalIcmpLost += epIcmpLost;

            // 2. Measure TCP 443 Handshake separately (never mixed into ICMP packetLossPercent!)
            var tcpProbe = await ProbeTcpEndpointAsync(endpoint.IpAddress, endpoint.TcpPort, 1500, cancellationToken);
            if (tcpProbe.State == TcpConnectionProbeState.Connected)
            {
                tcpConnectedTargets++;
                if (!endpointIcmpAvg.HasValue && tcpProbe.LatencyMs.HasValue)
                {
                    allLatencies.Add(tcpProbe.LatencyMs.Value);
                }
            }

            double epIcmpLossPct = epIcmpSamples.Count > 0
                ? Math.Round((double)epIcmpLost / epIcmpSent * 100.0, 1)
                : (tcpProbe.State == TcpConnectionProbeState.Connected ? 0.0 : 100.0);

            bool reachable = epIcmpSamples.Count > 0 || tcpProbe.State == TcpConnectionProbeState.Connected;

            targetResults.Add(new NetworkTargetProbeResult(
                TargetName: endpoint.Name,
                HostOrIp: endpoint.IpAddress,
                TcpPort: endpoint.TcpPort,
                IcmpLatencyMs: endpointIcmpAvg,
                IcmpPacketLossPercent: epIcmpLossPct,
                TcpHandshakeMs: tcpProbe.LatencyMs,
                TcpState: tcpProbe.State,
                IsReachable: reachable,
                StatusDescription: $"ICMP: {(endpointIcmpAvg.HasValue ? $"{endpointIcmpAvg:F1} ms" : "Filtrado")} | TCP:{endpoint.TcpPort}: {tcpProbe.State} ({(tcpProbe.LatencyMs.HasValue ? $"{tcpProbe.LatencyMs:F1} ms" : "N/D")})"
            ));
        }

        var dnsProbe = await MeasureDnsResolutionAsync("steampowered.com", cancellationToken);

        double tcpReachabilityPct = DefaultVerifiedEndpoints.Length > 0
            ? Math.Round((double)tcpConnectedTargets / DefaultVerifiedEndpoints.Length * 100.0, 1)
            : 0.0;

        // Calculate true ICMP Packet Loss without conflating TCP port state or ICMP firewall filtering
        double truePacketLossPercent;
        if (allLatencies.Count == 0 && tcpConnectedTargets == 0)
        {
            truePacketLossPercent = 100.0;
        }
        else if (totalIcmpLost == totalIcmpSent && tcpConnectedTargets > 0)
        {
            // ICMP Echo is blocked by local/ISP firewall, but TCP handshakes succeeded with 0% loss
            truePacketLossPercent = 0.0;
        }
        else
        {
            // Only count ICMP loss on endpoints that responded to at least one probe or when all probes failed
            int responsiveEndpointsIcmpSent = targetResults.Where(t => t.IcmpLatencyMs.HasValue).Count() * 2;
            int responsiveEndpointsIcmpLost = targetResults
                .Where(t => t.IcmpLatencyMs.HasValue)
                .Sum(t => (int)Math.Round(t.IcmpPacketLossPercent / 100.0 * 2));

            truePacketLossPercent = responsiveEndpointsIcmpSent > 0
                ? Math.Round((double)responsiveEndpointsIcmpLost / responsiveEndpointsIcmpSent * 100.0, 1)
                : 0.0;
        }

        double avgRtt = allLatencies.Count > 0 ? Math.Round(allLatencies.Average(), 1) : 0.0;
        double jitter = CalculateJitter(allLatencies);
        bool isStable = (allLatencies.Count > 0 || tcpConnectedTargets > 0) &&
                        avgRtt < 85.0 &&
                        jitter < 15.0 &&
                        truePacketLossPercent < 2.0;

        var primaryTcpState = tcpConnectedTargets > 0
            ? TcpConnectionProbeState.Connected
            : (targetResults.FirstOrDefault()?.TcpState ?? TcpConnectionProbeState.ConnectionFailure);

        var finding = BuildStructuredFinding(
            "Multi-Destino Verificado (1.1.1.1:443, 8.8.8.8:443, 9.9.9.9:443, 208.67.222.222:443) + ICMP + DNS",
            avgRtt,
            jitter,
            truePacketLossPercent,
            primaryTcpState,
            dnsProbe.State
        );

        string summary = (allLatencies.Count > 0 || tcpConnectedTargets > 0)
            ? $"Medição Multi-Destino ({tcpConnectedTargets}/{DefaultVerifiedEndpoints.Length} alvos TCP:443 conectados): RTT {avgRtt:F1} ms | Jitter {jitter:F1} ms | Perda ICMP {truePacketLossPercent:F1}% | DNS {(dnsProbe.LatencyMs.HasValue ? $"{dnsProbe.LatencyMs:F1} ms" : "N/D")}"
            : "Sem conectividade externa nos alvos testados.";

        return new NetworkDiagnosticsReport(
            "Multi-Target (Cloudflare / Google / Quad9 / OpenDNS)",
            avgRtt,
            jitter,
            truePacketLossPercent,
            isStable,
            summary,
            dnsProbe.LatencyMs,
            targetResults,
            finding,
            tcpReachabilityPct
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
        double icmpLossPercent,
        TcpConnectionProbeState tcpState,
        DnsResolutionState dnsState)
    {
        if (tcpState != TcpConnectionProbeState.Connected && icmpLossPercent >= 100.0)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Handshake TCP ({tcpState}) e sondagem ICMP sem resposta",
                EvidenceFound: $"Estado TCP: {tcpState} | Perda ICMP: {icmpLossPercent:F1}% | Estado DNS: {dnsState}",
                ProbableCause: "Desconexão de rede local, queda do provedor (ISP) ou bloqueio de firewall.",
                RecommendedSolution: "Verifique o status do adaptador Ethernet/Wi-Fi e reinicie o gateway local se necessário.",
                VerificationSteps: "Reexecute o teste no Network Lab e confirme alcance TCP Connected em 1.1.1.1:443."
            );
        }

        if (icmpLossPercent >= 2.0)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Perda real de pacotes ICMP ({icmpLossPercent:F1}%)",
                EvidenceFound: $"RTT médio: {avgLatencyMs:F1} ms | Jitter: {jitterMs:F1} ms | Perda ICMP: {icmpLossPercent:F1}%",
                ProbableCause: "Congestionamento na rede local (downloads em segundo plano), interferência Wi-Fi ou saturação na rota do provedor.",
                RecommendedSolution: "Prefira conexão via cabo Ethernet, feche downloads pesados em segundo plano e avalie a rota no Game Network Accelerator.",
                VerificationSteps: "Execute nova bateria de 8 amostras ICMP e verifique se a perda cai para 0%."
            );
        }

        if (jitterMs >= 15.0)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Oscilação de latência (Jitter de {jitterMs:F1} ms acima do ideal < 15 ms)",
                EvidenceFound: $"RTT médio: {avgLatencyMs:F1} ms | Jitter: {jitterMs:F1} ms | Perda ICMP: {icmpLossPercent:F1}%",
                ProbableCause: "Uso de rede sem fio (Wi-Fi) sujeita a interferência de canal ou bufferbloat no roteador.",
                RecommendedSolution: "Ative o modo Estabilidade no Game Network Accelerator ou utilize cabo Ethernet Cat6.",
                VerificationSteps: "Compare o Jitter antes e depois no gráfico de estabilidade."
            );
        }

        return new StructuredNetworkDiagnosticFinding(
            WhatWasTested: testedScope,
            WhatFailed: "Nenhuma falha detectada (0 anomalias)",
            EvidenceFound: $"RTT médio: {avgLatencyMs:F1} ms | Jitter: {jitterMs:F1} ms | Perda ICMP: {icmpLossPercent:F1}% | TCP: {tcpState} | DNS: {dnsState}",
            ProbableCause: "Rota direta saudável e sem perda de pacotes.",
            RecommendedSolution: "Mantenha a rota direta atual; não é necessário túnel intermediário.",
            VerificationSteps: "Monitoramento contínuo ativo no Network Lab."
        );
    }
}
