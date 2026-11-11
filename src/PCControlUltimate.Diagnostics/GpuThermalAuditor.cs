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
    string Message,
    bool IsEvaluationLimited = false
);

public record ThermalAuditResult(
    bool IsHealthy,
    float? CoreTemp,
    float? HotspotTemp,
    float? DeltaTemp,
    string Severity,
    string Recommendation,
    bool IsEvaluationLimited = false
);

/// <summary>
/// Universal GPU Thermal Auditor (§5):
/// - Never uses RX 7900 XTX as a global default.
/// - Never assumes all GPUs expose a Hotspot/Junction sensor.
/// - Never fabricates manufacturer thermal limits when not provided.
/// - Explicitly reports when evaluation is limited due to missing Hotspot or missing sensors.
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
                "Avaliação térmica limitada: sensor de temperatura da GPU indisponível (0°C ou não exposto pelo driver/hardware atual).",
                IsEvaluationLimited: true
            );
        }

        var profile = ResolveArchitectureThermalProfile(gpuName);
        string archContext = !string.IsNullOrWhiteSpace(gpuName)
            ? $"{gpuName} ({profile.ArchitectureLabel})"
            : profile.ArchitectureLabel;

        // Do not assume all GPUs expose a Hotspot sensor; report limited evaluation when Hotspot is absent
        if (float.IsNaN(hotspotTemp) || float.IsInfinity(hotspotTemp) || hotspotTemp <= 0.0f)
        {
            bool coreHigh = coreTemp >= profile.ConservativeCoreWarningC;
            return new GpuThermalEvaluationResult(
                coreHigh ? GpuThermalHealth.Warning : GpuThermalHealth.Normal,
                coreTemp,
                0.0f,
                0.0f,
                coreHigh
                    ? $"Avaliação térmica limitada ({archContext}): Core elevado ({coreTemp:F1}°C). Sensor de Hotspot/Junction não disponibilizado por esta GPU ou driver."
                    : $"Avaliação térmica limitada ({archContext}): Core em {coreTemp:F1}°C. Sensor de Hotspot/Junction não disponibilizado por esta GPU ou driver.",
                IsEvaluationLimited: true
            );
        }

        float delta = Math.Max(0.0f, hotspotTemp - coreTemp);

        if (hotspotTemp >= profile.ConservativeHotspotCriticalC || delta >= profile.CriticalDeltaC)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.Critical,
                coreTemp,
                hotspotTemp,
                delta,
                $"Alerta Crítico ({archContext}): Hotspot em {hotspotTemp:F1}°C (Delta ΔT = {delta:F1}°C). " +
                "Importante contextualizar com potência (W), rotação de ventoinhas e possível degradação de interface térmica.",
                IsEvaluationLimited: false
            );
        }

        if (hotspotTemp >= profile.ConservativeHotspotWarningC || delta >= profile.WarningDeltaC)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.Warning,
                coreTemp,
                hotspotTemp,
                delta,
                $"Atenção Térmica ({archContext}): Delta ΔT de {delta:F1}°C (Core {coreTemp:F1}°C / Hotspot {hotspotTemp:F1}°C). " +
                "Recomendado contextualizar com carga da GPU, consumo em Watts e curva de ventilação.",
                IsEvaluationLimited: false
            );
        }

        if (delta <= 18.0f && hotspotTemp < 85.0f)
        {
            return new GpuThermalEvaluationResult(
                GpuThermalHealth.Excellent,
                coreTemp,
                hotspotTemp,
                delta,
                $"Excelente eficiência térmica ({archContext}): Core {coreTemp:F1}°C | Hotspot {hotspotTemp:F1}°C | Delta ΔT {delta:F1}°C.",
                IsEvaluationLimited: false
            );
        }

        return new GpuThermalEvaluationResult(
            GpuThermalHealth.Normal,
            coreTemp,
            hotspotTemp,
            delta,
            $"Operação térmica normal ({archContext}): Core {coreTemp:F1}°C | Hotspot {hotspotTemp:F1}°C | Delta ΔT {delta:F1}°C.",
            IsEvaluationLimited: false
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
                "Avaliação térmica limitada: sensor de temperatura de GPU não detectado ou indisponível no hardware atual.",
                IsEvaluationLimited: true
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
            state.Message,
            IsEvaluationLimited: state.IsEvaluationLimited
        );
    }

    public record GpuArchitectureThermalProfile(
        string ArchitectureLabel,
        float ConservativeCoreWarningC,
        float ConservativeHotspotWarningC,
        float ConservativeHotspotCriticalC,
        float WarningDeltaC,
        float CriticalDeltaC
    );

    public static GpuArchitectureThermalProfile ResolveArchitectureThermalProfile(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
        {
            return new GpuArchitectureThermalProfile("Perfil Conservador Universal", 85f, 95f, 105f, 22f, 30f);
        }

        if (gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RX ", StringComparison.OrdinalIgnoreCase))
        {
            bool isRdna3 = gpuName.Contains("Radeon RX 7", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("RX 7900", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("RX 7800", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("RX 7700", StringComparison.OrdinalIgnoreCase) ||
                           gpuName.Contains("RX 7600", StringComparison.OrdinalIgnoreCase);
            if (isRdna3)
            {
                // Technically justified AMD RDNA 3 junction/hotspot architecture (TJMax = 110C)
                return new GpuArchitectureThermalProfile("AMD RDNA 3", 88f, 96f, 108f, 22f, 30f);
            }

            return new GpuArchitectureThermalProfile("AMD Radeon", 86f, 95f, 105f, 22f, 29f);
        }

        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase))
        {
            return new GpuArchitectureThermalProfile("NVIDIA GeForce / RTX", 84f, 93f, 102f, 18f, 25f);
        }

        if (gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return new GpuArchitectureThermalProfile("Intel Graphics / Arc", 85f, 94f, 103f, 18f, 25f);
        }

        if (gpuName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase))
        {
            return new GpuArchitectureThermalProfile("Qualcomm Adreno SoC", 82f, 90f, 98f, 16f, 23f);
        }

        return new GpuArchitectureThermalProfile("Perfil Conservador Universal", 85f, 95f, 105f, 22f, 30f);
    }
}