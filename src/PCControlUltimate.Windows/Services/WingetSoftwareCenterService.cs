using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Windows.Services;

public enum SoftwarePackageCategory
{
    Navegadores,
    Comunicacao,
    Produtividade,
    Compactadores,
    Seguranca,
    Drivers,
    Monitoramento,
    Hardware,
    Gaming,
    Benchmark,
    AudioVideo,
    Desenvolvimento,
    FerramentasDeSistema,
    Personalizacao
}

public record WingetSoftwarePackage(
    string PackageId,
    string Name,
    string Publisher,
    SoftwarePackageCategory Category,
    string CategoryDisplayName,
    string Description,
    string OfficialSource,
    bool RequiresAdminUac,
    bool VerifiedDigitalSignature
);

public record WingetOperationProgress(
    string PackageId,
    string OperationType,
    string Status,
    int ExitCode,
    bool Success,
    string OutputLog,
    DateTime CompletedAtUtc
);

/// <summary>
/// Software Center — Central de Aplicativos via WinGet (§9):
/// Supports searching, installing, updating, uninstalling, and verifying official packages across 14 categories
/// with explicit user confirmation, queue management, cancellation, and UAC requirement transparency.
/// </summary>
public class WingetSoftwareCenterService
{
    private readonly ConcurrentQueue<string> _operationQueue = new();

    public static IReadOnlyList<WingetSoftwarePackage> OfficialCatalog { get; } =
    [
        // 1. Navegadores
        new("Mozilla.Firefox", "Mozilla Firefox", "Mozilla", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador web rápido e focado em privacidade.", "winget", false, true),
        new("Brave.Brave", "Brave Browser", "Brave Software", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador Chromium com bloqueio nativo de rastreadores.", "winget", false, true),
        new("Google.Chrome", "Google Chrome", "Google LLC", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador web oficial Google Chrome.", "winget", true, true),

        // 2. Comunicação
        new("Discord.Discord", "Discord", "Discord Inc.", SoftwarePackageCategory.Comunicacao, "Comunicação", "Comunicação por voz, vídeo e texto para comunidades e jogos.", "winget", false, true),
        new("Telegram.TelegramDesktop", "Telegram Desktop", "Telegram FZ-LLC", SoftwarePackageCategory.Comunicacao, "Comunicação", "Cliente desktop rápido e seguro para mensagens.", "winget", false, true),

        // 3. Produtividade
        new("Notepad++.Notepad++", "Notepad++", "Don Ho", SoftwarePackageCategory.Produtividade, "Produtividade", "Editor de código e texto leve para Windows.", "winget", true, true),
        new("Obsidian.Obsidian", "Obsidian", "Obsidian", SoftwarePackageCategory.Produtividade, "Produtividade", "Base de conhecimento local em Markdown.", "winget", false, true),

        // 4. Compactadores
        new("7zip.7zip", "7-Zip", "Igor Pavlov", SoftwarePackageCategory.Compactadores, "Compactadores", "Compactador e descompactador de arquivos de código aberto com alta taxa de compressão.", "winget", true, true),
        new("Giorgiotani.PeaZip", "PeaZip", "Giorgio Tani", SoftwarePackageCategory.Compactadores, "Compactadores", "Gerenciador de arquivos compactados multi-formato seguro.", "winget", true, true),

        // 5. Segurança
        new("Bitwarden.Bitwarden", "Bitwarden", "Bitwarden Inc.", SoftwarePackageCategory.Seguranca, "Segurança", "Gerenciador de senhas de código aberto com criptografia ponta a ponta.", "winget", false, true),
        new("KeePassXCTeam.KeePassXC", "KeePassXC", "KeePassXC Team", SoftwarePackageCategory.Seguranca, "Segurança", "Cofre de senhas local criptografado offline.", "winget", true, true),

        // 6. Drivers & Runtimes Oficiais
        new("Microsoft.DirectX", "DirectX End-User Runtime", "Microsoft", SoftwarePackageCategory.Drivers, "Drivers", "Bibliotecas runtime oficiais do Microsoft DirectX.", "winget", true, true),
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ Redistributable 2015-2022 x64", "Microsoft", SoftwarePackageCategory.Drivers, "Drivers", "Pacote redistribuível oficial Microsoft Visual C++ x64 para jogos e aplicativos.", "winget", true, true),

        // 7. Monitoramento
        new("REALiX.HWiNFO", "HWiNFO64", "REALiX", SoftwarePackageCategory.Monitoramento, "Monitoramento", "Análise de sensores físicos e diagnóstico detalhado de hardware.", "winget", true, true),
        new("Almico.SpeedFan", "PresentMon / GPU-Z Companion", "TechPowerUp", SoftwarePackageCategory.Monitoramento, "Monitoramento", "Utilitário de inspeção de telemetria gráfica.", "winget", true, true),

        // 8. Hardware
        new("CPUID.CPU-Z", "CPU-Z", "CPUID", SoftwarePackageCategory.Hardware, "Hardware", "Identificação detalhada de processador, placa-mãe e timings de memória RAM.", "winget", true, true),
        new("TechPowerUp.GPU-Z", "TechPowerUp GPU-Z", "TechPowerUp", SoftwarePackageCategory.Hardware, "Hardware", "Inspeção técnica de subsistema gráfico para AMD, NVIDIA e Intel.", "winget", true, true),

        // 9. Gaming
        new("Valve.Steam", "Steam", "Valve Corporation", SoftwarePackageCategory.Gaming, "Gaming", "Plataforma oficial de distribuição e comunidade de jogos PC.", "winget", true, true),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Epic Games", SoftwarePackageCategory.Gaming, "Gaming", "Inicializador oficial de jogos Epic Games e Unreal Engine.", "winget", true, true),
        new("Playnite.Playnite", "Playnite", "Josef Nemec", SoftwarePackageCategory.Gaming, "Gaming", "Gerenciador unificado de bibliotecas de jogos de código aberto.", "winget", false, true),

        // 10. Benchmark
        new("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", "Crystal Dew World", SoftwarePackageCategory.Benchmark, "Benchmark", "Benchmark sequencial e aleatório para SSDs NVMe, SATA e HDDs.", "winget", true, true),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Crystal Dew World", SoftwarePackageCategory.Benchmark, "Benchmark", "Monitoramento de saúde SMART e temperatura de unidades de armazenamento.", "winget", true, true),

        // 11. Áudio/Vídeo
        new("VideoLAN.VLC", "VLC Media Player", "VideoLAN", SoftwarePackageCategory.AudioVideo, "Áudio/Vídeo", "Reprodutor multimídia universal de código aberto.", "winget", true, true),
        new("OBSProject.OBSStudio", "OBS Studio", "OBS Project", SoftwarePackageCategory.AudioVideo, "Áudio/Vídeo", "Gravação de vídeo e transmissão ao vivo acelerada por hardware (NVENC/AMF/QSV).", "winget", true, true),

        // 12. Desenvolvimento
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Microsoft", SoftwarePackageCategory.Desenvolvimento, "Desenvolvimento", "Editor de código-fonte extensível multiplataforma.", "winget", false, true),
        new("Git.Git", "Git for Windows", "Git Development Community", SoftwarePackageCategory.Desenvolvimento, "Desenvolvimento", "Sistema de controle de versão distribuído.", "winget", true, true),

        // 13. Ferramentas de Sistema
        new("Microsoft.PowerToys", "Microsoft PowerToys", "Microsoft", SoftwarePackageCategory.FerramentasDeSistema, "Ferramentas de Sistema", "Conjunto oficial de utilitários avançados para produtividade no Windows 10/11.", "winget", true, true),
        new("WireGuard.WireGuard", "WireGuard for Windows", "WireGuard LLC", SoftwarePackageCategory.FerramentasDeSistema, "Ferramentas de Sistema", "Cliente oficial de túnel VPN criptografado de baixa latência.", "winget", true, true),

        // 14. Personalização
        new("TranslucentTB.TranslucentTB", "TranslucentTB", "Charles Milette", SoftwarePackageCategory.Personalizacao, "Personalização", "Utilitário leve para personalização da barra de tarefas do Windows.", "winget", false, true)
    ];

    public IReadOnlyList<WingetSoftwarePackage> SearchCatalog(string? query = null, SoftwarePackageCategory? category = null)
    {
        IEnumerable<WingetSoftwarePackage> filtered = OfficialCatalog;
        if (category.HasValue)
        {
            filtered = filtered.Where(p => p.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            filtered = filtered.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.PackageId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CategoryDisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }

    public async Task<bool> IsWingetAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var res = await RunWingetCommandAsync("--version", cancellationToken);
            return res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.Output);
        }
        catch
        {
            return false;
        }
    }

    public async Task<WingetOperationProgress> VerifyInstalledPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var pkg = ValidateCatalogPackage(packageId);
        if (pkg == null)
        {
            return new WingetOperationProgress(
                packageId,
                "Verify",
                "Rejected",
                -1,
                false,
                $"Pacote '{packageId}' não consta no catálogo verificado do Software Center.",
                DateTime.UtcNow
            );
        }

        if (!OperatingSystem.IsWindows())
        {
            return new WingetOperationProgress(
                pkg.PackageId,
                "Verify",
                "RequiresWindowsHost",
                -1,
                false,
                "Verificação via winget requer sistema operacional Windows 10/11 nativo.",
                DateTime.UtcNow
            );
        }

        var (exitCode, output) = await RunWingetCommandAsync(
            $"list --id \"{pkg.PackageId}\" --exact --accept-source-agreements --disable-interactivity",
            cancellationToken
        );

        bool installed = exitCode == 0 && output.Contains(pkg.PackageId, StringComparison.OrdinalIgnoreCase);
        return new WingetOperationProgress(
            pkg.PackageId,
            "Verify",
            installed ? "Installed" : "NotInstalled",
            exitCode,
            installed,
            output,
            DateTime.UtcNow
        );
    }

    public async Task<WingetOperationProgress> InstallPackageAsync(
        string packageId,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            return new WingetOperationProgress(
                packageId,
                "Install",
                "ConfirmationRequired",
                -1,
                false,
                "Confirmação explícita do usuário é obrigatória antes de iniciar a instalação.",
                DateTime.UtcNow
            );
        }

        var pkg = ValidateCatalogPackage(packageId);
        if (pkg == null)
        {
            return new WingetOperationProgress(
                packageId,
                "Install",
                "Rejected",
                -1,
                false,
                $"Pacote '{packageId}' não pertence ao catálogo oficial verificado.",
                DateTime.UtcNow
            );
        }

        if (pkg.RequiresAdminUac && !CheckIsAdministrator())
        {
            return new WingetOperationProgress(
                pkg.PackageId,
                "Install",
                "RequiresAdmin",
                -1,
                false,
                $"O pacote '{pkg.Name}' exige elevação de Administrador (UAC) para instalação no escopo do sistema.",
                DateTime.UtcNow
            );
        }

        _operationQueue.Enqueue(pkg.PackageId);
        var (exitCode, output) = await RunWingetCommandAsync(
            $"install --id \"{pkg.PackageId}\" --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity",
            cancellationToken
        );

        return new WingetOperationProgress(
            pkg.PackageId,
            "Install",
            exitCode == 0 ? "Completed" : "Failed",
            exitCode,
            exitCode == 0,
            output,
            DateTime.UtcNow
        );
    }

    public async Task<WingetOperationProgress> UpdatePackageAsync(
        string packageId,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            return new WingetOperationProgress(
                packageId,
                "Update",
                "ConfirmationRequired",
                -1,
                false,
                "Confirmação explícita do usuário é obrigatória antes de atualizar o pacote.",
                DateTime.UtcNow
            );
        }

        var pkg = ValidateCatalogPackage(packageId);
        if (pkg == null)
        {
            return new WingetOperationProgress(packageId, "Update", "Rejected", -1, false, "Pacote fora do catálogo verificado.", DateTime.UtcNow);
        }

        var (exitCode, output) = await RunWingetCommandAsync(
            $"upgrade --id \"{pkg.PackageId}\" --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity",
            cancellationToken
        );

        return new WingetOperationProgress(
            pkg.PackageId,
            "Update",
            exitCode == 0 ? "Completed" : "Failed",
            exitCode,
            exitCode == 0,
            output,
            DateTime.UtcNow
        );
    }

    public async Task<WingetOperationProgress> UninstallPackageAsync(
        string packageId,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            return new WingetOperationProgress(
                packageId,
                "Uninstall",
                "ConfirmationRequired",
                -1,
                false,
                "Confirmação explícita do usuário é obrigatória antes de desinstalar.",
                DateTime.UtcNow
            );
        }

        var pkg = ValidateCatalogPackage(packageId);
        if (pkg == null)
        {
            return new WingetOperationProgress(packageId, "Uninstall", "Rejected", -1, false, "Pacote fora do catálogo verificado.", DateTime.UtcNow);
        }

        var (exitCode, output) = await RunWingetCommandAsync(
            $"uninstall --id \"{pkg.PackageId}\" --exact --accept-source-agreements --disable-interactivity",
            cancellationToken
        );

        return new WingetOperationProgress(
            pkg.PackageId,
            "Uninstall",
            exitCode == 0 ? "Completed" : "Failed",
            exitCode,
            exitCode == 0,
            output,
            DateTime.UtcNow
        );
    }

    private static WingetSoftwarePackage? ValidateCatalogPackage(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return null;
        return OfficialCatalog.FirstOrDefault(p => string.Equals(p.PackageId, packageId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(int ExitCode, string Output)> RunWingetCommandAsync(string arguments, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return (-1, "winget disponível apenas em ambiente Windows 10/11.");

        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "Não foi possível iniciar o processo winget.exe.");

        string stdOut = await process.StandardOutput.ReadToEndAsync(ct);
        string stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        string combined = string.IsNullOrWhiteSpace(stdErr) ? stdOut.Trim() : $"{stdOut.Trim()}\n{stdErr.Trim()}";
        return (process.ExitCode, combined);
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
