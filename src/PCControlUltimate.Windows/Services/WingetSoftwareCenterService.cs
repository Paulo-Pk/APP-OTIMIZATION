using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    TamperedOrBadDigest,
    ExpiredCertificate,
    UntrustedRoot,
    InconclusiveRevocationOffline,
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
    int NativeWinTrustHResult,
    string? SignerSubject,
    string? Issuer,
    string? Thumbprint,
    DateTime? ValidUntilUtc,
    stringVerificationMessage Message,
    bool IsTimestamped = false
)
{
    public bool IsCryptographicallyValid => State == SignatureVerificationState.VerifiedAuthenticode;
}

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
/// - Real cryptographic Authenticode signature verification via native Windows WinVerifyTrust (wintrust.dll)
///   checking file digest integrity, certificate chain, expiration, signer identity, and revocation policy.
/// - Never marks a package as verified merely because a certificate is attached.
/// - Eliminates obsolete X509Certificate.CreateFromSignedFile warnings.
/// </summary>
public class WingetSoftwareCenterService
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public const int HResultSuccess = 0;
    public const int TrustENoSignature = unchecked((int)0x800B0100);
    public const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    public const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);
    public const int TrustEBadDigest = unchecked((int)0x80096010);
    public const int CertEExpired = unchecked((int)0x800B0101);
    public const int CertEUntrustedRoot = unchecked((int)0x800B0109);
    public const int CertERevoked = unchecked((int)0x800B010C);
    public const int CertERevocationFailure = unchecked((int)0x800B010E);
    public const int CryptESecuritySettings = unchecked((int)0x80092026);

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
        new("Opera.OperaGX", "Opera GX", "Opera", SoftwarePackageCategory.Navegadores, "Navegadores", "Navegador com recursos voltados a jogos e controles de consumo de recursos.", "winget", true),

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
        new("Microsoft.GamingApp", "Xbox para PC", "Microsoft", SoftwarePackageCategory.Gaming, "Gaming", "Aplicativo oficial Xbox para PC Game Pass e biblioteca de jogos compatíveis.", "winget", true),
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
        new("TranslucentTB.TranslucentTB", "TranslucentTB", "Charles Milette", SoftwarePackageCategory.Personalizacao, "Personalização", "Utilitário leve para personalização da barra de tarefas do Windows.", "winget", false),
        new("ElectronicArts.EADesktop", "EA app", "Electronic Arts", SoftwarePackageCategory.Gaming, "Gaming", "Cliente oficial para instalar e atualizar jogos da Electronic Arts.", "winget", true),
        new("Ubisoft.Connect", "Ubisoft Connect", "Ubisoft", SoftwarePackageCategory.Gaming, "Gaming", "Cliente oficial da Ubisoft para biblioteca, atualizações e serviços dos jogos.", "winget", true),
        new("GOG.Galaxy", "GOG GALAXY", "GOG", SoftwarePackageCategory.Gaming, "Gaming", "Cliente oficial para organizar e instalar jogos da biblioteca GOG.", "winget", true),
        new("9MV0B5HZVK9Z", "Xbox para PC", "Microsoft / Xbox", SoftwarePackageCategory.Gaming, "Gaming", "Aplicativo oficial Xbox para jogos de PC e Game Pass, distribuído pela Microsoft Store.", "msstore", false),
        new("Rem0o.FanControl", "Fan Control", "Rem0o", SoftwarePackageCategory.Hardware, "Hardware", "Controle de ventoinhas com curvas personalizadas em hardware compatível.", "winget", true),
        new("ShareX.ShareX", "ShareX", "ShareX Team", SoftwarePackageCategory.FerramentasDeSistema, "Ferramentas de Sistema", "Captura de tela e gravação de área de trabalho.", "winget", false)
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

    public static bool IsTrustedSourceOrigin(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return false;
        return AllowedWingetSources.Contains(sourceName.Trim());
    }

    /// <summary>
    /// Performs real cryptographic Authenticode signature verification using Windows native WinVerifyTrust (wintrust.dll):
    /// 1) Verifies PE/MSI/CAB cryptographic file digest against tampering (TRUST_E_BAD_DIGEST).
    /// 2) Verifies certificate trust chain and root authority (CERT_E_UNTRUSTEDROOT).
    /// 3) Verifies certificate validity period (CERT_E_EXPIRED).
    /// 4) Checks revocation across the chain when network is available, or reports InconclusiveRevocationOffline if CRL/OCSP is unreachable.
    /// 5) Extracts signer identity and PKCS#7 countersignature timestamp status via CryptQueryObject/CryptMsgGetParam without obsolete APIs.
    /// </summary>
    public static AuthenticodeVerificationResult VerifyBinaryAuthenticodeSignature(
        string filePath,
        bool checkOnlineRevocation = true)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new AuthenticodeVerificationResult(
                filePath ?? string.Empty,
                SignatureVerificationState.VerificationUnavailable,
                -1,
                null,
                null,
                null,
                null,
                "Arquivo binário não encontrado no disco para verificação Authenticode.",
                IsTimestamped: false
            );
        }

        if (!OperatingSystem.IsWindows())
        {
            return new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.VerificationUnavailable,
                -1,
                null,
                null,
                null,
                null,
                "Validação criptográfica nativa via WinVerifyTrust (wintrust.dll) requer sistema operacional Windows.",
                IsTimestamped: false
            );
        }

        int hResult = ExecuteNativeWinVerifyTrust(filePath, checkOnlineRevocation);
        var (subject, issuer, thumbprint, validUntilUtc, isTimestamped) = TryReadSignerMetadata(filePath);

        return MapWinTrustHResultToVerificationResult(
            filePath,
            hResult,
            subject,
            issuer,
            thumbprint,
            validUntilUtc,
            isTimestamped
        );
    }

    /// <summary>
    /// Overload accepting (int hResult, string filePath, ...) for deterministic unit testing of WinVerifyTrust HRESULT states.
    /// </summary>
    public static AuthenticodeVerificationResult MapWinTrustHResultToVerificationResult(
        int hResult,
        string filePath,
        string? signerSubject = null,
        string? issuer = null,
        string? thumbprint = null,
        bool isTimestamped = false,
        DateTime? validUntilUtc = null)
    {
        return MapWinTrustHResultToVerificationResult(
            filePath,
            hResult,
            signerSubject,
            issuer,
            thumbprint,
            validUntilUtc,
            isTimestamped
        );
    }

    /// <summary>
    /// Pure mapping helper from WinVerifyTrust HRESULT to AuthenticodeVerificationResult so all cryptographic error states
    /// (tampered digest, unsigned, expired, untrusted root, revocation offline) are deterministically testable.
    /// </summary>
    public static AuthenticodeVerificationResult MapWinTrustHResultToVerificationResult(
        string filePath,
        int hResult,
        string? signerSubject = null,
        string? issuer = null,
        string? thumbprint = null,
        DateTime? validUntilUtc = null,
        bool isTimestamped = false)
    {
        return hResult switch
        {
            HResultSuccess => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.VerifiedAuthenticode,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                $"Integridade criptográfica Authenticode e cadeia de confiança verificadas via WinVerifyTrust ({signerSubject ?? "Signatário confiável"}).",
                IsTimestamped: isTimestamped
            ),
            TrustEBadDigest => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.TamperedOrBadDigest,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                "ALERTA CRÍTICO: O hash do arquivo não corresponde à assinatura Authenticode (TRUST_E_BAD_DIGEST — arquivo corrompido ou adulterado).",
                IsTimestamped: isTimestamped
            ),
            CertEExpired => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.ExpiredCertificate,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                "Assinatura rejeitada: O certificado do signatário expirou e não possui carimbo de tempo válido (CERT_E_EXPIRED).",
                IsTimestamped: isTimestamped
            ),
            CertEUntrustedRoot => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.UntrustedRoot,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                "Assinatura rejeitada: A cadeia de certificação termina em uma autoridade raiz não confiável (CERT_E_UNTRUSTEDROOT).",
                IsTimestamped: isTimestamped
            ),
            CertERevocationFailure => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.InconclusiveRevocationOffline,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                "Verificação inconclusiva: Não foi possível contatar o servidor de revogação CRL/OCSP (CERT_E_REVOCATION_FAILURE).",
                IsTimestamped: isTimestamped
            ),
            TrustENoSignature or TrustESubjectNotTrusted or TrustEExplicitDistrust or CertERevoked or CryptESecuritySettings => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.UnsignedOrInvalid,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                $"Binário sem assinatura Authenticode válida ou revogado (HRESULT: 0x{hResult:X8}).",
                IsTimestamped: isTimestamped
            ),
            _ => new AuthenticodeVerificationResult(
                filePath,
                SignatureVerificationState.UnsignedOrInvalid,
                hResult,
                signerSubject,
                issuer,
                thumbprint,
                validUntilUtc,
                $"Falha na validação criptográfica WinVerifyTrust (HRESULT: 0x{hResult:X8}).",
                IsTimestamped: isTimestamped
            )
        };
    }

    private static int ExecuteNativeWinVerifyTrust(string filePath, bool checkOnlineRevocation)
    {
        IntPtr pFilePath = IntPtr.Zero;
        IntPtr pFileInfo = IntPtr.Zero;
        Guid action = WintrustActionGenericVerifyV2;
        var trustData = new WINTRUST_DATA();
        bool stateOpened = false;

        try
        {
            pFilePath = Marshal.StringToCoTaskMemUni(filePath);
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = pFilePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            trustData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = checkOnlineRevocation ? 1u : 0u, // 1 = WTD_REVOKE_WHOLECHAIN, 0 = WTD_REVOKE_NONE
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = pFileInfo,
                dwStateAction = 1, // WTD_STATEACTION_VERIFY
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = checkOnlineRevocation
                    ? 0x00000040u | 0x00000800u // WTD_REVOCATION_CHECK_CHAIN | WTD_LifetimeSigningFlag
                    : 0x00000010u | 0x00000800u, // WTD_CACHE_ONLY_URL_RETRIVIAL
                dwUIContext = 0
            };

            stateOpened = true;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
        }
        finally
        {
            if (stateOpened && trustData.hWVTStateData != IntPtr.Zero)
            {
                trustData.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            }

            if (pFileInfo != IntPtr.Zero)
                Marshal.FreeHGlobal(pFileInfo);

            if (pFilePath != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pFilePath);
        }
    }

    private static (string? Subject, string? Issuer, string? Thumbprint, DateTime? ValidUntilUtc, bool IsTimestamped) TryReadSignerMetadata(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return (null, null, null, null, false);

        IntPtr hStore = IntPtr.Zero;
        IntPtr hMsg = IntPtr.Zero;
        IntPtr pCertContext = IntPtr.Zero;

        try
        {
            const int CERT_QUERY_OBJECT_FILE = 1;
            const int CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED = 1 << 10;
            const int CERT_QUERY_FORMAT_FLAG_BINARY = 1 << 1;

            bool ok = CryptQueryObject(
                CERT_QUERY_OBJECT_FILE,
                filePath,
                CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED,
                CERT_QUERY_FORMAT_FLAG_BINARY,
                0,
                out _,
                out _,
                out _,
                ref hStore,
                ref hMsg,
                IntPtr.Zero
            );

            if (!ok || hStore == IntPtr.Zero)
                return (null, null, null, null, false);

            bool hasTimestamp = false;
            if (hMsg != IntPtr.Zero)
            {
                const int CMSG_SIGNER_UNAUTH_ATTR_PARAM = 10;
                uint cbData = 0;
                if (CryptMsgGetParam(hMsg, CMSG_SIGNER_UNAUTH_ATTR_PARAM, 0, IntPtr.Zero, ref cbData) && cbData > 0)
                {
                    hasTimestamp = true;
                }
            }

            pCertContext = CertEnumCertificatesInStore(hStore, IntPtr.Zero);
            if (pCertContext == IntPtr.Zero)
                return (null, null, null, null, hasTimestamp);

            using var cert2 = new X509Certificate2(pCertContext);
            return (
                cert2.Subject,
                cert2.Issuer,
                cert2.Thumbprint,
                cert2.NotAfter.ToUniversalTime(),
                hasTimestamp
            );
        }
        catch
        {
            return (null, null, null, null, false);
        }
        finally
        {
            if (pCertContext != IntPtr.Zero)
                CertFreeCertificateContext(pCertContext);
            if (hStore != IntPtr.Zero)
                CertCloseStore(hStore, 0);
            if (hMsg != IntPtr.Zero)
                CryptMsgClose(hMsg);
        }
    }

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

        bool installed = exitCode == 0 && ParseWingetTableOutput(output, isInstalledList: true)
            .Any(package => string.Equals(package.PackageId, packageId.Trim(), StringComparison.OrdinalIgnoreCase));

        // "winget list" confirms presence and package identity only; it does not
        // cryptographically verify the installed binary or prove its current signature.
        return new WingetOperationProgress(
            packageId.Trim(),
            "Verify",
            installed ? "Installed" : "NotInstalled",
            exitCode,
            installed,
            output,
            DateTime.UtcNow,
            SignatureVerificationState.NotVerifiedYet
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

        // The source is part of the reviewed package metadata (for example, Xbox uses msstore).
        source = catalogPkg.OfficialSource;
        if (!IsTrustedSourceOrigin(source))
        {
            return new WingetOperationProgress(
                catalogPkg.PackageId,
                "Install",
                "UntrustedSourceRejected",
                -1,
                false,
                $"Origem '{source}' rejeitada. Apenas repositórios oficiais ('winget', 'msstore') são permitidos.",
                DateTime.UtcNow,
                SignatureVerificationState.UnsignedOrInvalid
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
            $"upgrade --id \"{pkg.PackageId}\" --exact --source {pkg.OfficialSource} --accept-package-agreements --accept-source-agreements --disable-interactivity",
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

        ct.ThrowIfCancellationRequested();
        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "Não foi possível iniciar o processo winget.exe.");

        // Drain stderr concurrently with stdout. Reading one redirected stream to
        // completion before touching the other can deadlock when its OS pipe fills.
        var standardErrorTask = process.StandardError.ReadToEndAsync(ct);
        var output = new StringBuilder();

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string? line = await process.StandardOutput.ReadLineAsync(ct);
                if (line == null) break;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    output.AppendLine(line);
                    progress?.Report(line.Trim());
                }
            }

            await process.WaitForExitAsync(ct);
            string standardError = await standardErrorTask;
            if (!string.IsNullOrWhiteSpace(standardError))
                output.AppendLine(standardError.Trim());

            return (process.ExitCode, output.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Preserve the original cancellation result if the process already exited.
            }

            // Bound process shutdown and stderr draining as well. If termination fails
            // or a descendant keeps the redirected pipe open, cancellation must not hang.
            try
            {
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch { }

            try
            {
                await standardErrorTask
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .ConfigureAwait(false);
            }
            catch { }

            throw;
        }
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] ref Guid pgActionID,
        ref WINTRUST_DATA pWVTData);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptQueryObject(
        int dwObjectType,
        [MarshalAs(UnmanagedType.LPWStr)] string pvObject,
        int dwExpectedContentTypeFlags,
        int dwExpectedFormatTypeFlags,
        int dwFlags,
        out int pdwMsgAndCertEncodingType,
        out int pdwContentType,
        out int pdwFormatType,
        ref IntPtr phCertStore,
        ref IntPtr phMsg,
        IntPtr ppvContext);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern IntPtr CertEnumCertificatesInStore(IntPtr hCertStore, IntPtr pPrevCertContext);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CertFreeCertificateContext(IntPtr pCertContext);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CertCloseStore(IntPtr hCertStore, int dwFlags);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptMsgGetParam(
        IntPtr hCryptMsg,
        int dwParamType,
        int dwIndex,
        IntPtr pvData,
        ref uint pcbData);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptMsgClose(IntPtr hCryptMsg);
}
