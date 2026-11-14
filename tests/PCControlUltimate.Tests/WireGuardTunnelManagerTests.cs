using PCControlUltimate.Gaming.Network;
using Xunit;

namespace PCControlUltimate.Tests;

public sealed class WireGuardTunnelManagerTests
{
    private const string ValidPrivateKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string ValidPublicKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void CapabilityReport_DoesNotClaimSelectiveRoutingIsAlreadyActive()
    {
        var report = GameNetworkAcceleratorService.GetSelectiveRoutingCapabilityReport();

        Assert.True(report.DestinationIpSelectiveRoutingSupported);
        Assert.False(report.KernelWfpProcessSplitTunnelEnabled);
        Assert.Contains("Not active by default", report.ActiveMechanism);
        Assert.Contains("only after", report.WfpLimitationExplanationEn);
    }

    [Fact]
    public void ConfigAudit_AcceptsExplicitSelectiveGameSubnet()
    {
        string config = $"""
            [Interface]
            PrivateKey = {ValidPrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {ValidPublicKey}
            Endpoint = 192.0.2.10:51820
            AllowedIPs = 10.20.0.0/16
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(config);

        Assert.True(audit.IsValid, audit.ErrorReason);
        Assert.False(audit.AltersDefaultRoute);
        Assert.True(audit.IsSelectiveGameTargetRouting);
        Assert.Contains("10.20.0.0/16", audit.AllowedIps);
    }

    [Fact]
    public void ConfigAudit_BlocksDefaultRouteOverrideWithoutExplicitConsent()
    {
        string config = $"""
            [Interface]
            PrivateKey = {ValidPrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {ValidPublicKey}
            Endpoint = 192.0.2.10:51820
            AllowedIPs = 0.0.0.0/0
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(config);

        Assert.False(audit.IsValid);
        Assert.Contains("DEFAULT_ROUTE_OVERRIDE_BLOCKED", audit.ErrorReason);
    }

    [Fact]
    public void ConfigAudit_RejectsScriptHooks()
    {
        string config = $"""
            [Interface]
            PrivateKey = {ValidPrivateKey}
            Address = 10.0.0.2/32
            PostUp = powershell.exe -Command whoami

            [Peer]
            PublicKey = {ValidPublicKey}
            Endpoint = 192.0.2.10:51820
            AllowedIPs = 10.20.0.0/16
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(config);

        Assert.False(audit.IsValid);
        Assert.Contains("UNSAFE_SCRIPT_HOOK_BLOCKED", audit.ErrorReason);
    }
}
