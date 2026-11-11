using System;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Diagnostics;

public enum GpuThermalHealth
{
    SensorUnavailable,
    Excellent,
    Normal,
    Warning,
    Critical
}

public record GpuThermalEvaluationResult(
    GpuThermalHealth Status,
    float CoreTemp,
    float HotspotTemp,
    float Delta,
    string Message
);

public record ThermalAuditResult(
    bool IsHealthy,
    float? CoreTemp,
    float? HotspotTemp,
    float? DeltaTemp,
    string Severity,
    string Recommendation
);

/// <summary>
/// Universal GPU Thermal Auditor supporting AMD Radeon (RDNA 2/3/4, GCN), NVIDIA GeForce/RTX,
/// Intel Arc/Integrated, and Qualcomm Adreno GPUs without global hardcoding to any single GPU model.
/// Optional vendor profiles provide architecture-specific context when a vendor/series is detected.
/// </summary>
public class GpuThermalAuditor
{
    public static GpuThermalEvaluationResult EvaluateThermalState(
        float coreTemp,
        float hotspotTemp,
        string? gpuName = null)
    {
        if (float.IsNaN(coreTemp) || float.IsInfinity(coreTemp) || coreTemp <= 0.0f)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.SensorUnavailable,
                0.0f,
                0.0f,
                0.0f,
                "Sensor de temperatura da GPU indisponível (0°C ou não suportado pelo hardware/driver atual)."
            );
        }

        var profile = ResolveArchitectureThermalProfile(gpuName);
        string archContext = !string.IsNullOrWhiteSpace(gpuName)
            ? $"{gpuName} ({profile.ArchitectureLabel})"
            : profile.ArchitectureLabel;

        if (float.IsNaN(hotspotTemp) || float.IsInfinity(hotspotTemp) || hotspotTemp <= 0.0f)
        {
            bool coreHigh = coreTemp >= profile.MaxSafeCoreTempC;
            return new GpuThermalEvaluationResult(
                coreHigh ? GpuThermalHealth.Warning : GpuThermalHealth.Normal,
                coreTemp,
                0.0f,
                0.0f,
                coreHigh
                    ? $"Temperatura Core elevada ({coreTemp:F1}°C) em {archContext}. Sensor Hotspot não exposto pelo driver."
                    : $"Temperatura Core saudável ({coreTemp:F1}°C) em {archContext}. Sensor Hotspot não exposto por este modelo."
            );
        }

        float delta = Math.Max(0.0f, hotspotTemp - coreTemp);

        if (hotspotTemp >= profile.CriticalHotspotTempC || delta >= 32.0f)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.Critical,
                coreTemp,
                hotspotTemp,
                delta,
                $"Alerta Crítico ({archContext}): Hotspot em {hotspotTemp:F1}°C (Delta ΔT = {delta:F1}°C). " +
                "Importante contextualizar com potência (W), rotação de ventoinhas e possível degradação de pasta térmica."
            );
        }

        if (hotspotTemp >= profile.WarningHotspotTempC || delta >= profile.WarningDeltaC)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.Warning,
                coreTemp,
                hotspotTemp,
                delta,
                $"Atenção Térmica ({archContext}): Delta ΔT de {delta:F1}°C (Core {coreTemp:F1}°C / Hotspot {hotspotTemp:F1}°C). " +
                "Recomendado contextualizar com carga da GPU, consumo em Watts e curva de ventilação."
            );
        }

        if (delta <= 18.0f && hotspotTemp < 85.0f)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.Excellent,
                coreTemp,
                hotspotTemp,
                delta,
                $"Excelente eficiência térmica ({archContext}): Core {coreTemp:F1}°C | Hotspot {hotspotTemp:F1}°C | Delta ΔT {delta:F1}°C."
            );
        }

        return new GpuThermalEvaluationResult(
            GpuThermalHealth.Normal,
            coreTemp,
            hotspotTemp,
            delta,
            $"Operação térmica normal ({archContext}): Core {coreTemp:F1}°C | Hotspot {hotspotTemp:F1}°C | Delta ΔT {delta:F1}°C."
        );
    }

    public ThermalAuditResult EvaluateGpuThermals(HardwareSnapshot snapshot)
    {
        if (!snapshot.GpuCoreTemp.HasValue)
        {
            return new ThermalAuditResult(
                true,
                null,
                null,
                null,
                "Unavailable",
                "Sensor de temperatura de GPU não detectado ou indisponível no hardware atual."
            );
        }

        var state = EvaluateThermalState(
            snapshot.GpuCoreTemp.Value,
            snapshot.GpuHotspotTemp ?? 0.0f,
            snapshot.GpuModelName
        );

        bool healthy = state.Status is GpuThermalHealth.Excellent or GpuThermalHealth.Normal or GpuThermalHealth.SensorUnavailable;
        return new ThermalAuditResult(
            healthy,
            snapshot.GpuCoreTemp,
            snapshot.GpuHotspotTemp,
            snapshot.GpuHotspotTemp.HasValue ? state.Delta : null,
            state.Status.ToString(),
            state.Message
        );
    }

    public record GpuArchitectureThermalProfile(
        string ArchitectureLabel,
        float MaxSafeCoreTempC,
        float WarningHotspotTempC,
        float CriticalHotspotTempC,
        float WarningDeltaC,
        float CriticalDeltaC
    );

    public static GpuArchitectureThermalProfile ResolveArchitectureThermalProfile(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
        {
            return new GpuArchitectureThermalProfile("GPU Universal", 85f, 95f, 105f, 22f, 30f);
        }

        if (gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RX ", StringComparison.OrdinalIgnoreCase))
        {
            bool isRdna3 = gpuName.Contains("7900", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("7800", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("7700", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("7600", StringComparison.OrdinalIgnoreCase);
            if (isRdna3)
            {
                return new GpuArchitectureThermalProfile("AMD RDNA 3 Discrete Adapter", 88f, 96f, 108f, 22f, 30f);
            }

            bool isDiscreteRadeon = gpuName.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                                    gpuName.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase);
            return isDiscreteRadeon
                ? new GpuArchitectureThermalProfile("AMD Radeon Discrete Adapter", 88f, 95f, 106f, 22f, 29f)
                : new GpuArchitectureThermalProfile("AMD Radeon Integrated Adapter", 85f, 92f, 100f, 18f, 25f);
        }

        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase))
        {
            return new GpuArchitectureThermalProfile("NVIDIA GeForce / RTX Adapter", 83f, 93f, 102f, 18f, 25f);
        }

        if (gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return new GpuArchitectureThermalProfile("Intel Graphics / Arc Adapter", 85f, 94f, 103f, 18f, 25f);
        }

        if (gpuName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase))
        {
            return new GpuArchitectureThermalProfile("Qualcomm Adreno SoC Adapter", 80f, 88f, 95f, 15f, 22f);
        }

        return new GpuArchitectureThermalProfile("GPU Universal", 85f, 95f, 105f, 22f, 30f);
    }
}