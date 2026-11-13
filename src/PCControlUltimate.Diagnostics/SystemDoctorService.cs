using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Models;

namespace PCControlUltimate.Diagnostics;

public enum DiagnosticSeverityLevel
{
    Informativo,
    Baixo,
    Moderado,
    Alto,
    Critico
}

public record SystemDoctorFinding(
    string Category,
    string Title,
    DiagnosticSeverityLevel Severity,
    string Evidence,
    string ProbableCause,
    string RecommendedAction,
    bool RequiresAdminForRepair,
    string? RepairCommandId = null
);

public record SystemDoctorReport(
    DateTime AuditedAtUtc,
    bool IsRunningAsAdministrator,
    int HealthScore,
    IReadOnlyList<SystemDoctorFinding> Findings,
    string ExecutiveSummary
);

public record SystemRepairExecutionResult(
    string RepairCommandId,
    string CommandExecuted,
    bool Success,
    bool RequiresAdminElevation,
    int ExitCode,
    string OutputLog,
    DateTime ExecutedAtUtc
);

/// <summary>
/// System Doctor — Diagnóstico e Reparo do Sistema (§10):
/// Performs immediate non-destructive read-only diagnostics across storage, memory, temperatures,
/// system integrity, startup, and Event Viewer logs even without UAC elevation, and executes
/// user-confirmed repair commands (DISM, SFC, CHKDSK) with UAC verification.
/// </summary>
public class SystemDoctorService
{
    private readonly SystemStabilityAuditor _stabilityAuditor;
    private readonly GpuThermalAuditor _gpuThermalAuditor;

    public SystemDoctorService(
        SystemStabilityAuditor? stabilityAuditor = null,
        GpuThermalAuditor? gpuThermalAuditor = null)
    {
        _stabilityAuditor = stabilityAuditor ?? new SystemStabilityAuditor();
        _gpuThermalAuditor = gpuThermalAuditor ?? new GpuThermalAuditor();
    }

    public async Task<SystemDoctorReport> RunComprehensiveAuditAsync(
        HardwareSnapshot snapshot,
        bool? isRunningAsAdminOverride = null,
        CancellationToken cancellationToken = default)
    {
        bool isAdmin = isRunningAsAdminOverride ?? CheckIsAdministrator();
        var findings = new List<SystemDoctorFinding>();

        // 1. Storage Free Space & Integrity Audit (Read-only, works without Admin)
        try
        {
            if (snapshot.StorageDrives != null && snapshot.StorageDrives.Count > 0)
            {
                foreach (var drive in snapshot.StorageDrives)
                {
                    if (drive.CapacityGb.HasValue && drive.CapacityGb.Value > 0 && drive.FreeSpaceGb.HasValue)
                    {
                        double freePct = drive.FreeSpaceGb.Value / drive.CapacityGb.Value * 100.0;
                        if (freePct < 10.0)
                        {
                            findings.Add(new SystemDoctorFinding(
                                Category: "Armazenamento",
                                Title: $"Pouco espaço livre no disco {drive.Model} ({freePct:F1}%)",
                                Severity: DiagnosticSeverityLevel.Alto,
                                Evidence: $"Apenas {drive.FreeSpaceGb.Value:F1} GB livres de {drive.CapacityGb.Value:F1} GB ({freePct:F1}%).",
                                ProbableCause: "O disco está quase cheio. Arquivos temporários, atualizações e jogos podem ocupar boa parte do espaço.",
                                RecommendedAction: "Execute uma simulação Dry-Run no módulo Debloat & Cleaner para liberar espaço com segurança.",
                                RequiresAdminForRepair: false
                            ));
                        }
                        else
                        {
                            findings.Add(new SystemDoctorFinding(
                                Category: "Armazenamento",
                                Title: $"Espaço livre adequado em {drive.Model}",
                                Severity: DiagnosticSeverityLevel.Informativo,
                                Evidence: $"{drive.FreeSpaceGb.Value:F1} GB livres de {drive.CapacityGb.Value:F1} GB ({freePct:F1}%).",
                                ProbableCause: "Há espaço livre suficiente para o Windows e os programas funcionarem com mais tranquilidade.",
                                RecommendedAction: "Nenhuma ação necessária.",
                                RequiresAdminForRepair: false
                            ));
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Memory Pressure Analysis
        if (snapshot.RamTotalGb.HasValue && snapshot.RamTotalGb.Value > 0 && snapshot.RamUsedGb.HasValue)
        {
            double ramPct = snapshot.RamUsedGb.Value / snapshot.RamTotalGb.Value * 100.0;
            if (ramPct >= 88.0)
            {
                findings.Add(new SystemDoctorFinding(
                    Category: "Memória RAM",
                    Title: $"Memória RAM quase cheia ({ramPct:F1}%)",
                    Severity: DiagnosticSeverityLevel.Moderado,
                    Evidence: $"{snapshot.RamUsedGb.Value:F1} GB em uso de {snapshot.RamTotalGb.Value:F1} GB totais.",
                    ProbableCause: "Muitos programas estão abertos ao mesmo tempo ou algum deles está usando memória demais.",
                    RecommendedAction: "Feche aplicativos ociosos em segundo plano ou revise itens de inicialização.",
                    RequiresAdminForRepair: false
                ));
            }
        }

        // 3. Universal Thermal Analysis (CPU & GPU)
        if (snapshot.CpuPackageTemp.HasValue && snapshot.CpuPackageTemp.Value >= 88f)
        {
            findings.Add(new SystemDoctorFinding(
                Category: "Temperaturas",
                Title: $"O processador está muito quente ({snapshot.CpuPackageTemp.Value:F1}°C)",
                Severity: snapshot.CpuPackageTemp.Value >= 95f ? DiagnosticSeverityLevel.Critico : DiagnosticSeverityLevel.Alto,
                Evidence: $"CPU {snapshot.CpuModelName ?? "Detectada"} operando a {snapshot.CpuPackageTemp.Value:F1}°C.",
                ProbableCause: "A refrigeração pode estar insuficiente, com poeira, ventoinha lenta ou configuração de energia muito agressiva.",
                RecommendedAction: "Verifique a rotação do cooler da CPU e o perfil térmico no Hardware Monitor.",
                RequiresAdminForRepair: false
            ));
        }

        var gpuAudit = _gpuThermalAuditor.EvaluateGpuThermals(snapshot);
        if (!gpuAudit.IsHealthy)
        {
            findings.Add(new SystemDoctorFinding(
                Category: "Temperaturas GPU",
                Title: "A placa de vídeo está aquecendo acima do recomendado",
                Severity: gpuAudit.Severity == "Critical" ? DiagnosticSeverityLevel.Critico : DiagnosticSeverityLevel.Moderado,
                Evidence: gpuAudit.Recommendation,
                ProbableCause: "A diferença entre a temperatura principal e o ponto mais quente pode estar alta, ou a refrigeração pode não estar dando conta.",
                RecommendedAction: "Inspecione a curva de ventoinhas no GPU Health Lab.",
                RequiresAdminForRepair: false
            ));
        }

        // 4. Windows Event Viewer Stability Audit (WHEA / Kernel-Power 41 / Display 4101)
        var stabilityReport = await _stabilityAuditor.AuditRecentEventsAsync(7, cancellationToken);
        if (!stabilityReport.IsSystemStable)
        {
            foreach (var ev in stabilityReport.CriticalEvents.Take(5))
            {
                var sev = ev.EventId == 41 || ev.Source.Contains("WHEA", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticSeverityLevel.Alto
                    : DiagnosticSeverityLevel.Moderado;

                findings.Add(new SystemDoctorFinding(
                    Category: "Eventos Críticos do Windows",
                    Title: ev.EventId == 4101
                        ? "O driver de vídeo parou de responder e foi reiniciado"
                        : ev.EventId == 41
                            ? "O Windows registrou um desligamento inesperado"
                            : ev.Source.Contains("WHEA", StringComparison.OrdinalIgnoreCase)
                                ? "O Windows registrou um possível erro de hardware"
                                : "O Windows registrou uma falha importante",
                    Severity: sev,
                    Evidence: $"{ev.TimestampUtc:yyyy-MM-dd HH:mm} UTC — {ev.Message}",
                    ProbableCause: ev.EventId == 4101
                        ? "Timeout e recuperação do driver de vídeo (TDR)."
                        : "Desligamento inesperado ou erro corrigido/não-corrigido de hardware.",
                    RecommendedAction: "Execute a verificação de integridade DISM CheckHealth e SFC /scannow no System Doctor.",
                    RequiresAdminForRepair: true,
                    RepairCommandId: "dism-checkhealth"
                ));
            }
        }
        else
        {
            findings.Add(new SystemDoctorFinding(
                Category: "Integridade do Windows",
                Title: "Nenhuma falha crítica recente encontrada",
                Severity: DiagnosticSeverityLevel.Informativo,
                Evidence: "O Windows foi consultado e não apresentou registros críticos nos últimos sete dias.",
                ProbableCause: "Subsistema de kernel e energia operando com estabilidade.",
                RecommendedAction: "Verificação preventiva DISM CheckHealth disponível a qualquer momento.",
                RequiresAdminForRepair: true,
                RepairCommandId: "dism-checkhealth"
            ));
        }

        int penalty = findings.Sum(f => f.Severity switch
        {
            DiagnosticSeverityLevel.Critico => 25,
            DiagnosticSeverityLevel.Alto => 15,
            DiagnosticSeverityLevel.Moderado => 8,
            DiagnosticSeverityLevel.Baixo => 3,
            _ => 0
        });

        int score = Math.Clamp(100 - penalty, 10, 100);
        string summary = isAdmin
            ? $"Diagnóstico concluído. Saúde do PC: {score}/100. Foram analisados {findings.Count} pontos. Nenhuma configuração foi alterada; reparos só serão executados mediante sua confirmação."
            : $"Diagnóstico concluído em modo de leitura. Saúde do PC: {score}/100. Foram analisados {findings.Count} pontos e nenhuma configuração foi alterada. Para reparos do Windows, será solicitada autorização de administrador.";

        return new SystemDoctorReport(
            AuditedAtUtc: DateTime.UtcNow,
            IsRunningAsAdministrator: isAdmin,
            HealthScore: score,
            Findings: findings,
            ExecutiveSummary: summary
        );
    }

    public async Task<SystemRepairExecutionResult> ExecuteVerifiedRepairToolAsync(
        string repairCommandId,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            return new SystemRepairExecutionResult(
                repairCommandId,
                "N/A",
                false,
                false,
                -1,
                "Confirmação explícita do usuário é obrigatória antes de executar ferramentas de reparo do Windows.",
                DateTime.UtcNow
            );
        }

        var (fileName, args, description) = repairCommandId.ToLowerInvariant() switch
        {
            "dism-checkhealth" => ("dism.exe", "/Online /Cleanup-Image /CheckHealth", "Verificação rápida de integridade do repositório de componentes (DISM CheckHealth)"),
            "dism-scanhealth" => ("dism.exe", "/Online /Cleanup-Image /ScanHealth", "Varredura profunda de corrupção na imagem do Windows (DISM ScanHealth)"),
            "sfc-verifyonly" => ("sfc.exe", "/verifyonly", "Verificação de integridade de arquivos protegidos do sistema sem alteração (SFC VerifyOnly)"),
            "sfc-scannow" => ("sfc.exe", "/scannow", "Verificação e reparo de arquivos protegidos do Windows (SFC ScanNow)"),
            "chkdsk-readonly" => ("chkdsk.exe", "C:", "Inspeção somente-leitura do sistema de arquivos NTFS (CHKDSK)"),
            _ => (string.Empty, string.Empty, string.Empty)
        };

        if (string.IsNullOrEmpty(fileName))
        {
            return new SystemRepairExecutionResult(
                repairCommandId,
                "UNKNOWN",
                false,
                false,
                -1,
                $"Comando de reparo '{repairCommandId}' não pertence ao catálogo seguro do System Doctor.",
                DateTime.UtcNow
            );
        }

        if (!CheckIsAdministrator())
        {
            return new SystemRepairExecutionResult(
                repairCommandId,
                $"{fileName} {args}",
                false,
                true,
                -1,
                $"'{description}' requer privilégios de Administrador (UAC). Clique em 'Elevar UAC' para autorizar a execução.",
                DateTime.UtcNow
            );
        }

        if (!OperatingSystem.IsWindows())
        {
            return new SystemRepairExecutionResult(
                repairCommandId,
                $"{fileName} {args}",
                false,
                false,
                -1,
                "Ferramentas nativas DISM/SFC/CHKDSK requerem sistema operacional Windows 10/11.",
                DateTime.UtcNow
            );
        }

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, fileName),
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return new SystemRepairExecutionResult(
                repairCommandId,
                $"{fileName} {args}",
                false,
                false,
                -1,
                $"Falha ao iniciar {fileName}.",
                DateTime.UtcNow
            );
        }

        string stdOut = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
        string stdErr = await proc.StandardError.ReadToEndAsync(cancellationToken);
        await proc.WaitForExitAsync(cancellationToken);

        string output = string.IsNullOrWhiteSpace(stdErr) ? stdOut.Trim() : $"{stdOut.Trim()}\n{stdErr.Trim()}";
        return new SystemRepairExecutionResult(
            repairCommandId,
            $"{fileName} {args}",
            proc.ExitCode == 0,
            false,
            proc.ExitCode,
            output,
            DateTime.UtcNow
        );
    }

    private static bool CheckIsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
