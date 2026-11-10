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

namespace PCControlUltimate.Windows.Services;

public record NetworkEndpointTestResult(
    string Key,
    string EndpointLabel,
    string HostOrIp,
    int Port,
    string TimestampUtc,
    long? IcmpLatencyMs,
    string IcmpStatus,
    double? TcpHandshakeMs,
    string TcpStatus,
    double PacketLossPercent
);

public record DnsQueryResult(
    string Domain,
    string TimestampUtc,
    bool Success,
    double? ResolutionTimeMs,
    string ResolvedAddress,
    string StatusMessage
);

public record ExternalThroughputResult(
    bool Available,
    double? DownloadMbps,
    long BytesTransferred,
    int DurationMs,
    string SourceUrl,
    string TimestampUtc,
    string? ErrorReason
);

public static class NetworkDiagnosticsService
{
    private static readonly ConcurrentQueue<string> _diagnosticHistory = new();

    public static IReadOnlyList<string> GetDiagnosticHistory() => _diagnosticHistory.ToArray();

    /// <summary>
    /// Executes clearly separated ICMP Echo, TCP Handshake, and DNS resolution tests against
    /// explicitly labeled infrastructure endpoints (never falsely claiming generic hosts are inside private game lobbies).
    /// </summary>
    public static async Task<object> RunRealDiagnosticsAsync()
    {
        string timestamp = DateTime.UtcNow.ToString("O");

        var targets = new[]
        {
            (Key: "valve", Label: "Infraestrutura pública Valve/Steam (Borda TCP/ICMP)", Host: "155.133.248.34", Port: 27015),
            (Key: "riot", Label: "Infraestrutura pública Riot Direct (Borda Anycast)", Host: "104.160.152.3", Port: 443),
            (Key: "cloudflare", Label: "Cloudflare Public Anycast DNS (1.1.1.1)", Host: "1.1.1.1", Port: 443),
            (Key: "google", Label: "Google Public Anycast DNS (8.8.8.8)", Host: "8.8.8.8", Port: 443)
        };

        var tcpLatencies = new Dictionary<string, double?>();
        var icmpLatencies = new Dictionary<string, long?>();
        var endpointDetails = new List<NetworkEndpointTestResult>();
        var allSamples = new List<double>();
        int totalProbes = 0;
        int failedProbes = 0;

        foreach (var t in targets)
        {
            // 1. ICMP Echo Ping (Explicitly separated from TCP handshake)
            var (icmpMs, icmpReason) = await MeasureIcmpPingWithStatusAsync(t.Host);
            icmpLatencies[t.Key] = icmpMs;

            // 2. TCP Handshake Latency (3 probes per target)
            var samples = new List<double>();
            int targetFailed = 0;
            for (int i = 0; i < 3; i++)
            {
                totalProbes++;
                double? ms = await MeasureTcpHandshakeMsAsync(t.Host, t.Port, 1500);
                if (ms.HasValue)
                {
                    samples.Add(ms.Value);
                    allSamples.Add(ms.Value);
                }
                else
                {
                    failedProbes++;
                    targetFailed++;
                }
            }

            double? avgTcp = samples.Count > 0 ? Math.Round(samples.Average(), 1) : null;
            tcpLatencies[t.Key] = avgTcp;
            double lossPct = Math.Round(targetFailed / 3.0 * 100.0, 1);

            endpointDetails.Add(new NetworkEndpointTestResult(
                Key: t.Key,
                EndpointLabel: t.Label,
                HostOrIp: t.Host,
                Port: t.Port,
                TimestampUtc: timestamp,
                IcmpLatencyMs: icmpMs,
                IcmpStatus: icmpReason,
                TcpHandshakeMs: avgTcp,
                TcpStatus: avgTcp.HasValue ? $"Conectado ({avgTcp.Value:F1} ms)" : "Indisponível / Timeout TCP",
                PacketLossPercent: lossPct
            ));
        }

        // 3. DNS Resolution Check
        var dnsChecks = new List<DnsQueryResult>
        {
            await MeasureDnsResolutionAsync("steampowered.com"),
            await MeasureDnsResolutionAsync("cloudflare.com")
        };

        double? jitterMs = null;
        if (allSamples.Count > 1)
        {
            double avg = allSamples.Average();
            double variance = allSamples.Select(v => Math.Pow(v - avg, 2)).Average();
            jitterMs = Math.Round(Math.Sqrt(variance), 2);
        }

        double packetLoss = totalProbes > 0 ? Math.Round((double)failedProbes / totalProbes * 100.0, 1) : 100.0;

        string summaryLog = $"[{DateTime.Now:HH:mm:ss}] Cloudflare TCP: {(tcpLatencies["cloudflare"]?.ToString("F1") ?? "N/D")} ms | ICMP: {(icmpLatencies["cloudflare"]?.ToString() ?? "N/D")} ms | Perda: {packetLoss:F1}%";
        _diagnosticHistory.Enqueue(summaryLog);
        while (_diagnosticHistory.Count > 50)
        {
            _diagnosticHistory.TryDequeue(out _);
        }

        return new
        {
            timestampUtc = timestamp,
            measurementType = "Diagnóstico Separado: ICMP Echo + TCP Handshake + Consulta DNS",
            valvePing = tcpLatencies["valve"],
            riotPing = tcpLatencies["riot"],
            cloudflarePing = tcpLatencies["cloudflare"],
            googleDnsPing = tcpLatencies["google"],
            icmpPingMs = icmpLatencies,
            endpointDetails,
            dnsChecks,
            jitterMs,
            packetLoss,
            history = _diagnosticHistory.ToArray()
        };
    }

    public static async Task<ExternalThroughputResult> MeasureExternalThroughputAsync(CancellationToken cancellationToken = default)
    {
        const string sourceUrl = "https://speed.cloudflare.com/__down?bytes=2000000";
        string timestamp = DateTime.UtcNow.ToString("O");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ExternalThroughputResult(false, null, 0, 0, sourceUrl, timestamp,
                    $"Servidor externo retornou HTTP {(int)response.StatusCode}");
            }

            byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            sw.Stop();

            double seconds = sw.Elapsed.TotalSeconds;
            if (seconds <= 0 || payload.Length == 0)
            {
                return new ExternalThroughputResult(false, null, 0, 0, sourceUrl, timestamp,
                    "Nenhum byte transferido durante o teste.");
            }

            double mbps = Math.Round((payload.Length * 8.0) / (seconds * 1_000_000.0), 1);
            _diagnosticHistory.Enqueue($"[{DateTime.Now:HH:mm:ss}] Throughput Externo (Cloudflare CDN): {mbps:F1} Mbps ({payload.Length / 1024} KB em {sw.ElapsedMilliseconds} ms)");

            return new ExternalThroughputResult(true, mbps, payload.Length, (int)sw.ElapsedMilliseconds, sourceUrl, timestamp, null);
        }
        catch (Exception ex)
        {
            return new ExternalThroughputResult(false, null, 0, 0, sourceUrl, timestamp,
                $"Teste de throughput indisponível: {ex.Message}");
        }
    }

    private static async Task<(long? rttMs, string status)> MeasureIcmpPingWithStatusAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1200);
            if (reply.Status == IPStatus.Success)
                return (reply.RoundtripTime, $"ICMP Echo OK ({reply.RoundtripTime} ms)");

            return (null, $"Indisponível ({reply.Status})");
        }
        catch (Exception ex)
        {
            return (null, $"ICMP Bloqueado/Indisponível ({ex.GetType().Name})");
        }
    }

    private static async Task<double?> MeasureTcpHandshakeMsAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<DnsQueryResult> MeasureDnsResolutionAsync(string domain)
    {
        string ts = DateTime.UtcNow.ToString("O");
        try
        {
            var sw = Stopwatch.StartNew();
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(domain);
            sw.Stop();

            if (addresses.Length == 0)
            {
                return new DnsQueryResult(domain, ts, false, null, "N/D", "Nenhum registro A/AAAA retornado.");
            }

            return new DnsQueryResult(
                domain,
                ts,
                true,
                Math.Round(sw.Elapsed.TotalMilliseconds, 1),
                addresses[0].ToString(),
                $"Resolvido ({sw.Elapsed.TotalMilliseconds:F1} ms)"
            );
        }
        catch (Exception ex)
        {
            return new DnsQueryResult(domain, ts, false, null, "N/D", $"Falha DNS: {ex.Message}");
        }
    }
}
