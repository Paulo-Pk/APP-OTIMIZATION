using System;

namespace PCControlUltimate.Core.Entities;

public enum OptimizationRiskLevel { Low, Medium, High }
public enum OptimizationProfileLevel { Conservative, Balanced, Advanced }

public class OptimizationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedBenefit { get; set; } = string.Empty;
    public OptimizationRiskLevel Risk { get; set; }
    public OptimizationProfileLevel ProfileLevel { get; set; }
    public string RegistryKey { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string PreviousValue { get; set; } = string.Empty;
    public string TargetValue { get; set; } = string.Empty;
    public bool IsApplied { get; set; }
    public bool IsReversible { get; set; } = true;
    public DateTime? AppliedAtUtc { get; set; }
    public string? BackupIdentifier { get; set; }
}

public class NetworkRouteDiagnosticRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GameProcessName { get; set; } = string.Empty;
    public string TargetEndpoint { get; set; } = string.Empty;
    public double? DirectRttMs { get; set; }
    public double? DirectJitterMs { get; set; }
    public double DirectLossPercent { get; set; }
    public string? CandidateNodeId { get; set; }
    public double? CandidateRttMs { get; set; }
    public double? CandidateJitterMs { get; set; }
    public double CandidateLossPercent { get; set; }
    public string RecommendedRoute { get; set; } = "Direct";
    public string RecommendationReason { get; set; } = string.Empty;
    public DateTime MeasuredAtUtc { get; set; } = DateTime.UtcNow;
}

public class RelayNodeEntity
{
    public string NodeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RegionCode { get; set; } = string.Empty;
    public string EndpointHost { get; set; } = string.Empty;
    public int EndpointPort { get; set; } = 51820;
    public string PublicKey { get; set; } = string.Empty;
    public string AllowedTargetCidrs { get; set; } = string.Empty;
    public bool IsCustomVps { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
