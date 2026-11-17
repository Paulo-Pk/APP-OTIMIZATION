using System.Threading.Tasks;
using PCControlUltimate.Core.Models;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class NetworkDiagnosticsServiceTests
{
    [Fact]
    public async Task GameReferenceMeasurement_RejectsUnknownIdsWithoutProbingArbitraryHosts()
    {
        var service = new NetworkDiagnosticsService();

        var report = await service.MeasureGameReferenceTargetsAsync(new[]
        {
            "unknown-game",
            "127.0.0.1",
            "example.invalid"
        });

        Assert.Empty(report.Results);
        Assert.Equal(0, report.TcpReachabilityPercent);
        Assert.NotNull(report.StructuredDiagnostic);
        Assert.Contains("Nenhum jogo autorizado", report.StructuredDiagnostic!.WhatWasTested);
    }

    [Fact]
    public void EvaluateTargetProbeOutcome_IcmpBlockedButTcpConnected_DoesNotInventPacketLoss()
    {
        var result = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Cloudflare", "1.1.1.1", 443, 4, Array.Empty<double>(),
            TcpConnectionProbeState.Connected, 12.5);

        Assert.Equal(IcmpProbeStatus.FilteredOrBlocked, result.IcmpStatus);
        Assert.Null(result.IcmpPacketLossPercent);
        Assert.True(result.IsTcpConnected);
        Assert.Contains("ICMP Filtrado/Bloqueado", result.StatusDescription);
    }

    [Fact]
    public void EvaluateTargetProbeOutcome_PartialIcmpReplies_CalculatesLossFromActualSamples()
    {
        var result = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Cloudflare", "1.1.1.1", 443, 4, new[] { 10.0, 12.0 },
            TcpConnectionProbeState.Connected, 13.0);

        Assert.Equal(IcmpProbeStatus.PartialLoss, result.IcmpStatus);
        Assert.Equal(50.0, result.IcmpPacketLossPercent!.Value);
        Assert.Equal(11.0, result.IcmpLatencyMs!.Value);
    }

    [Fact]
    public void EvaluateTargetProbeOutcome_MultipleIcmpFailuresAndTcpTimeout_ReportsConfirmedUnreachable()
    {
        var result = NetworkDiagnosticsService.EvaluateTargetProbeOutcome(
            "Endpoint", "192.0.2.1", 443, 4, Array.Empty<double>(),
            TcpConnectionProbeState.Timeout, null);

        Assert.Equal(IcmpProbeStatus.Unreachable, result.IcmpStatus);
        Assert.Equal(100.0, result.IcmpPacketLossPercent!.Value);
        Assert.False(result.IsTcpConnected);
    }
}
