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
                                Title: $"Pouco espaço livre em {drive.Model} ({freePct:F1}%)",
                                Severity: DiagnosticSeverityLevel.Alto,
                                Evidence: $"Apenas {drive.FreeSpaceGb.Value:F1} GB livres de {drive.CapacityGb.Value:F1} GB ({freePct:F1}%).",
                                ProbableCause: "Arquivos temporários, atualizações antigas ou jogos ocupando muito espaço.",
                                RecommendedAction: "Abra Limpeza do PC, confira os arquivos que podem ser removidos e escolha o que deseja limpar.",
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
                                ProbableCause: "A unidade tem espaço livre suficiente neste momento.",
                                RecommendedAction: "Não é necessário fazer nada agora.",
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
                    Title: $"A memória do computador está quase cheia ({ramPct:F1}%)",
                    Severity: DiagnosticSeverityLevel.Moderado,
                    Evidence: $"{snapshot.RamUsedGb.Value:F1} GB em uso de {snapshot.RamTotalGb.Value:F1} GB totais.",
                    ProbableCause: "Muitos programas estão abertos ou algum aplicativo está usando muita memória.",
                    RecommendedAction: "Feche programas que não está usando e confira quais abrem junto com o Windows.",
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
                Evidence: $"O processador {snapshot.CpuModelName ?? "detectado"} está marcando {snapshot.CpuPackageTemp.Value:F1}°C.",
                ProbableCause: "Pode haver poeira, pouca ventilação ou ventoinha trabalhando abaixo do necessário.",
                RecommendedAction: "Confira se o cooler está girando e se as entradas de ar do gabinete estão livres.",
                RequiresAdminForRepair: false
            ));
        }

        var gpuAudit = _gpuThermalAuditor.EvaluateGpuThermals(snapshot);
        if (!gpuAudit.IsHealthy)
        {
            findings.Add(new SystemDoctorFinding(
                Category: "Temperaturas GPU",
                Title: "A placa de vídeo está aquecendo mais do que o esperado",
                Severity: gpuAudit.Severity == "Critical" ? DiagnosticSeverityLevel.Critico : DiagnosticSeverityLevel.Moderado,
                Evidence: snapshot.GpuCoreTemp.HasValue && snapshot.GpuHotspotTemp.HasValue
                    ? $"Temperatura da GPU: {snapshot.GpuCoreTemp.Value:F1}°C; Hotspot: {snapshot.GpuHotspotTemp.Value:F1}°C."
                    : "Não foi possível obter todas as temperaturas da placa de vídeo.",
                ProbableCause: "A diferença entre as temperaturas da placa ou a ventilação pode estar fora do esperado.",
                RecommendedAction: "Confira a temperatura, as ventoinhas e a ventilação da placa de vídeo.",
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
                    Title: "O Windows registrou uma falha importante",
                    Severity: sev,
                    Evidence: "Foi registrada uma falha no histórico do Windows. Consulte os detalhes técnicos somente se precisar investigar mais a fundo.",
                    ProbableCause: ev.EventId == 4101
                        ? "O driver de vídeo parou de responder e o Windows tentou recuperá-lo."
                        : "O computador desligou inesperadamente ou algum componente informou um erro.",
                    RecommendedAction: "Faça uma verificação dos arquivos do Windows. Se o problema voltar, procure suporte técnico.",
                    RequiresAdminForRepair: true,
                    RepairCommandId: "dism-checkhealth"
                ));
            }
        }
        else
        {
            findings.Add(new SystemDoctorFinding(
                Category: "Integridade do Windows",
                Title: "Nenhuma falha grave foi encontrada nos últimos 7 dias",
                Severity: DiagnosticSeverityLevel.Informativo,
                Evidence: "O histórico do Windows foi conferido e não mostrou falhas graves recentes.",
                ProbableCause: "O Windows não apresentou sinais de instabilidade nesse período.",
                RecommendedAction: "Você pode fazer uma verificação preventiva dos arquivos do Windows quando quiser.",
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
            ? $"Verificação completa concluída. Resultado geral: {score}/100, com {findings.Count} itens conferidos."
            : $"Verificação concluída sem alterar o computador. Resultado geral: {score}/100. Algumas correções precisam de autorização do Windows.";

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

        // Target the actual Windows system volume instead of assuming Windows is installed on C:.
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var (fileName, args, description) = repairCommandId.ToLowerInvariant() switch
        {
            "dism-checkhealth" => ("dism.exe", "/Online /Cleanup-Image /CheckHealth", "Verificação rápida de integridade do repositório de componentes (DISM CheckHealth)"),
            "dism-scanhealth" => ("dism.exe", "/Online /Cleanup-Image /ScanHealth", "Varredura profunda de corrupção na imagem do Windows (DISM ScanHealth)"),
            "sfc-verifyonly" => ("sfc.exe", "/verifyonly", "Verificação de integridade de arquivos protegidos do sistema sem alteração (SFC VerifyOnly)"),
            "sfc-scannow" => ("sfc.exe", "/scannow", "Verificação e reparo de arquivos protegidos do Windows (SFC ScanNow)"),
            "chkdsk-readonly" => ("chkdsk.exe", systemDrive, "Inspeção somente-leitura do volume do Windows (CHKDSK)"),
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

        // Drain both redirected streams concurrently to prevent a child process from
        // blocking when either OS pipe buffer fills (DISM/SFC can be very verbose).
        // Keep draining both pipes independently of the caller token. If the caller
        // cancels, the process is terminated first and the readers are observed with
        // bounded waits so a broken child pipe cannot hang shutdown indefinitely.
        Task<string> stdOutTask = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stdErrTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await proc.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try
            {
                await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch { }
            try { await stdOutTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
            try { await stdErrTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
            throw;
        }

        await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
        string stdOut = await stdOutTask.ConfigureAwait(false);
        string stdErr = await stdErrTask.ConfigureAwait(false);

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
