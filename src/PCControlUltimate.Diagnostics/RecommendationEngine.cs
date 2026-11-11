using System;
using System.Collections.Generic;
using System.Linq;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Diagnostics;

public record EvidenceBasedRecommendation(
    string RecommendationId,
    string Title,
    string ProblemIdentified,
    string EvidenceCollected,
    string ExpectedImpact,
    string RiskLevel,
    string RecommendedAction,
    string TargetModule
);

/// <summary>
/// Intelligent Evidence-Based Recommendation Engine (§14):
/// Generates hardware-agnostic recommendations strictly from real telemetry, storage, network, and compatibility findings.
/// Uses shared contracts in PCControlUltimate.Core.Models without referencing PCControlUltimate.Windows.
/// </summary>
public class RecommendationEngine
{
    public IReadOnlyList<EvidenceBasedRecommendation> GenerateRecommendations(
        UniversalHardwareReport hardwareReport,
        NetworkDiagnosticsReport? networkReport = null,
        SystemDoctorReport? doctorReport = null)
    {
        var list = new List<EvidenceBasedRecommendation>();

        // 1. Sensor elevation / driver recommendation if physical sensors require UAC
        if (!hardwareReport.IsRunningAsAdmin &&
            (!hardwareReport.Cpu.PackageTemperatureC.IsAvailable ||
             (hardwareReport.PrimaryGpu != null && !hardwareReport.PrimaryGpu.CoreTemperatureC.IsAvailable)))
        {
            list.Add(new EvidenceBasedRecommendation(
                RecommendationId: "rec-uac-sensors",
                Title: "Habilitar leitura física completa de sensores via UAC",
                ProblemIdentified: "Sensores térmicos físicos (CPU Package / GPU Core) estão em modo restrito de usuário padrão.",
                EvidenceCollected: $"Sessão atual sem elevação UAC; status do sensor CPU: {hardwareReport.Cpu.PackageTemperatureC.SensorStatus}.",
                ExpectedImpact: "Desbloqueia telemetria térmica precisa, consumo em Watts e rotação de ventoinhas.",
                RiskLevel: "Seguro",
                RecommendedAction: "Utilize o botão 'Elevar UAC' no topo da interface quando desejar leitura física completa.",
                TargetModule: "Hardware Monitor"
            ));
        }

        // 2. Hybrid Notebook / Multi-GPU Battery recommendation
        if (hardwareReport.DetectedProfile == SystemHardwareProfile.Notebook || hardwareReport.BatteryAndPower.HasBattery)
        {
            list.Add(new EvidenceBasedRecommendation(
                RecommendationId: "rec-notebook-power",
                Title: "Perfil Inteligente para Notebook / Gráficos Híbridos",
                ProblemIdentified: "Dispositivo portátil com bateria detectado.",
                EvidenceCollected: $"{hardwareReport.ProfileJustification} (GPUs detectadas: {hardwareReport.Gpus.Count}).",
                ExpectedImpact: "Evita consumo excessivo da GPU dedicada em tarefas de área de trabalho e preserva temperaturas em jogos.",
                RiskLevel: "Seguro",
                RecommendedAction: "Use o modo Equilibrado quando estiver na bateria e ative o Boost Center apenas conectado à fonte AC.",
                TargetModule: "Boost Center"
            ));
        }

        // 3. Memory pressure recommendation
        if (hardwareReport.Memory.UsagePercent.IsAvailable && (hardwareReport.Memory.UsagePercent.Value ?? 0f) >= 80f)
        {
            list.Add(new EvidenceBasedRecommendation(
                RecommendationId: "rec-memory-pressure",
                Title: "Otimizar Alocação de Memória Física",
                ProblemIdentified: "Ocupação de memória RAM acima de 80%.",
                EvidenceCollected: $"Uso atual: {hardwareReport.Memory.UsedGb.Value:F1} GB / {hardwareReport.Memory.TotalGb.Value:F1} GB ({hardwareReport.Memory.UsagePercent.Value:F1}%).",
                ExpectedImpact: "Reduz paginação em disco (stuttering) durante alternância de janelas e jogos.",
                RiskLevel: "Seguro",
                RecommendedAction: "Aplique o ajuste 'Priorizar RAM para Aplicativos (LargeSystemCache=0)' em Windows Tweaks.",
                TargetModule: "Windows Tweaks"
            ));
        }

        // 4. Storage capacity recommendation
        var lowDisk = hardwareReport.StorageDevices.FirstOrDefault(d =>
            d.CapacityGb.Value.HasValue && d.CapacityGb.Value.Value > 0 &&
            d.FreeSpaceGb.Value.HasValue && (d.FreeSpaceGb.Value.Value / d.CapacityGb.Value.Value) < 0.15f);

        if (lowDisk != null)
        {
            list.Add(new EvidenceBasedRecommendation(
                RecommendationId: "rec-storage-space",
                Title: $"Liberar espaço no volume {lowDisk.Model}",
                ProblemIdentified: "Menos de 15% de espaço livre na unidade de armazenamento.",
                EvidenceCollected: $"Livre: {lowDisk.FreeSpaceGb.Value:F1} GB de {lowDisk.CapacityGb.Value:F1} GB.",
                ExpectedImpact: "Preserva a velocidade de escrita sustentada do SSD e evita falhas de atualização.",
                RiskLevel: "Seguro",
                RecommendedAction: "Execute a análise Dry-Run no Debloat & Cleaner para remover caches temporários verificados.",
                TargetModule: "Debloat & Cleaner"
            ));
        }

        // 5. Network Wi-Fi / Jitter / Loss recommendation
        var activeWifi = hardwareReport.NetworkAdapters.FirstOrDefault(n => n.IsConnected && n.IsWiFi);
        if (activeWifi != null && networkReport != null && networkReport.JitterMs >= 10.0)
        {
            list.Add(new EvidenceBasedRecommendation(
                RecommendationId: "rec-network-wifi-jitter",
                Title: "Reduzir oscilação (Jitter) em conexão Wi-Fi",
                ProblemIdentified: "Conexão sem fio ativa apresentando oscilação de latência.",
                EvidenceCollected: $"Adaptador Wi-Fi '{activeWifi.Name}' ativo com Jitter de {networkReport.JitterMs:F1} ms.",
                ExpectedImpact: "Melhora a consistência de registro de pacotes em jogos competitivos.",
                RiskLevel: "Seguro",
                RecommendedAction: "Prefira cabo Ethernet ou utilize o modo 'Estabilidade' no Game Network Accelerator.",
                TargetModule: "Game Network Accelerator"
            ));
        }

        // 6. Default baseline recommendation when system is healthy
        if (list.Count == 0)
        {
            list.Add(new EvidenceBasedRecommendation(
                RecommendationId: "rec-baseline-snapshot",
                Title: "Sistema Saudável — Manter Snapshot de Segurança Atualizado",
                ProblemIdentified: "Nenhum gargalo crítico térmico, de armazenamento ou de rede detectado no momento.",
                EvidenceCollected: $"Perfil '{hardwareReport.DetectedProfile}' operando dentro dos parâmetros normais.",
                ExpectedImpact: "Garante ponto de reversão imediato com integridade SHA-256 antes de qualquer manutenção futura.",
                RiskLevel: "Seguro",
                RecommendedAction: "Verifique seus snapshots no Restore Center.",
                TargetModule: "Restore Center"
            ));
        }

        return list;
    }
}
