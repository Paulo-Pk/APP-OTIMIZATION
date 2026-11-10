using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Gaming.Network;

public interface IAccelerationNode
{
    string NodeId { get; }
    string DisplayName { get; }
    string RegionCode { get; }
    string EndpointHost { get; }
    int EndpointPort { get; }
    string PublicKey { get; }
    IReadOnlyList<string> GameTargetCidrs { get; }
    bool IsCustomVps { get; }
}

public interface IGameNetworkDetector
{
    IReadOnlyList<DetectedGameConnection> DetectRunningGamesAndConnections();
}

public interface IRouteProbe
{
    Task<MultiSampleProbeSummary> ProbeEndpointAsync(
        string targetLabel,
        string hostOrIp,
        int port = 443,
        int sampleCount = 5,
        bool preferIcmp = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TracerouteHopResult>> TraceRouteAsync(
        string hostOrIp,
        int maxHops = 15,
        int timeoutMs = 900,
        CancellationToken cancellationToken = default);
}

public interface INetworkMeasurementService
{
    MultiSampleProbeSummary ComputeStatisticalSummary(
        string targetLabel,
        string hostOrIp,
        int port,
        string probeMethod,
        int samplesAttempted,
        IReadOnlyList<double> validRttSamplesMs);

    RouteComparisonEvaluation CompareDirectAndRelayRoutes(
        string gameIdentifier,
        string targetServerHost,
        int targetServerPort,
        MultiSampleProbeSummary directRoute,
        RelayNodeCandidate? candidateNode,
        MultiSampleProbeSummary? relayIngressProbe,
        double? estimatedRelayToGameLegMs = null);

    IReadOnlyList<RouteComparisonEvaluation> GetMeasurementHistory();
}

public interface INodeHealthService
{
    IReadOnlyList<RelayNodeCandidate> GetRegisteredNodes();

    Task<MultiSampleProbeSummary> CheckNodeHealthAsync(
        RelayNodeCandidate node,
        int sampleCount = 5,
        CancellationToken cancellationToken = default);
}

public interface ITunnelProvider
{
    (bool installed, string? wireGuardExePath, string? wgCliPath) CheckInstallation();

    WireGuardConfigAuditResult ValidateConfiguration(
        string? configContent,
        bool allowDefaultRouteOverride = false);

    Task<WireGuardTunnelStatusReport> GetTunnelStatusAsync(
        string tunnelName = "PCControlGameTunnel",
        CancellationToken cancellationToken = default);

    Task<WireGuardServiceOperationResult> InstallTunnelWithConsentAsync(
        string tunnelName,
        string configContent,
        bool userConfirmedExecution,
        bool allowDefaultRouteOverride = false,
        CancellationToken cancellationToken = default);

    Task<WireGuardServiceOperationResult> RemoveTunnelAndRestoreRoutesAsync(
        string tunnelName,
        bool userConfirmedExecution,
        CancellationToken cancellationToken = default);
}

public interface ITrafficRoutingProvider
{
    SelectiveRoutingCapabilityReport GetRoutingCapabilities();
    Task<string> CaptureRouteTableSnapshotAsync(CancellationToken cancellationToken = default);
    string? GetLastRouteTableSnapshot();
}

public interface IAccelerationProvider :
    IGameNetworkDetector,
    IRouteProbe,
    INetworkMeasurementService,
    INodeHealthService,
    ITunnelProvider,
    ITrafficRoutingProvider
{
    Task<RouteComparisonEvaluation> AnalyzeGameRouteAsync(
        string gameIdentifier,
        string targetServerHost,
        int targetServerPort = 443,
        int sampleCount = 5,
        CancellationToken cancellationToken = default);
}

public sealed class GameNetworkAcceleratorEngine : IAccelerationProvider
{
    public IReadOnlyList<DetectedGameConnection> DetectRunningGamesAndConnections() =>
        GameNetworkAcceleratorService.DetectRunningGamesAndConnections();

    public Task<MultiSampleProbeSummary> ProbeEndpointAsync(
        string targetLabel,
        string hostOrIp,
        int port = 443,
        int sampleCount = 5,
        bool preferIcmp = true,
        CancellationToken cancellationToken = default) =>
        GameNetworkAcceleratorService.MeasureMultiSampleEndpointAsync(
            targetLabel, hostOrIp, port, sampleCount, preferIcmp, cancellationToken);

    public Task<IReadOnlyList<TracerouteHopResult>> TraceRouteAsync(
        string hostOrIp,
        int maxHops = 15,
        int timeoutMs = 900,
        CancellationToken cancellationToken = default) =>
        GameNetworkAcceleratorService.RunTracerouteAsync(hostOrIp, maxHops, timeoutMs, cancellationToken);

    public MultiSampleProbeSummary ComputeStatisticalSummary(
        string targetLabel,
        string hostOrIp,
        int port,
        string probeMethod,
        int samplesAttempted,
        IReadOnlyList<double> validRttSamplesMs) =>
        GameNetworkAcceleratorService.ComputeStatisticalProbeSummary(
            targetLabel, hostOrIp, port, probeMethod, samplesAttempted, validRttSamplesMs);

    public RouteComparisonEvaluation CompareDirectAndRelayRoutes(
        string gameIdentifier,
        string targetServerHost,
        int targetServerPort,
        MultiSampleProbeSummary directRoute,
        RelayNodeCandidate? candidateNode,
        MultiSampleProbeSummary? relayIngressProbe,
        double? estimatedRelayToGameLegMs = null) =>
        GameNetworkAcceleratorService.EvaluateHonestRouteComparison(
            gameIdentifier, targetServerHost, targetServerPort, directRoute, candidateNode, relayIngressProbe, estimatedRelayToGameLegMs);

    public IReadOnlyList<RouteComparisonEvaluation> GetMeasurementHistory() =>
        GameNetworkAcceleratorService.GetEvaluationHistory();

    public IReadOnlyList<RelayNodeCandidate> GetRegisteredNodes() =>
        GameNetworkAcceleratorService.GetRegisteredRelayNodes();

    public Task<MultiSampleProbeSummary> CheckNodeHealthAsync(
        RelayNodeCandidate node,
        int sampleCount = 5,
        CancellationToken cancellationToken = default) =>
        GameNetworkAcceleratorService.MeasureMultiSampleEndpointAsync(
            $"HealthCheck:{node.NodeId}", node.EndpointHost, node.EndpointPort, sampleCount, preferIcmp: true, cancellationToken);

    public (bool installed, string? wireGuardExePath, string? wgCliPath) CheckInstallation() =>
        WireGuardTunnelManager.CheckWireGuardInstallation();

    public WireGuardConfigAuditResult ValidateConfiguration(
        string? configContent,
        bool allowDefaultRouteOverride = false) =>
        WireGuardTunnelManager.ValidateAndAuditConfig(configContent, allowDefaultRouteOverride);

    public Task<WireGuardTunnelStatusReport> GetTunnelStatusAsync(
        string tunnelName = "PCControlGameTunnel",
        CancellationToken cancellationToken = default) =>
        WireGuardTunnelManager.QueryTunnelStatusAsync(tunnelName, cancellationToken);

    public Task<WireGuardServiceOperationResult> InstallTunnelWithConsentAsync(
        string tunnelName,
        string configContent,
        bool userConfirmedExecution,
        bool allowDefaultRouteOverride = false,
        CancellationToken cancellationToken = default) =>
        WireGuardTunnelManager.InstallTunnelServiceWithConsentAsync(
            tunnelName, configContent, userConfirmedExecution, allowDefaultRouteOverride, cancellationToken);

    public Task<WireGuardServiceOperationResult> RemoveTunnelAndRestoreRoutesAsync(
        string tunnelName,
        bool userConfirmedExecution,
        CancellationToken cancellationToken = default) =>
        WireGuardTunnelManager.UninstallTunnelServiceWithConsentAsync(
            tunnelName, userConfirmedExecution, cancellationToken);

    public SelectiveRoutingCapabilityReport GetRoutingCapabilities() =>
        GameNetworkAcceleratorService.GetSelectiveRoutingCapabilityReport();

    public Task<string> CaptureRouteTableSnapshotAsync(CancellationToken cancellationToken = default) =>
        WireGuardTunnelManager.CaptureCurrentIPv4RouteTableSnapshotAsync(cancellationToken);

    public string? GetLastRouteTableSnapshot() =>
        WireGuardTunnelManager.GetLastCapturedRouteTableSnapshot();

    public Task<RouteComparisonEvaluation> AnalyzeGameRouteAsync(
        string gameIdentifier,
        string targetServerHost,
        int targetServerPort = 443,
        int sampleCount = 5,
        CancellationToken cancellationToken = default) =>
        GameNetworkAcceleratorService.AnalyzeGameRouteAsync(
            gameIdentifier, targetServerHost, targetServerPort, sampleCount, cancellationToken);
}
