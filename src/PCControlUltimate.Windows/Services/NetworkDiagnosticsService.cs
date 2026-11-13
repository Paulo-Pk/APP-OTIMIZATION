using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Windows.Services;

/// <summary>
/// Network Intelligence & Diagnostics Service (§7):
/// Strictly differentiates:
/// 1) IcmpProbeStatus.Responded (0% loss proven by samples)
/// 2) IcmpProbeStatus.PartialLoss (partial loss proven by samples)
/// 3) IcmpProbeStatus.FilteredOrBlocked (no ICMP reply while host is reachable over TCP -> PacketLossPercent is null / "Não mensurável")
/// 4) IcmpProbeStatus.Inconclusive (insufficient evidence / < 2 probes or indeterminate state -> PacketLossPercent is null / "Inconclusivo")
/// 5) IcmpProbeStatus.Unreachable (destination unreachable proven by multi-sample ICMP + TCP failure -> PacketLossPercent is 100.0)
/// Keeps ICMP latency and TCP handshake time as distinct metrics, and implements real HTTPS external throughput measurement.
/// </summary>
public class NetworkDiagnosticsService
{
    public record VerifiedDiagnosticEndpoint(string Name, string IpAddress, int TcpPort, string DnsHostname);

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ConcurrentQueue<string> _diagnosticHistory = new();

    public static IReadOnlyList<VerifiedDiagnosticEndpoint> DefaultVerifiedEndpoints { get; } =
    [
        new("Cloudflare Edge Anycast", "1.1.1.1", 443, "one.one.one.one"),
        new("Google Public Edge", "8.8.8.8", 443, "dns.google"),
        new("Quad9 Security Edge", "9.9.9.9", 443, "dns.quad9.net"),
        new("OpenDNS Cisco Umbrella", "208.67.222.222", 443, "resolver1.opendns.com")
    ];

    public IReadOnlyList<string> GetDiagnosticHistory() => _diagnosticHistory.Reverse().ToList();

    /// <summary>
    /// Evaluates a single target's ICMP and TCP outcomes independently.
    /// Never uses TCP handshake results to calculate ICMP packet loss.
    /// Does not classify absence of ICMP response as 100% loss unless there is sufficient multi-probe + TCP failure or explicit ICMP DestinationUnreachable evidence.
    /// </summary>
    public static NetworkTargetProbeResult EvaluateTargetProbeOutcome(
        string targetName,
        string hostOrIp,
        int tcpPort,
        int icmpSent,
        IReadOnlyList<double> icmpSamplesMs,
        TcpConnectionProbeState tcpState,
        double? tcpHandshakeMs,
        int explicitDestinationUnreachableCount = 0)
    {
        int sent = Math.Max(0, icmpSent);
        int received = icmpSamplesMs?.Count ?? 0;
        int lost = Math.Max(0, sent - received);
        bool isTcpConnected = tcpState == TcpConnectionProbeState.Connected;

        IcmpProbeStatus icmpStatus;
        double? icmpLossPct;
        double? icmpAvgMs = null;

        if (sent <= 0)
        {
            icmpStatus = IcmpProbeStatus.NotTested;
            icmpLossPct = null;
        }
        else if (received > 0 && icmpSamplesMs != null)
        {
            icmpAvgMs = Math.Round(icmpSamplesMs.Average(), 1);
            double loss = Math.Round((double)lost / sent * 100.0, 1);
            icmpLossPct = loss;
            icmpStatus = loss == 0.0 ? IcmpProbeStatus.Responded : IcmpProbeStatus.PartialLoss;
        }
        else if (isTcpConnected || tcpState == TcpConnectionProbeState.ConnectionRefused)
        {
            // Host responded at the TCP stack (SYN-ACK or RST), so the host is alive; absence of ICMP Echo Reply means ICMP is filtered/blocked
            icmpStatus = IcmpProbeStatus.FilteredOrBlocked;
            icmpLossPct = null;
        }
        else if (explicitDestinationUnreachableCount > 0 || tcpState == TcpConnectionProbeState.NetworkUnreachable)
        {
            // Explicit ICMP DestinationUnreachable reply or OS routing table NetworkUnreachable confirms destination unreachability
            icmpStatus = IcmpProbeStatus.Unreachable;
            icmpLossPct = 100.0;
        }
        else if (sent < 2 || tcpState == TcpConnectionProbeState.FilteredPort)
        {
            // Insufficient samples (< 2) or filtered port without definitive unreachability -> Inconclusive (never claim 100% packet loss without sufficient evidence)
            icmpStatus = IcmpProbeStatus.Inconclusive;
            icmpLossPct = null;
        }
        else
        {
            // Multiple ICMP probes failed (>= 2) AND TCP timed out / failed to reach destination
            icmpStatus = IcmpProbeStatus.Unreachable;
            icmpLossPct = 100.0;
        }

        string icmpText = icmpStatus switch
        {
            IcmpProbeStatus.Responded => $"{icmpAvgMs:F1} ms (Perda ICMP: 0.0%)",
            IcmpProbeStatus.PartialLoss => $"{icmpAvgMs:F1} ms (Perda ICMP: {icmpLossPct:F1}%)",
            IcmpProbeStatus.FilteredOrBlocked => "Não mensurável (ICMP Filtrado/Bloqueado)",
            IcmpProbeStatus.Inconclusive => "Inconclusivo (Evidência insuficiente)",
            IcmpProbeStatus.NotTested => "Não testado",
            _ => "Inacessível (100.0% perda ICMP)"
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
        int explicitUnreachable = 0;

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
                else if (reply.Status is IPStatus.DestinationHostUnreachable
                         or IPStatus.DestinationNetworkUnreachable
                         or IPStatus.DestinationUnreachable)
                {
                    explicitUnreachable++;
                }
            }
            catch { }

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
            tcpResult.LatencyMs,
            explicitUnreachable
        );

        var report = BuildAggregateReportFromTargets(
            targetHost,
            new[] { targetProbe },
            dnsResult.LatencyMs,
            dnsResult.State
        );

        RecordHistoryEntry($"[{DateTime.UtcNow:HH:mm:ss} UTC] {report.DiagnosticSummary}");
        return report;
    }

    /// <summary>
    /// Multi-target real network diagnostic across verified TCP/HTTPS and ICMP endpoints.
    /// Keeps ICMP latency/loss, TCP handshake states/time, and DNS resolution strictly separated.
    /// </summary>
    public async Task<NetworkDiagnosticsReport> RunRealDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        // Probe independent endpoints concurrently. The previous sequential implementation
        // could exceed the native agent's 15-second request deadline when several hosts timed out.
        var targetTask = Task.WhenAll(
            DefaultVerifiedEndpoints.Select(endpoint => ProbeEndpointAsync(endpoint, cancellationToken))
        );
        var dnsTask = MeasureDnsResolutionAsync("steampowered.com", cancellationToken);

        await Task.WhenAll(targetTask, dnsTask);
        cancellationToken.ThrowIfCancellationRequested();

        var targetResults = await targetTask;
        var dnsProbe = await dnsTask;
        var report = BuildAggregateReportFromTargets(
            "Multi-Target (Cloudflare / Google / Quad9 / OpenDNS)",
            targetResults,
            dnsProbe.LatencyMs,
            dnsProbe.State
        );

        RecordHistoryEntry($"[{DateTime.UtcNow:HH:mm:ss} UTC] {report.DiagnosticSummary}");
        return report;
    }

    private static async Task<NetworkTargetProbeResult> ProbeEndpointAsync(
        VerifiedDiagnosticEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        const int icmpSent = 3;
        int explicitUnreachable = 0;
        var icmpSamples = new List<double>();

        // Run TCP handshake in parallel with the three ICMP samples to keep the
        // total request comfortably below the local agent's timeout.
        var tcpTask = ProbeTcpEndpointAsync(endpoint.IpAddress, endpoint.TcpPort, 1500, cancellationToken);

        using (var ping = new Ping())
        {
            for (int i = 0; i < icmpSent; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reply = await ping.SendPingAsync(endpoint.IpAddress, 1200);
                    if (reply.Status == IPStatus.Success)
                    {
                        icmpSamples.Add(reply.RoundtripTime);
                    }
                    else if (reply.Status is IPStatus.DestinationHostUnreachable
                             or IPStatus.DestinationNetworkUnreachable
                             or IPStatus.DestinationUnreachable)
                    {
                        explicitUnreachable++;
                    }
                }
                catch (PingException) { }
                catch (SocketException) { }
            }
        }

        var tcpProbe = await tcpTask;
        return EvaluateTargetProbeOutcome(
            endpoint.Name,
            endpoint.IpAddress,
            endpoint.TcpPort,
            icmpSent,
            icmpSamples,
            tcpProbe.State,
            tcpProbe.LatencyMs,
            explicitUnreachable
        );
    }

    /// <summary>
    /// Executes a real external HTTPS download throughput measurement against Cloudflare Speed CDN.
    /// Never fabricates or simulates throughput values.
    /// </summary>
    public async Task<ExternalThroughputResult> MeasureExternalThroughputAsync(CancellationToken cancellationToken = default)
    {
        const string sourceUrl = "https://speed.cloudflare.com/__down?bytes=2000000";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            var sw = Stopwatch.StartNew();
            using var response = await SharedHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string err = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                RecordHistoryEntry($"[{DateTime.UtcNow:HH:mm:ss} UTC] Throughput externo falhou: {err}");
                return new ExternalThroughputResult(false, null, 0, 0, sourceUrl, err);
            }

            byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            sw.Stop();

            long bytes = payload.LongLength;
            long durationMs = Math.Max(1L, sw.ElapsedMilliseconds);
            double seconds = sw.Elapsed.TotalSeconds;

            if (bytes <= 0 || seconds <= 0)
            {
                return new ExternalThroughputResult(false, null, 0, durationMs, sourceUrl, "Transferência vazia ou tempo inválido.");
            }

            double mbps = Math.Round((bytes * 8.0) / (seconds * 1_000_000.0), 1);
            RecordHistoryEntry($"[{DateTime.UtcNow:HH:mm:ss} UTC] Throughput externo medido: {mbps:F1} Mbps ({bytes / 1024} KB em {durationMs} ms)");

            return new ExternalThroughputResult(
                Available: true,
                DownloadMbps: mbps,
                BytesTransferred: bytes,
                DurationMs: durationMs,
                SourceUrl: sourceUrl,
                ErrorReason: null
            );
        }
        catch (Exception ex)
        {
            string msg = $"Origem externa inacessível neste ambiente: {ex.Message}";
            RecordHistoryEntry($"[{DateTime.UtcNow:HH:mm:ss} UTC] Throughput externo indisponível: {ex.Message}");
            return new ExternalThroughputResult(false, null, 0, 0, sourceUrl, msg);
        }
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
                TargetHost: summaryLabel,
                AverageLatencyMs: 0,
                JitterMs: 0,
                PacketLossPercent: null,
                IsStableForGaming: false,
                DiagnosticSummary: "Nenhum alvo testado (Inconclusivo).",
                DnsResolutionMs: dnsLatencyMs,
                TargetResults: Array.Empty<NetworkTargetProbeResult>(),
                StructuredFinding: null,
                TcpReachabilityPercent: 0.0,
                IcmpStatus: IcmpProbeStatus.Inconclusive,
                IcmpPacketLossPercent: null,
                TcpState: TcpConnectionProbeState.ConnectionFailure,
                DnsState: dnsState,
                AverageIcmpLatencyMs: null,
                AverageTcpHandshakeMs: null,
                EndpointDetails: Array.Empty<NetworkEndpointTestResult>(),
                DnsChecks: Array.Empty<DnsQueryResult>()
            );
        }

        int tcpConnectedCount = targetResults.Count(t => t.IsTcpConnected);
        double tcpReachabilityPct = Math.Round((double)tcpConnectedCount / targetResults.Count * 100.0, 1);

        var icmpMeasurableTargets = targetResults
            .Where(t => t.IcmpStatus is IcmpProbeStatus.Responded or IcmpProbeStatus.PartialLoss && t.IcmpPacketLossPercent.HasValue)
            .ToList();

        IcmpProbeStatus overallIcmpStatus;
        double? overallIcmpLossPercent;

        if (icmpMeasurableTargets.Count > 0)
        {
            double avgIcmpLoss = Math.Round(icmpMeasurableTargets.Average(t => t.IcmpPacketLossPercent!.Value), 1);
            overallIcmpLossPercent = avgIcmpLoss;
            overallIcmpStatus = avgIcmpLoss == 0.0 ? IcmpProbeStatus.Responded : IcmpProbeStatus.PartialLoss;
        }
        else if (targetResults.Any(t => t.IcmpStatus == IcmpProbeStatus.FilteredOrBlocked))
        {
            // ICMP is filtered/blocked while host is reachable over TCP: PacketLossPercent MUST remain null (never 0.0 or 100.0!)
            overallIcmpStatus = IcmpProbeStatus.FilteredOrBlocked;
            overallIcmpLossPercent = null;
        }
        else if (targetResults.All(t => t.IcmpStatus == IcmpProbeStatus.Unreachable))
        {
            overallIcmpStatus = IcmpProbeStatus.Unreachable;
            overallIcmpLossPercent = 100.0;
        }
        else
        {
            overallIcmpStatus = IcmpProbeStatus.Inconclusive;
            overallIcmpLossPercent = null;
        }

        // Keep ICMP latency and TCP handshake time strictly separated!
        var icmpLatencies = targetResults.Where(t => t.IcmpLatencyMs.HasValue).Select(t => t.IcmpLatencyMs!.Value).ToList();
        var tcpLatencies = targetResults.Where(t => t.TcpHandshakeMs.HasValue).Select(t => t.TcpHandshakeMs!.Value).ToList();

        double? avgIcmpLatencyMs = icmpLatencies.Count > 0 ? Math.Round(icmpLatencies.Average(), 1) : null;
        double? avgTcpHandshakeMs = tcpLatencies.Count > 0 ? Math.Round(tcpLatencies.Average(), 1) : null;

        double primaryDisplayLatency = avgIcmpLatencyMs ?? avgTcpHandshakeMs ?? 0.0;
        double jitter = icmpLatencies.Count > 1
            ? CalculateJitter(icmpLatencies)
            : CalculateJitter(tcpLatencies);

        var overallTcpState = tcpConnectedCount > 0
            ? TcpConnectionProbeState.Connected
            : targetResults[0].TcpState;

        bool isStable = (icmpLatencies.Count > 0 || tcpConnectedCount > 0) &&
                        primaryDisplayLatency < 85.0 &&
                        jitter < 15.0 &&
                        (!overallIcmpLossPercent.HasValue || overallIcmpLossPercent.Value < 2.0) &&
                        overallIcmpStatus != IcmpProbeStatus.Unreachable &&
                        dnsState is not (DnsResolutionState.DnsResolutionFailure or DnsResolutionState.ServerUnreachable);

        var finding = BuildStructuredFinding(
            summaryLabel,
            avgIcmpLatencyMs,
            avgTcpHandshakeMs,
            jitter,
            overallIcmpStatus,
            overallIcmpLossPercent,
            overallTcpState,
            tcpReachabilityPct,
            dnsState
        );

        var uiEndpoints = targetResults.Select(t => new NetworkEndpointTestResult(
            Name: $"{t.TargetName} ({t.HostOrIp})",
            IcmpRoundtripMs: t.IcmpLatencyMs.HasValue ? $"{t.IcmpLatencyMs.Value:F1}" : "N/M (Filtrado)",
            TcpConnectMs: t.TcpHandshakeMs.HasValue ? $"{t.TcpHandshakeMs.Value:F1}" : t.TcpState.ToString(),
            Note: t.StatusDescription
        )).ToList();

        var uiDns = new List<DnsQueryResult>
        {
            new(
                Hostname: "steampowered.com",
                Status: dnsState.ToString(),
                ResolutionMs: dnsLatencyMs.HasValue ? (long)Math.Round(dnsLatencyMs.Value) : -1L
            )
        };

        string icmpSummaryStr = overallIcmpLossPercent.HasValue
            ? $"Perda ICMP: {overallIcmpLossPercent.Value:F1}% ({overallIcmpStatus})"
            : overallIcmpStatus == IcmpProbeStatus.FilteredOrBlocked
                ? "Perda ICMP: Não mensurável (ICMP Filtrado)"
                : "Perda ICMP: Inconclusivo";

        string icmpRttStr = avgIcmpLatencyMs.HasValue ? $"{avgIcmpLatencyMs.Value:F1} ms" : "N/M";
        string tcpRttStr = avgTcpHandshakeMs.HasValue ? $"{avgTcpHandshakeMs.Value:F1} ms" : "N/M";

        string summary = $"{icmpSummaryStr} | Latência ICMP: {icmpRttStr} | Handshake TCP: {tcpRttStr} ({overallTcpState}, {tcpReachabilityPct:F0}%) | DNS: {dnsState} | Jitter: {jitter:F1} ms";

        return new NetworkDiagnosticsReport(
            TargetHost: summaryLabel,
            AverageLatencyMs: primaryDisplayLatency,
            JitterMs: jitter,
            PacketLossPercent: overallIcmpLossPercent,
            IsStableForGaming: isStable,
            DiagnosticSummary: summary,
            DnsResolutionMs: dnsLatencyMs,
            TargetResults: targetResults,
            StructuredFinding: finding,
            TcpReachabilityPercent: tcpReachabilityPct,
            IcmpStatus: overallIcmpStatus,
            IcmpPacketLossPercent: overallIcmpLossPercent,
            TcpState: overallTcpState,
            DnsState: dnsState,
            AverageIcmpLatencyMs: avgIcmpLatencyMs,
            AverageTcpHandshakeMs: avgTcpHandshakeMs,
            EndpointDetails: uiEndpoints,
            DnsChecks: uiDns
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
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    (null, TcpConnectionProbeState.NetworkUnreachable),
                SocketError.AccessDenied =>
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
        catch (SocketException sockEx) when (sockEx.SocketErrorCode is SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.TimedOut)
        {
            return (null, DnsResolutionState.ServerUnreachable);
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

    private void RecordHistoryEntry(string entry)
    {
        _diagnosticHistory.Enqueue(entry);
        while (_diagnosticHistory.Count > 50)
        {
            _diagnosticHistory.TryDequeue(out _);
        }
    }

    private static StructuredNetworkDiagnosticFinding BuildStructuredFinding(
        string testedScope,
        double? avgIcmpLatencyMs,
        double? avgTcpHandshakeMs,
        double jitterMs,
        IcmpProbeStatus icmpStatus,
        double? icmpLossPercent,
        TcpConnectionProbeState tcpState,
        double tcpReachabilityPct,
        DnsResolutionState dnsState)
    {
        if (dnsState is DnsResolutionState.DnsResolutionFailure or DnsResolutionState.ServerUnreachable)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Falha de resolução de nomes DNS ({dnsState})",
                EvidenceFound: $"Estado DNS: {dnsState} | Estado TCP: {tcpState} | Estado ICMP: {icmpStatus}",
                ProbableCause: "Servidor DNS configurado indisponível, bloqueio na porta UDP 53 ou queda de link.",
                RecommendedSolution: "Verifique a configuração de DNS do adaptador (ex.: 1.1.1.1 ou 8.8.8.8).",
                VerificationSteps: "Execute novamente o benchmark DNS no Network Lab."
            );
        }

        if (icmpStatus == IcmpProbeStatus.FilteredOrBlocked)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: "ICMP Echo bloqueado/filtrado (Perda ICMP Não Mensurável); pilha TCP alcançável",
                EvidenceFound: $"Estado ICMP: {icmpStatus} (Perda ICMP: Não mensurável) | Estado TCP: {tcpState} ({tcpReachabilityPct:F0}%) | Handshake TCP: {(avgTcpHandshakeMs.HasValue ? $"{avgTcpHandshakeMs:F1} ms" : "N/M")}",
                ProbableCause: "Firewall local, roteador ou provedor bloqueando pacotes ICMP Echo Request sem impedir tráfego TCP.",
                RecommendedSolution: "Nenhuma intervenção obrigatória; o bloqueio ICMP torna a medição de perda ICMP inconclusiva, mantendo o TCP independente.",
                VerificationSteps: "Verifique regras de ICMP no firewall caso deseje telemetria via Ping."
            );
        }

        if (icmpStatus == IcmpProbeStatus.Inconclusive)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: "Teste ICMP inconclusivo por evidência insuficiente",
                EvidenceFound: $"Estado ICMP: {icmpStatus} (Perda ICMP: Inconclusiva) | Estado TCP: {tcpState}",
                ProbableCause: "Quantidade de amostras insuficiente ou filtragem intermediária sem confirmação de queda total.",
                RecommendedSolution: "Execute novamente o diagnóstico com no mínimo 3 amostras por destino.",
                VerificationSteps: "Confirme estado Responded ou FilteredOrBlocked na próxima bateria."
            );
        }

        if (tcpState != TcpConnectionProbeState.Connected && icmpStatus == IcmpProbeStatus.Unreachable)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Inacessibilidade comprovada ICMP ({icmpStatus}) e falha TCP ({tcpState})",
                EvidenceFound: $"Estado ICMP: {icmpStatus} (100.0% perda) | Estado TCP: {tcpState} | Estado DNS: {dnsState}",
                ProbableCause: "Sem conectividade externa à internet ou bloqueio total de saída no gateway.",
                RecommendedSolution: "Verifique o cabo Ethernet/Wi-Fi e o gateway padrão.",
                VerificationSteps: "Reexecute o teste no Network Lab."
            );
        }

        if (icmpLossPercent.HasValue && icmpLossPercent.Value >= 2.0)
        {
            return new StructuredNetworkDiagnosticFinding(
                WhatWasTested: testedScope,
                WhatFailed: $"Perda parcial comprovada de pacotes ICMP ({icmpLossPercent.Value:F1}%)",
                EvidenceFound: $"Estado ICMP: {icmpStatus} ({icmpLossPercent.Value:F1}%) | Latência ICMP: {(avgIcmpLatencyMs.HasValue ? $"{avgIcmpLatencyMs:F1} ms" : "N/M")} | Handshake TCP: {(avgTcpHandshakeMs.HasValue ? $"{avgTcpHandshakeMs:F1} ms" : "N/M")} | Jitter: {jitterMs:F1} ms",
                ProbableCause: "Congestionamento na rede local, interferência Wi-Fi ou saturação no provedor.",
                RecommendedSolution: "Prefira cabo Ethernet e feche downloads em segundo plano.",
                VerificationSteps: "Execute nova bateria ICMP e confirme perda 0%."
            );
        }

        return new StructuredNetworkDiagnosticFinding(
            WhatWasTested: testedScope,
            WhatFailed: "Nenhuma falha detectada",
            EvidenceFound: $"Estado ICMP: {icmpStatus} (Perda: {icmpLossPercent ?? 0.0:F1}%) | Latência ICMP: {(avgIcmpLatencyMs.HasValue ? $"{avgIcmpLatencyMs:F1} ms" : "N/M")} | Handshake TCP: {(avgTcpHandshakeMs.HasValue ? $"{avgTcpHandshakeMs:F1} ms" : "N/M")} ({tcpState}) | DNS: {dnsState}",
            ProbableCause: "Rota direta saudável com respostas ICMP, TCP e DNS independentes verificadas.",
            RecommendedSolution: "Mantenha a rota direta atual.",
            VerificationSteps: "Monitoramento contínuo disponível no Network Lab."
        );
    }
}
