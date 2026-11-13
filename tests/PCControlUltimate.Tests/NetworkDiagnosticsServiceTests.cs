using System.Threading.Tasks;
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
}
