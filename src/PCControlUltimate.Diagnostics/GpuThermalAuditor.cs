using System;

namespace PCControlUltimate.Diagnostics;

public enum GpuThermalHealth { SensorUnavailable, Excellent, Good, Warning, Critical }

public record GpuThermalEvaluation(float Delta, GpuThermalHealth Status, string Message);

public static class GpuThermalAuditor
{
    public static GpuThermalEvaluation EvaluateThermalState(float coreTemp, float hotspotTemp)
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

        if (delta <= 18.0f)
        {
            return new GpuThermalEvaluation(
                delta,
                GpuThermalHealth.Excellent,
                "Gradiente térmico estreito entre Core e Hotspot (<= 18°C). Excelente acoplamento térmico para a carga atual."
            );
        }

        if (delta <= 24.0f)
        {
            return new GpuThermalEvaluation(
                delta,
                GpuThermalHealth.Good,
                "Gradiente térmico dentro da faixa operacional esperada (18°C–24°C) em GPUs modernas sob carga."
            );
        }

        if (delta <= 30.0f)
        {
            return new GpuThermalEvaluation(
                delta,
                GpuThermalHealth.Warning,
                "Delta térmico elevado (24°C–30°C). Recomenda-se contextualizar com o consumo (W), arquitetura da GPU e curva de ventoinhas."
            );
        }

        return new GpuThermalEvaluation(
            delta,
            GpuThermalHealth.Critical,
            "Delta térmico acima de 30°C sob carga. Pode indicar restrição de fluxo de ar ou fadiga da interface térmica (TIM), dependendo do projeto térmico da placa."
        );
    }
}