using System;
using System.Collections.Generic;
using PCControlUltimate.Gaming.Network;
using Xunit;

namespace PCControlUltimate.Tests;

public class GameNetworkAcceleratorTests
{
    [Fact]
    public void ComputeStatisticalProbeSummary_ShouldCalculateMeanJitterAndPacketLossCorrectly()
    {
        var samples = new List<double> { 20.0, 22.0, 24.0, 26.0 };
        var summary = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            targetLabel: "ValveGRU",
            hostOrIp: "155.133.248.34",
            port: 27015,
            probeMethod: "ICMP Echo Multi-Amostra",
            samplesAttempted: 5,
            validRttSamplesMs: samples
        );

        Assert.Equal(5, summary.SamplesAttempted);
        Assert.Equal(4, summary.SamplesSucceeded);
        Assert.Equal(20.0, summary.PacketLossPercent);
        Assert.Equal(23.0, summary.AverageRttMs);
        Assert.NotNull(summary.JitterMs);
        Assert.True(summary.JitterMs > 2.0 && summary.JitterMs < 2.5);
        Assert.True(summary.IsReliable);
    }

    [Fact]
    public void EvaluateHonestRouteComparison_ShouldRecommendDirectRoute_WhenRelayIsSlowerOrEqual()
    {
        var directRoute = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Direct", "155.133.248.34", 27015, "ICMP", 5, new[] { 18.0, 19.0, 18.5, 19.5, 18.0 }
        );

        var relayNode = new RelayNodeCandidate(
            "sa-gru-01", "São Paulo Relay", "SA-EAST-1", "203.0.113.10", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }, true
        );

        var slowerRelayProbe = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Relay", "203.0.113.10", 51820, "ICMP", 5, new[] { 24.0, 25.0, 24.5, 25.5, 24.0 }
        );

        var eval = GameNetworkAcceleratorService.EvaluateHonestRouteComparison(
            "Counter-Strike 2", "155.133.248.34", 27015, directRoute, relayNode, slowerRelayProbe, estimatedRelayToGameLegMs: 2.0
        );

        Assert.False(eval.ShouldUseRelay);
        Assert.Equal("Direct", eval.RecommendedMode);
        Assert.Contains("MANTER ROTA DIRETA", eval.HonestVerdictPt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateHonestRouteComparison_ShouldNotRecommendRelay_WhenRelayToGameLegWasNotMeasured()
    {
        var direct = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Direct", "155.133.248.34", 27015, "ICMP", 5, new[] { 60.0, 61.0, 60.5, 60.0, 61.0 });
        var relay = new RelayNodeCandidate(
            "sa-gru-unmeasured", "Unmeasured Relay", "SA-EAST-1", "203.0.113.40", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }, true);
        var relayIngress = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Relay ingress", "203.0.113.40", 51820, "ICMP", 5, new[] { 15.0, 15.5, 15.0, 14.5, 15.0 });

        var evaluation = GameNetworkAcceleratorService.EvaluateHonestRouteComparison(
            "CS2", "155.133.248.34", 27015, direct, relay, relayIngress);

        Assert.False(evaluation.ShouldUseRelay);
        Assert.Equal("Direct", evaluation.RecommendedMode);
        Assert.Null(evaluation.EstimatedTotalRelayRttMs);
        Assert.Contains("segundo trecho", evaluation.HonestVerdictPt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateHonestRouteComparison_ShouldNotRecommendRelay_WhenDirectBaselineIsUnreliable()
    {
        var direct = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Direct", "155.133.248.34", 27015, "ICMP", 5, Array.Empty<double>());
        var relay = new RelayNodeCandidate(
            "sa-gru-no-direct", "Relay", "SA-EAST-1", "203.0.113.41", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }, true);
        var relayIngress = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Relay ingress", "203.0.113.41", 51820, "ICMP", 5, new[] { 15.0, 15.5, 15.0, 14.5, 15.0 });

        var evaluation = GameNetworkAcceleratorService.EvaluateHonestRouteComparison(
            "CS2", "155.133.248.34", 27015, direct, relay, relayIngress, estimatedRelayToGameLegMs: 4.0);

        Assert.False(evaluation.ShouldUseRelay);
        Assert.Equal("Direct", evaluation.RecommendedMode);
        Assert.Contains("amostras confiáveis", evaluation.HonestVerdictPt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateHonestRouteComparison_ShouldRecommendRelay_OnlyWhenLatencyImprovementIsStatisticallySignificant()
    {
        // Bad direct route due to suboptimal ISP peering (65 ms)
        var directRoute = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Direct", "155.133.248.34", 27015, "ICMP", 5, new[] { 64.0, 66.0, 65.0, 64.5, 65.5 }
        );

        var relayNode = new RelayNodeCandidate(
            "sa-gru-fast", "São Paulo Direct Peering Relay", "SA-EAST-1", "203.0.113.20", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }, true
        );

        // Relay ingress is 18 ms + 4 ms leg to game server = 22 ms total (43 ms faster than direct!)
        var fastRelayProbe = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Relay", "203.0.113.20", 51820, "ICMP", 5, new[] { 18.0, 18.5, 17.5, 18.0, 18.0 }
        );

        var eval = GameNetworkAcceleratorService.EvaluateHonestRouteComparison(
            "Counter-Strike 2", "155.133.248.34", 27015, directRoute, relayNode, fastRelayProbe, estimatedRelayToGameLegMs: 4.0
        );

        Assert.True(eval.ShouldUseRelay);
        Assert.Equal("Relay:sa-gru-fast", eval.RecommendedMode);
    }

    [Fact]
    public void WireGuardTunnelManager_RouteTableComparison_RequiresExactPreTunnelRouteRecovery()
    {
        const string before = """
            IPv4 Route Table
            Active Routes:
            Network Destination        Netmask          Gateway       Interface  Metric
            0.0.0.0                    0.0.0.0          192.168.1.1   192.168.1.100  25
            192.168.1.0                255.255.255.0    On-link       192.168.1.100  281
            """;
        const string same = """
            IPv4 Route Table
            Active Routes:
            Network Destination        Netmask          Gateway       Interface  Metric
            0.0.0.0                    0.0.0.0          192.168.1.1   192.168.1.100  25
            192.168.1.0                255.255.255.0    On-link       192.168.1.100  281
            """;
        const string changedDefaultRoute = """
            IPv4 Route Table
            Active Routes:
            Network Destination        Netmask          Gateway       Interface  Metric
            0.0.0.0                    0.0.0.0          10.66.66.1    10.66.66.2  5
            192.168.1.0                255.255.255.0    On-link       192.168.1.100  281
            """;

        Assert.True(WireGuardTunnelManager.AreIpv4RouteTablesEquivalent(before, same));
        Assert.False(WireGuardTunnelManager.AreIpv4RouteTablesEquivalent(before, changedDefaultRoute));
        Assert.False(WireGuardTunnelManager.AreIpv4RouteTablesEquivalent(before, string.Empty));
    }

    [Fact]
    public void WireGuardTunnelManager_Ipv6RouteTableComparison_DetectsDefaultRouteChanges()
    {
        const string before = """
            IPv6 Route Table
            Active Routes:
            If Metric Network Destination      Gateway
            1 331 ::1/128                       On-link
            1 281 fe80::/64                     On-link
            """;
        const string same = """
            IPv6 Route Table
            Active Routes:
            If Metric Network Destination      Gateway
            1 331 ::1/128                       On-link
            1 281 fe80::/64                     On-link
            """;
        const string changed = """
            IPv6 Route Table
            Active Routes:
            If Metric Network Destination      Gateway
            1 331 ::1/128                       On-link
            1 5 ::/0                            fe80::1
            """;

        Assert.True(WireGuardTunnelManager.AreIpv6RouteTablesEquivalent(before, same));
        Assert.False(WireGuardTunnelManager.AreIpv6RouteTablesEquivalent(before, changed));
        Assert.False(WireGuardTunnelManager.AreIpv6RouteTablesEquivalent(before, string.Empty));
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldBlockIpv4DefaultRouteOverride_WhenSelectiveRoutingRequired()
    {
        string ipv4FullTunnelConfig = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32

            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 0.0.0.0/0
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(ipv4FullTunnelConfig, allowDefaultRouteOverride: false);

        Assert.False(audit.IsValid);
        Assert.True(audit.AltersDefaultRoute);
        Assert.True(audit.AltersDefaultWindowsRoute);
        Assert.True(audit.AltersIpv4DefaultRoute);
        Assert.False(audit.AltersIpv6DefaultRoute);
        Assert.False(audit.IsSelectiveGameTargetRouting);
        Assert.Contains("DEFAULT_ROUTE_OVERRIDE_BLOCKED", audit.ErrorReason);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldBlockIpv6DefaultRouteOverride_WhenSelectiveRoutingRequired()
    {
        string ipv6FullTunnelConfig = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32, fd00:66::2/128

            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = ::/0
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(ipv6FullTunnelConfig, allowDefaultRouteOverride: false);

        Assert.False(audit.IsValid);
        Assert.True(audit.AltersDefaultRoute);
        Assert.True(audit.AltersDefaultWindowsRoute);
        Assert.False(audit.AltersIpv4DefaultRoute);
        Assert.True(audit.AltersIpv6DefaultRoute);
        Assert.False(audit.IsSelectiveGameTargetRouting);
        Assert.Contains("IPv6 (::/0)", audit.ErrorReason);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldDetectBothIpv4AndIpv6DefaultRoutes_AndNeverPresentAsSelective()
    {
        string dualFullTunnelConfig = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32, fd00:66::2/128

            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 0.0.0.0/0, ::/0
            """;

        var blockedAudit = WireGuardTunnelManager.ValidateAndAuditConfig(dualFullTunnelConfig, allowDefaultRouteOverride: false);
        Assert.False(blockedAudit.IsValid);
        Assert.True(blockedAudit.AltersDefaultRoute);
        Assert.True(blockedAudit.AltersIpv4DefaultRoute);
        Assert.True(blockedAudit.AltersIpv6DefaultRoute);
        Assert.False(blockedAudit.IsSelectiveGameTargetRouting);

        // Even when full-tunnel override is explicitly authorized, it MUST NOT be marked as selective game routing
        var authorizedGlobalAudit = WireGuardTunnelManager.ValidateAndAuditConfig(dualFullTunnelConfig, allowDefaultRouteOverride: true);
        Assert.True(authorizedGlobalAudit.IsValid);
        Assert.True(authorizedGlobalAudit.AltersDefaultRoute);
        Assert.True(authorizedGlobalAudit.AltersIpv4DefaultRoute);
        Assert.True(authorizedGlobalAudit.AltersIpv6DefaultRoute);
        Assert.False(authorizedGlobalAudit.IsSelectiveGameTargetRouting);
        Assert.Contains("Global", authorizedGlobalAudit.RouteImpactExplanationPt);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldDetectSplitCidrsThatCoverTheEntireAddressSpace()
    {
        string splitIpv4FullTunnel = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32
            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 0.0.0.0/2, 64.0.0.0/2, 128.0.0.0/2, 192.0.0.0/2
            """;
        string splitIpv6FullTunnel = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = fd00:66::2/128
            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = ::/2, 4000::/2, 8000::/2, c000::/2
            """;

        var ipv4Audit = WireGuardTunnelManager.ValidateAndAuditConfig(splitIpv4FullTunnel);
        Assert.False(ipv4Audit.IsValid);
        Assert.True(ipv4Audit.AltersIpv4DefaultRoute);

        var ipv6Audit = WireGuardTunnelManager.ValidateAndAuditConfig(splitIpv6FullTunnel);
        Assert.False(ipv6Audit.IsValid);
        Assert.True(ipv6Audit.AltersIpv6DefaultRoute);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldRejectDisabledRouteTableAndMultiplePeers()
    {
        string tableOffConfig = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32
            Table = off
            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 155.133.248.0/24
            """;
        string multiplePeersConfig = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32
            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 155.133.248.0/24
            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.11:51820
            AllowedIPs = 155.133.249.0/24
            """;

        var tableAudit = WireGuardTunnelManager.ValidateAndAuditConfig(tableOffConfig);
        Assert.False(tableAudit.IsValid);
        Assert.Contains("UNSUPPORTED_ROUTE_TABLE_MODE", tableAudit.ErrorReason);

        var peersAudit = WireGuardTunnelManager.ValidateAndAuditConfig(multiplePeersConfig);
        Assert.False(peersAudit.IsValid);
        Assert.Contains("MULTIPLE_PEER_SECTIONS", peersAudit.ErrorReason);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldRejectGlobalDnsOverrideInGameTunnelConfig()
    {
        string config = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32
            DNS = 1.1.1.1
            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 155.133.248.0/24
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(config);

        Assert.False(audit.IsValid);
        Assert.Contains("GLOBAL_DNS_OVERRIDE_BLOCKED", audit.ErrorReason);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldBlockMaliciousPreUpPostUpScriptHooks()
    {
        string maliciousConfig = """
            [Interface]
            PrivateKey = wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=
            Address = 10.66.66.2/32
            PostUp = powershell.exe -EncodedCommand ZWNobyBoYWNrZWQ=

            [Peer]
            PublicKey = mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=
            Endpoint = 203.0.113.10:51820
            AllowedIPs = 155.133.248.0/24
            """;

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(maliciousConfig, allowDefaultRouteOverride: true);

        Assert.False(audit.IsValid);
        Assert.Contains("UNSAFE_SCRIPT_HOOK_BLOCKED", audit.ErrorReason);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldAcceptSelectiveGameTargetCidrConfig_WithNeitherDefaultRoutePresent()
    {
        var node = new RelayNodeCandidate(
            "sa-gru-01", "São Paulo Relay", "SA-EAST-1", "203.0.113.10", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24", "2001:db8:85a3::/48" }, true
        );

        string selectiveConfig = WireGuardTunnelManager.BuildSelectiveGameTunnelConfig(
            "wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=",
            "10.66.66.2/32",
            node
        );

        var audit = WireGuardTunnelManager.ValidateAndAuditConfig(selectiveConfig, allowDefaultRouteOverride: false);

        Assert.True(audit.IsValid);
        Assert.False(audit.AltersDefaultRoute);
        Assert.False(audit.AltersDefaultWindowsRoute);
        Assert.False(audit.AltersIpv4DefaultRoute);
        Assert.False(audit.AltersIpv6DefaultRoute);
        Assert.True(audit.IsSelectiveGameTargetRouting);
        Assert.Equal(2, audit.AllowedIps.Count);
    }

    [Fact]
    public void WireGuardTunnelManager_ShouldNotInventGameCidrs_WhenNodeHasNoTargets()
    {
        var node = new RelayNodeCandidate(
            "sa-gru-empty", "Empty Relay", "SA-EAST-1", "203.0.113.30", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            Array.Empty<string>(), true);

        var exception = Assert.Throws<ArgumentException>(() =>
            WireGuardTunnelManager.BuildSelectiveGameTunnelConfig(
                "wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=",
                "10.66.66.2/32",
                node));

        Assert.Contains("Nenhum CIDR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterOrUpdateRelayNode_ShouldValidateHostPortAndCidrs_AndRecordEvaluationHistory()
    {
        GameNetworkAcceleratorService.ClearNodesForTesting();

        var invalidHost = GameNetworkAcceleratorService.RegisterOrUpdateRelayNode(
            "node-1", "Bad Host", "SA", "invalid..host", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }
        );
        Assert.False(invalidHost.success);
        Assert.Contains("INVALID_ENDPOINT_HOST", invalidHost.errorReason);

        var invalidPort = GameNetworkAcceleratorService.RegisterOrUpdateRelayNode(
            "node-1", "Bad Port", "SA", "203.0.113.10", 70000,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }
        );
        Assert.False(invalidPort.success);
        Assert.Contains("INVALID_ENDPOINT_PORT", invalidPort.errorReason);

        var validNode = GameNetworkAcceleratorService.RegisterOrUpdateRelayNode(
            "node-valid", "Valid Node", "SA-EAST-1", "203.0.113.10", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }
        );
        Assert.True(validNode.success);
        Assert.NotNull(validNode.node);

        var (wgInstalled, wgExe, _) = WireGuardTunnelManager.CheckWireGuardInstallation();
        if (!wgInstalled)
        {
            Assert.Null(wgExe);
        }

        var directProbe = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Direct", "155.133.248.34", 27015, "ICMP", 5, new[] { 20.0, 21.0, 20.5, 20.0, 20.5 }
        );
        var relayProbe = GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            "Relay", "203.0.113.10", 51820, "ICMP", 5, new[] { 25.0, 26.0, 25.5, 25.0, 25.5 }
        );
        GameNetworkAcceleratorService.EvaluateHonestRouteComparison(
            "CS2", "155.133.248.34", 27015, directProbe, validNode.node, relayProbe, 3.0
        );

        Assert.NotEmpty(GameNetworkAcceleratorService.GetEvaluationHistory());
    }

    [Fact]
    public void SelectiveRoutingCapabilityReport_ShouldDiscloseWfpKernelDriverIsDisabled()
    {
        var report = GameNetworkAcceleratorService.GetSelectiveRoutingCapabilityReport();

        Assert.True(report.DestinationIpSelectiveRoutingSupported);
        Assert.False(report.KernelWfpProcessSplitTunnelEnabled);
        Assert.Contains("DESATIVADO", report.WfpLimitationExplanationPt);
        Assert.Contains("DISABLED", report.WfpLimitationExplanationEn);
        Assert.DoesNotContain("guarantee zero conflict", report.WfpLimitationExplanationEn, StringComparison.OrdinalIgnoreCase);

        // Verify dedicated GPUs (e.g., RX 7900 XTX) are distinguished from integrated GPUs (iGPU)
        Assert.True(PCControlUltimate.Hardware.LibreHardwareMonitorWrapper.IsIntegratedGpuName("AMD Radeon(TM) Graphics"));
        Assert.True(PCControlUltimate.Hardware.LibreHardwareMonitorWrapper.IsIntegratedGpuName("Intel(R) UHD Graphics 770"));
        Assert.False(PCControlUltimate.Hardware.LibreHardwareMonitorWrapper.IsIntegratedGpuName("AMD Radeon RX 7900 XTX"));
        Assert.False(PCControlUltimate.Hardware.LibreHardwareMonitorWrapper.IsIntegratedGpuName("ASRock Radeon RX 7900 XTX Taichi White 24G OC"));
    }

    [Fact]
    public async System.Threading.Tasks.Task WireGuardTunnelManager_InstallAndUninstall_WithoutExplicitUserConsent_MustBeBlocked()
    {
        var node = new RelayNodeCandidate(
            "sa-gru-01", "São Paulo Relay", "SA-EAST-1", "203.0.113.10", 51820,
            "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            new[] { "155.133.248.0/24" }, true
        );

        Assert.IsAssignableFrom<IAccelerationNode>(node);

        string selectiveConfig = WireGuardTunnelManager.BuildSelectiveGameTunnelConfig(
            "wJ9K4m8N1pQ6rT3vX5zB7cF0hL2sD4gA8eM1uP9yV3E=",
            "10.66.66.2/32",
            node
        );

        var engine = new GameNetworkAcceleratorEngine();
        Assert.IsAssignableFrom<IAccelerationProvider>(engine);
        Assert.IsAssignableFrom<ITunnelProvider>(engine);
        Assert.IsAssignableFrom<ITrafficRoutingProvider>(engine);
        Assert.IsAssignableFrom<IRouteProbe>(engine);
        Assert.IsAssignableFrom<IGameNetworkDetector>(engine);
        Assert.IsAssignableFrom<INetworkMeasurementService>(engine);
        Assert.IsAssignableFrom<INodeHealthService>(engine);

        var installBlocked = await engine.InstallTunnelWithConsentAsync(
            "PCControlGameTunnel",
            selectiveConfig,
            userConfirmedExecution: false,
            allowDefaultRouteOverride: false
        );
        Assert.False(installBlocked.Success);
        Assert.Equal("USER_CONSENT_REQUIRED", installBlocked.ErrorCode);

        var uninstallBlocked = await engine.RemoveTunnelAndRestoreRoutesAsync(
            "PCControlGameTunnel",
            userConfirmedExecution: false
        );
        Assert.False(uninstallBlocked.Success);
        Assert.Equal("USER_CONSENT_REQUIRED", uninstallBlocked.ErrorCode);
    }
}
