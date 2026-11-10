using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PCControlUltimate.Windows.Services;

public static class NetworkDiagnosticsService
{
    public static async Task<object> RunRealDiagnosticsAsync()
    {
        var targets = new[]
        {
            (Key: "valve", Host: "155.133.248.34", Port: 27015),
            (Key: "riot", Host: "104.160.152.3", Port: 443),
            (Key: "cloudflare", Host: "1.1.1.1", Port: 443),
            (Key: "google", Host: "8.8.8.8", Port: 443)
        };

        var tcpLatencies = new Dictionary<string, double?>();
        var icmpLatencies = new Dictionary<string, long?>();
        var allSamples = new List<double>();
        int totalProbes = 0;
        int failedProbes = 0;

        foreach (var t in targets)
        {
            // 1. ICMP Echo Ping (when permitted by firewall)
            icmpLatencies[t.Key] = await MeasureIcmpPingMsAsync(t.Host);

            // 2. TCP Handshake Latency (3 probes per target)
            var samples = new List<double>();
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
                }
            }

            tcpLatencies[t.Key] = samples.Count > 0 ? Math.Round(samples.Average(), 1) : null;
        }

        double? jitterMs = null;
        if (allSamples.Count > 1)
        {
            double avg = allSamples.Average();
            double variance = allSamples.Select(v => Math.Pow(v - avg, 2)).Average();
            jitterMs = Math.Round(Math.Sqrt(variance), 2);
        }

        double packetLoss = totalProbes > 0 ? Math.Round((double)failedProbes / totalProbes * 100.0, 1) : 100.0;

        return new
        {
            measurementType = "ICMP + TCP Handshake",
            valvePing = tcpLatencies["valve"],
            riotPing = tcpLatencies["riot"],
            cloudflarePing = tcpLatencies["cloudflare"],
            googleDnsPing = tcpLatencies["google"],
            icmpPingMs = icmpLatencies,
            jitterMs,
            packetLoss
        };
    }

    private static async Task<long?> MeasureIcmpPingMsAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1200);
            if (reply.Status == IPStatus.Success)
                return reply.RoundtripTime;
        }
        catch { }
        return null;
    }

    private static async Task<double?> MeasureTcpHandshakeMsAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
        catch
        {
            return null;
        }
    }
}
