using PCControlUltimate.Gaming.NetworkAcceleration;
using Xunit;

namespace PCControlUltimate.Tests;

public sealed class NetworkAccelerationTests
{
    [Fact]
    public async Task TcpProbe_RejectsWhitespaceHostBeforeNetworkAccess()
    {
        var probe = new NetworkRouteProbe();
        await Assert.ThrowsAsync<ArgumentException>(() => probe.MeasureTcpAsync("bad host", 443));
    }

    [Fact]
    public async Task TcpProbe_RejectsInvalidPortBeforeNetworkAccess()
    {
        var probe = new NetworkRouteProbe();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => probe.MeasureTcpAsync("127.0.0.1", 70000));
    }

    [Fact]
    public async Task TraceRoute_RejectsWhitespaceHostBeforeNetworkAccess()
    {
        var probe = new NetworkRouteProbe();
        await Assert.ThrowsAsync<ArgumentException>(() => probe.TraceRouteAsync("not a host"));
    }

    [Fact]
    public void WireGuardProvider_UsesOfficialWindowsExecutableOnly()
    {
        var provider = new WireGuardWindowsProvider();
        Assert.Equal("wireguard-windows", provider.ProviderId);
        Assert.Equal("WireGuard for Windows (configuração própria)", provider.DisplayName);
        if (!OperatingSystem.IsWindows())
            Assert.False(provider.IsAvailable);
    }
}
