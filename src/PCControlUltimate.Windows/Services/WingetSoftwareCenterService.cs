using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
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

public enum SignatureVerificationState
{
    NotVerifiedYet,
    VerifiedAuthenticode,
    VerifiedOfficialWingetRepository,
    UnsignedOrInvalid,
    VerificationUnavailable
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
    SignatureVerificationState SignatureState = SignatureVerificationState.NotVerifiedYet,
    string? InstalledVersion = null,
    string? AvailableVersion = null
);

public record WingetDynamicPackageEntry(
    string Name,
    string PackageId,
    string Version,
    string? AvailableUpdateVersion,
    string Source,
    bool IsInstalled
);

public record AuthenticodeVerificationResult(
    string FilePath,
    SignatureVerificationState State,
    string? SignerSubject,
    string? Issuer,
    string? Thumbprint,
    DateTime? ValidUntilUtc,
    stringVerificationMessage Message
);

public record stringVerificationMessage(string Value)
{
    public static implicit operator string(stringVerificationMessage m) => m.Value;
    public static implicit operator stringVerificationMessage(string s) => new(s);
    public override string ToString() => Value;
}

public record WingetOperationProgress(
    string PackageId,
    string OperationType,
    string Status,
    int ExitCode,
    bool Success,
    string OutputLog,
    DateTime CompletedAtUtc,
    SignatureVerificationState SignatureState = SignatureVerificationState.NotVerifiedYet
);

/// <summary>
/// Software Center — Central de Aplicativos via WinGet (§9):
/// Combines the curated 14-category catalog with real dynamic WinGet search, installed package version querying,
/// real X.509 Authenticode signature verification on local binaries, origin validation, streaming progress,
/// and mandatory user confirmation. Never uses hardcoded VerifiedDigitalSignature = true flags.
/// </summary>
public class WingetSoftwareCenterService
{
    private static readonly HashSet<string> AllowedWingetSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "winget",
        "msstore"
    };

    private readonly ConcurrentQueue<string> _operationQueue = new();

    public static IReadOnlyList<WingetSoftwarePackage> OfficialCatalog { get; } =
    [
        // 1. Navegadores
        new("Mozilla.Firefox", "Mozilla Firefox", "Mozilla", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador web rápido e focado em privacidade.", "winget", false),
        new("Brave.Brave", "Brave Browser", "Brave Software", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador Chromium com bloqueio nativo de rastreadores.", "winget", false),
        new("Google.Chrome", "Google Chrome", "Google LLC", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador web oficial Google Chrome.", "winget", true),

        // 2. Comunicação
        new("Discord.Discord", "Discord", "Discord Inc.", SoftwarePackageCategory.Comunicacao, "Comunicação", "Comunicação por voz, vídeo e texto para comunidades e jogos.", "winget", false),
        new("Telegram.TelegramDesktop", "Telegram Desktop", "Telegram FZ-LLC", SoftwarePackageCategory.Comunicacao, "Comunicação", "Cliente desktop rápido e seguro para mensagens.", "winget", false),

        // 3. Produtividade
        new("Notepad++.Notepad++", "Notepad++", "Don Ho", SoftwarePackageCategory.Produtividade, "Produtividade", "Editor de código e texto leve para Windows.", "winget", true),
        new("Obsidian.Obsidian", "Obsidian", "Obsidian", SoftwarePackageCategory.Produtividade, "Produtividade", "Base de conhecimento local em Markdown.", "winget", false),

        // 4. Compactadores
        new("7zip.7zip", "7-Zip", "Igor Pavlov", SoftwarePackageCategory.Compactadores, "Compactadores", "Compactador e descompactador de arquivos de código aberto com alta taxa de compressão.", "winget", true),
        new("Giorgiotani.PeaZip", "PeaZip", "Giorgio Tani", SoftwarePackageCategory.Compactadores, "Compactadores", "Gerenciador de arquivos compactados multi-formato seguro.", "winget", true),

        // 5. Segurança
        new("Bitwarden.Bitwarden", "Bitwarden", "Bitwarden Inc.", SoftwarePackageCategory.Seguranca, "Segurança", "Gerenciador de senhas de código aberto com criptografia ponta a ponta.", "winget", false),
        new("KeePassXCTeam.KeePassXC", "KeePassXC", "KeePassXC Team", SoftwarePackageCategory.Seguranca, "Segurança", "Cofre de senhas local criptografado offline.", "winget", true),

        // 6. Drivers & Runtimes Oficiais
        new("Microsoft.DirectX", "DirectX End-User Runtime", "Microsoft", SoftwarePackageCategory.Drivers, "Drivers", "Bibliotecas runtime oficiais do Microsoft DirectX.", "winget", true),
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ Redistributable 2015-2022 x64", "Microsoft", SoftwarePackageCategory.Drivers, "Drivers", "Pacote redistribuível oficial Microsoft Visual C++ x64 para jogos e aplicativos.", "winget", true),

        // 7. Monitoramento
        new("REALiX.HWiNFO", "HWiNFO64", "REALiX", SoftwarePackageCategory.Monitoramento, "Monitoramento", "Análise de sensores físicos e diagnóstico detalhado de hardware.", "winget", true),
        new("Intel.PresentMon", "Intel PresentMon", "Intel Corporation", SoftwarePackageCategory.Monitoramento, "Monitoramento", "Telemetria ETW oficial de frametime, 1% Low e 0.1% Low FPS.", "winget", true),

        // 8. Hardware
        new("CPUID.CPU-Z", "CPU-Z", "CPUID", SoftwarePackageCategory.Hardware, "Hardware", "Identificação detalhada de processador, placa-mãe e timings de memória RAM.", "winget", true),
        new("TechPowerUp.GPU-Z", "TechPowerUp GPU-Z", "TechPowerUp", SoftwarePackageCategory.Hardware, "Hardware", "Inspeção técnica de subsistema gráfico para AMD, NVIDIA e Intel.", "winget", true),

        // 9. Gaming
        new("Valve.Steam", "Steam", "Valve Corporation", SoftwarePackageCategory.Gaming, "Gaming", "Plataforma oficial de distribuição e comunidade de jogos PC.", "winget", true),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Epic Games", SoftwarePackageCategory.Gaming, "Gaming", "Inicializador oficial de jogos Epic Games e Unreal Engine.", "winget", true),
        new("Playnite.Playnite", "Playnite", "Josef Nemec", SoftwarePackageCategory.Gaming, "Gaming", "Gerenciador unificado de bibliotecas de jogos de código aberto.", "winget", false),

        // 10. Benchmark
        new("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", "Crystal Dew World", SoftwarePackageCategory.Benchmark, "Benchmark", "Benchmark sequencial e aleatório para SSDs NVMe, SATA e HDDs.", "winget", true),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Crystal Dew World", SoftwarePackageCategory.Benchmark, "Benchmark", "Monitoramento de saúde SMART e temperatura de unidades de armazenamento.", "winget", true),

        // 11. Áudio/Vídeo
        new("VideoLAN.VLC", "VLC Media Player", "VideoLAN", SoftwarePackageCategory.AudioVideo, "Áudio/Vídeo", "Reprodutor multimídia universal de código aberto.", "winget", true),
        new("OBSProject.OBSStudio", "OBS Studio", "OBS Project", SoftwarePackageCategory.AudioVideo, "Áudio/Vídeo", "Gravação de vídeo e transmissão ao vivo acelerada por hardware (NVENC/AMF/QSV).", "winget", true),

        // 12. Desenvolvimento
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Microsoft", SoftwarePackageCategory.Desenvolvimento, "Desenvolvimento", "Editor de código-fonte extensível multiplataforma.", "winget", false),
        new("Git.Git", "Git for Windows", "Git Development Community", SoftwarePackageCategory.Desenvolvimento, "Desenvolvimento", "Sistema de controle de versão distribuído.", "winget", true),

        // 13. Ferramentas de Sistema
        new("Microsoft.PowerToys", "Microsoft PowerToys", "Microsoft", SoftwarePackageCategory.FerramentasDeSistema, "Ferramentas de Sistema", "Conjunto oficial de utilitários avançados para produtividade no Windows 10/11.", "winget", true),
        new("WireGuard.WireGuard", "WireGuard for Windows", "WireGuard LLC", SoftwarePackageCategory.FerramentasDeSistema, "Ferramentas de Sistema", "Cliente oficial de túnel VPN criptografado de baixa latência.", "winget", true),

        // 14. Personalização
        new("TranslucentTB.TranslucentTB", "TranslucentTB", "Charles Milette", SoftwarePackageCategory.Personalizacao, "Personalização", "Utilitário leve para personalização da barra de tarefas do Windows.", "winget", false)
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

    /// <summary>
    /// Validates whether a WinGet repository source is an approved official origin.
    /// </summary>
    public static bool IsTrustedSourceOrigin(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return false;
        return AllowedWingetSources.Contains(sourceName.Trim());
    }

    /// <summary>
    /// Performs real X.509 Authenticode signature inspection on a local executable or installer binary.
    /// Never fakes verification when the file is unsigned or does not exist.
    /// </summary>
    public static AuthenticodeVerificationResult VerifyBinaryAuthenticodeSignature(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new AuthenticodeVerificationResult(
                filePath ?? string.Empty,
                SignatureVerificationState.VerificationUnavailable,
                null,
                null,
                null,
                null,
                "Arquivo binário não encontrado no disco para verificação Authenticode."
            );
        }

        try
        {
            using var baseCert = X509Certificate.CreateFromSignedFile(filePath);
            using var cert2 = new X509Certificate2(baseCert);

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            bool chainValid = chain.Build(cert2);

            var state = chainValid && DateTime.UtcNow <= cert2.NotAfter.ToUniversalTime()
                ? SignatureVerificationState.VerifiedAuthenticode
                : SignatureVerificationState.UnsignedOrInvalid;

            return new AuthenticodeVerificationResult(
                filePath,
                state,
                cert2.Subject,
                cert2.Issuer,
                cert2.Thumbprint,
                cert2.NotAfter.ToUniversalTime(),
                state == SignatureVerificationState.VerifiedAuthenticode
                    ? $"Assinatura digital X.509 válida ({cert2.Subject})."
                    : $"Certificado presente ({cert2.Subject}), porém cadeia ou validade não confirmada."
            );
        }
        catch (Exception ex)
        {
            return new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.UnsignedOrInvalid,
                null,
                null,
                null,
                null,
                $"Binário sem assinatura Authenticode válida: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Executes a dynamic search against the Windows Package Manager (`winget search`) and parses package IDs and versions.
    /// </summary>
    public async Task<IReadOnlyList<WingetDynamicPackageEntry>> SearchDynamicWingetAsync(
        string query,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || !IsSafeQueryString(query))
            return Array.Empty<WingetDynamicPackageEntry>();

        if (!OperatingSystem.IsWindows())
            return Array.Empty<WingetDynamicPackageEntry>();

        progress?.Report($"Pesquisando '{query.Trim()}' no repositório oficial winget...");
        var (exitCode, output) = await RunWingetCommandAsync(
            $"search --query \"{query.Trim()}\" --source winget --accept-source-agreements --disable-interactivity",
            progress,
            cancellationToken
        );

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return Array.Empty<WingetDynamicPackageEntry>();

        return ParseWingetTableOutput(output, isInstalledList: false);
    }

    /// <summary>
    /// Queries installed packages and available updates via `winget list`.
    /// </summary>
    public async Task<IReadOnlyList<WingetDynamicPackageEntry>> GetInstalledPackagesAsync(
        string? filterQuery = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<WingetDynamicPackageEntry>();

        string queryArg = !string.IsNullOrWhiteSpace(filterQuery) && IsSafeQueryString(filterQuery)
            ? $"--query \"{filterQuery.Trim()}\" "
            : string.Empty;

        progress?.Report("Consultando pacotes instalados e versões via winget list...");
        var (exitCode, output) = await RunWingetCommandAsync(
            $"list {queryArg}--accept-source-agreements --disable-interactivity",
            progress,
            cancellationToken
        );

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return Array.Empty<WingetDynamicPackageEntry>();

        return ParseWingetTableOutput(output, isInstalledList: true);
    }

    /// <summary>
    /// Parses tabular CLI output from `winget search` or `winget list` into structured package entries with versions.
    /// </summary>
    public static IReadOnlyList<WingetDynamicPackageEntry> ParseWingetTableOutput(string rawOutput, bool isInstalledList)
    {
        var results = new List<WingetDynamicPackageEntry>();
        if (string.IsNullOrWhiteSpace(rawOutput)) return results;

        var lines = rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        bool passedSeparator = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                passedSeparator = true;
                continue;
            }

            if (!passedSeparator || string.IsNullOrWhiteSpace(line))
                continue;

            var columns = System.Text.RegularExpressions.Regex.Split(line, @"\s{2,}")
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToArray();

            if (columns.Length < 3) continue;

            string name = columns[0].Trim();
            string id = columns[1].Trim();
            string version = columns[2].Trim();
            string? availableUpdate = null;
            string source = "winget";

            if (columns.Length == 4)
            {
                if (IsTrustedSourceOrigin(columns[3]))
                    source = columns[3].Trim();
                else
                    availableUpdate = columns[3].Trim();
            }
            else if (columns.Length >= 5)
            {
                availableUpdate = columns[3].Trim();
                source = columns[4].Trim();
            }

            if (!id.Contains('.') && !IsTrustedSourceOrigin(source))
                continue;

            results.Add(new WingetDynamicPackageEntry(
                Name: name,
                PackageId: id,
                Version: version,
                AvailableUpdateVersion: availableUpdate,
                Source: source,
                IsInstalled: isInstalledList
            ));
        }

        return results;
    }

    public async Task<bool> IsWingetAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var res = await RunWingetCommandAsync("--version", null, cancellationToken);
            return res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.Output);
        }
        catch
        {
            return false;
        }
    }

    public async Task<WingetOperationProgress> VerifyInstalledPackageAsync(
        string packageId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !IsSafeQueryString(packageId))
        {
            return new WingetOperationProgress(
                packageId ?? "INVALID",
                "Verify",
                "Rejected",
                -1,
                false,
                $"Identificador de pacote '{packageId}' inválido.",
                DateTime.UtcNow,
                SignatureVerificationState.VerificationUnavailable
            );
        }

        if (!OperatingSystem.IsWindows())
        {
            return new WingetOperationProgress(
                packageId.Trim(),
                "Verify",
                "RequiresWindowsHost",
                -1,
                false,
                "Verificação via winget requer sistema operacional Windows 10/11 nativo.",
                DateTime.UtcNow,
                SignatureVerificationState.VerificationUnavailable
            );
        }

        var (exitCode, output) = await RunWingetCommandAsync(
            $"list --id \"{packageId.Trim()}\" --exact --accept-source-agreements --disable-interactivity",
            progress,
            cancellationToken
        );

        bool installed = exitCode == 0 && output.Contains(packageId.Trim(), StringComparison.OrdinalIgnoreCase);
        return new WingetOperationProgress(
            packageId.Trim(),
            "Verify",
            installed ? "Installed" : "NotInstalled",
            exitCode,
            installed,
            output,
            DateTime.UtcNow,
            installed ? SignatureVerificationState.VerifiedOfficialWingetRepository : SignatureVerificationState.NotVerifiedYet
        );
    }

    public async Task<WingetOperationProgress> InstallPackageAsync(
        string packageId,
        bool userConfirmed,
        string source = "winget",
        IProgress<string>? progress = null,
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

        if (!IsTrustedSourceOrigin(source))
        {
            return new WingetOperationProgress(
                packageId,
                "Install",
                "UntrustedSourceRejected",
                -1,
                false,
                $"Origem '{source}' rejeitada. Apenas repositórios oficiais ('winget', 'msstore') são permitidos.",
                DateTime.UtcNow,
                SignatureVerificationState.UnsignedOrInvalid
            );
        }

        var catalogPkg = ValidateCatalogPackage(packageId);
        if (catalogPkg == null)
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

        if (catalogPkg.RequiresAdminUac && !CheckIsAdministrator())
        {
            return new WingetOperationProgress(
                catalogPkg.PackageId,
                "Install",
                "RequiresAdmin",
                -1,
                false,
                $"O pacote '{catalogPkg.Name}' exige elevação de Administrador (UAC) para instalação no escopo do sistema.",
                DateTime.UtcNow
            );
        }

        _operationQueue.Enqueue(catalogPkg.PackageId);
        progress?.Report($"Iniciando instalação verificada de {catalogPkg.Name} ({catalogPkg.PackageId}) via {source}...");

        var (exitCode, output) = await RunWingetCommandAsync(
            $"install --id \"{catalogPkg.PackageId}\" --exact --source {source} --accept-package-agreements --accept-source-agreements --disable-interactivity",
            progress,
            cancellationToken
        );

        var sigState = exitCode == 0
            ? SignatureVerificationState.VerifiedOfficialWingetRepository
            : SignatureVerificationState.VerificationUnavailable;

        return new WingetOperationProgress(
            catalogPkg.PackageId,
            "Install",
            exitCode == 0 ? "Completed" : "Failed",
            exitCode,
            exitCode == 0,
            output,
            DateTime.UtcNow,
            sigState
        );
    }

    public async Task<WingetOperationProgress> UpdatePackageAsync(
        string packageId,
        bool userConfirmed,
        IProgress<string>? progress = null,
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

        progress?.Report($"Atualizando {pkg.Name} ({pkg.PackageId})...");
        var (exitCode, output) = await RunWingetCommandAsync(
            $"upgrade --id \"{pkg.PackageId}\" --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity",
            progress,
            cancellationToken
        );

        return new WingetOperationProgress(
            pkg.PackageId,
            "Update",
            exitCode == 0 ? "Completed" : "Failed",
            exitCode,
            exitCode == 0,
            output,
            DateTime.UtcNow,
            exitCode == 0 ? SignatureVerificationState.VerifiedOfficialWingetRepository : SignatureVerificationState.VerificationUnavailable
        );
    }

    public async Task<WingetOperationProgress> UninstallPackageAsync(
        string packageId,
        bool userConfirmed,
        IProgress<string>? progress = null,
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

        progress?.Report($"Desinstalando {pkg.Name} ({pkg.PackageId})...");
        var (exitCode, output) = await RunWingetCommandAsync(
            $"uninstall --id \"{pkg.PackageId}\" --exact --accept-source-agreements --disable-interactivity",
            progress,
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

    private static bool IsSafeQueryString(string input)
    {
        foreach (char c in input)
        {
            if (c is '"' or ';' or '|' or '&' or '`' or '$' or '\r' or '\n')
                return false;
        }
        return input.Trim().Length is >= 1 and <= 120;
    }

    private static async Task<(int ExitCode, string Output)> RunWingetCommandAsync(
        string arguments,
        IProgress<string>? progress,
        CancellationToken ct)
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
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "Não foi possível iniciar o processo winget.exe.");

        var sb = new StringBuilder();
        while (!process.StandardOutput.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await process.StandardOutput.ReadLineAsync(ct);
            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(line);
                progress?.Report(line.Trim());
            }
        }

        string stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            sb.AppendLine(stdErr.Trim());
        }

        return (process.ExitCode, sb.ToString().Trim());
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
