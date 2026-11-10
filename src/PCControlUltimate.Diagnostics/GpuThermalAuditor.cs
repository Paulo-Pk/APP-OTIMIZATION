using System;

namespace PCControlUltimate.Diagnostics;

public enum GpuThermalHealth { SensorUnavailable, Excellent, Good, Warning, Critical }

public record GpuThermalEvaluation(float Delta, GpuThermalHealth Status, string Message);

public static class GpuThermalAuditor
{
    public static GpuThermalEvaluation EvaluateThermalState(float coreTemp, float hotspotTemp, string? gpuModelName = null)
    {
        if (float.IsNaN(coreTemp) || float.IsNaN(hotspotTemp) || coreTemp <= 0.0f || hotspotTemp <= 0.0f)
        {
            return new GpuThermalEvaluation(
                0.0f,
                GpuThermalHealth.SensorUnavailable,
                "Sensores de Core ou Hotspot indisponíveis (N/D). Nenhuma inferência térmica realizada."
            );
        }

        float delta = Math.Max(0.0f, hotspotTemp - coreTemp);
        bool isRdna3Flagship = !string.IsNullOrWhiteSpace(gpuModelName) &&
                               (gpuModelName.Contains("7900 XTX", StringComparison.OrdinalIgnoreCase) ||
                                gpuModelName.Contains("7900 XT", StringComparison.OrdinalIgnoreCase) ||
                                gpuModelName.Contains("Radeon RX 7900", StringComparison.OrdinalIgnoreCase));

        string modelSuffix = isRdna3Flagship
            ? " (Arquitetura AMD RDNA 3 Chiplet GCD/MCD detectada — nenhum undervolt/overclock automático aplicado)."
            : string.Empty;

        if (delta <= 18.0f)
        {
            return new GpuThermalEvaluation(
                delta,
                GpuThermalHealth.Excellent,
                $"Gradiente térmico estreito entre Core ({coreTemp:F1}°C) e Hotspot ({hotspotTemp:F1}°C, Δ={delta:F1}°C <= 18°C). Excelente acoplamento térmico para a carga atual.{modelSuffix}"
            );
        }

        if (delta <= 24.0f)
        {
            return new GpuThermalEvaluation(
                delta,
                GpuThermalHealth.Good,
                $"Gradiente térmico dentro da faixa operacional esperada (18°C–24°C) em GPUs modernas sob carga.{modelSuffix}"
            );
        }

        if (delta <= 30.0f)
        {
            return new GpuThermalEvaluation(
                delta,
                GpuThermalHealth.Warning,
                $"Delta térmico elevado ({delta:F1}°C entre Core e Hotspot). Recomenda-se contextualizar com o consumo TBP (W), arquitetura da GPU e curva de ventoinhas.{modelSuffix}"
            );
        }

        return new GpuThermalEvaluation(
            delta,
            GpuThermalHealth.Critical,
            $"Delta térmico acima de 30°C ({delta:F1}°C) sob carga. Pode indicar restrição de fluxo de ar ou fadiga da interface térmica (pump-out), dependendo do projeto térmico da placa.{modelSuffix}"
        );
    }
}