using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Gaming.Network;

public record WireGuardConfigAuditResult(
    bool IsValid,
    string ErrorReason,
    string InterfaceAddress,
    string PeerPublicKey,
    string PeerEndpoint,
    IReadOnlyList<string> AllowedIps,
    bool AltersDefaultRoute,
    bool IsSelectiveGameTargetRouting,
    string RouteImpactExplanationPt,
    string RouteImpactExplanationEn
)
{
    public bool AltersDefaultWindowsRoute => AltersDefaultRoute;
    public bool AltersIpv4DefaultRoute { get; init; }
    public bool AltersIpv6DefaultRoute { get; init; }
}

public record WireGuardTunnelStatusReport(
    bool WireGuardInstalled,
    string? WireGuardBinaryPath,
    string TunnelName,
    bool ServiceInstalled,
    bool ServiceRunning,
    bool HandshakeVerified,
    long? LastHandshakeEpochSeconds,
    long BytesReceived,
    long BytesSent,
    IReadOnlyList<string> ActiveAllowedIps,
    string DiagnosticMessage
);

public record WireGuardServiceOperationResult(
    bool Success,
    string ErrorCode,
    string Message,
    bool UacCancelled = false,
    bool RouteTableRestored = false
);

public static class WireGuardTunnelManager
{
    private static readonly string[] CandidateWireGuardPaths =
    {
        @"C:\Program Files\WireGuard\wireguard.exe",
        @"C:\Program Files (x86)\WireGuard\wireguard.exe"
    };

    private static readonly string[] CandidateWgCliPaths =
    {
        @"C:\Program Files\WireGuard\wg.exe",
        @"C:\Program Files (x86)\WireGuard\wg.exe"
    };

    private static readonly Regex SafeTunnelNameRegex = new(
        @"^[A-Za-z0-9_\-]{3,32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    // Standard WireGuard Curve25519 key in Base64: 43 base64 chars + '=' (44 chars total)
    private static readonly Regex WireGuardBase64KeyRegex = new(
        @"^[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static string? _capturedRouteTableBeforeTunnel;

    public static (bool installed, string? wireGuardExePath, string? wgCliPath) CheckWireGuardInstallation()
    {
        string? wgExe = CandidateWireGuardPaths.FirstOrDefault(File.Exists);
        string? wgCli = CandidateWgCliPaths.FirstOrDefault(File.Exists);
        return (!string.IsNullOrEmpty(wgExe), wgExe, wgCli);
    }

    public static bool IsValidCidrBlock(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) return false;
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var ip)) return false;
        if (!int.TryParse(parts[1], out int prefix)) return false;

        return ip.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => prefix is >= 0 and <= 32,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => prefix is >= 0 and <= 128,
            _ => false
        };
    }

    public static bool IsValidWireGuardKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && WireGuardBase64KeyRegex.IsMatch(key.Trim());

    public static bool IsIpv4DefaultRouteCidr(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) return false;
        string normalized = cidr.Trim();
        return normalized.Equals("0.0.0.0/0", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("0.0.0.0/1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("128.0.0.0/1", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIpv6DefaultRouteCidr(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) return false;
        string normalized = cidr.Trim();
        return normalized.Equals("::/0", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("0:0:0:0:0:0:0:0/0", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("::/1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("8000::/1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates a WireGuard .conf payload, inspects AllowedIPs to determine whether it alters
    /// the Windows default route (0.0.0.0/0 or ::/0) vs. selective game server CIDR routing,
    /// and blocks unsafe configurations unless explicitly authorized.
    /// </summary>
    public static WireGuardConfigAuditResult ValidateAndAuditConfig(
        string? configContent,
        bool allowDefaultRouteOverride = false)
    {
        if (string.IsNullOrWhiteSpace(configContent))
        {
            return new WireGuardConfigAuditResult(
                false, "EMPTY_CONFIG: O conteúdo da configuração WireGuard está vazio.",
                string.Empty, string.Empty, string.Empty, Array.Empty<string>(),
                false, false, string.Empty, string.Empty
            );
        }

        bool inInterface = false;
        bool inPeer = false;
        string privateKey = string.Empty;
        string interfaceAddress = string.Empty;
        string peerPublicKey = string.Empty;
        string peerEndpoint = string.Empty;
        var allowedIps = new List<string>();

        var lines = configContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            // Reject PreUp/PostUp/PreDown/PostDown shell command hooks to prevent arbitrary command execution!
            if (line.StartsWith("PreUp", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PostUp", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PreDown", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PostDown", StringComparison.OrdinalIgnoreCase))
            {
                return new WireGuardConfigAuditResult(
                    false,
                    "UNSAFE_SCRIPT_HOOK_BLOCKED: Comandos PreUp/PostUp/PreDown/PostDown são bloqueados por segurança para impedir execução arbitrária de scripts.",
                    string.Empty, string.Empty, string.Empty, Array.Empty<string>(),
                    false, false, string.Empty, string.Empty
                );
            }

            if (line.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
            {
                inInterface = true;
                inPeer = false;
                continue;
            }

            if (line.Equals("[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                inPeer = true;
                inInterface = false;
                continue;
            }

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            string key = line[..eqIdx].Trim();
            string val = line[(eqIdx + 1)..].Trim();

            if (inInterface)
            {
                if (key.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase))
                    privateKey = val;
                else if (key.Equals("Address", StringComparison.OrdinalIgnoreCase))
                    interfaceAddress = val;
            }
            else if (inPeer)
            {
                if (key.Equals("PublicKey", StringComparison.OrdinalIgnoreCase))
                    peerPublicKey = val;
                else if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                    peerEndpoint = val;
                else if (key.Equals("AllowedIPs", StringComparison.OrdinalIgnoreCase))
                {
                    var splitIps = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    allowedIps.AddRange(splitIps);
                }
            }
        }

        if (!IsValidWireGuardKey(privateKey))
        {
            return new WireGuardConfigAuditResult(
                false, "INVALID_PRIVATE_KEY: Chave privada da seção [Interface] ausente ou fora do formato Curve25519 Base64.",
                interfaceAddress, peerPublicKey, peerEndpoint, allowedIps,
                false, false, string.Empty, string.Empty
            );
        }

        var ifaceCidrs = interfaceAddress.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ifaceCidrs.Length == 0 || !ifaceCidrs.All(IsValidCidrBlock))
        {
            return new WireGuardConfigAuditResult(
                false, "INVALID_INTERFACE_ADDRESS: Endereço IP/CIDR da interface do túnel é inválido.",
                interfaceAddress, peerPublicKey, peerEndpoint, allowedIps,
                false, false, string.Empty, string.Empty
            );
        }

        if (!IsValidWireGuardKey(peerPublicKey))
        {
            return new WireGuardConfigAuditResult(
                false, "INVALID_PEER_PUBLIC_KEY: PublicKey da seção [Peer] ausente ou inválida.",
                interfaceAddress, peerPublicKey, peerEndpoint, allowedIps,
                false, false, string.Empty, string.Empty
            );
        }

        if (string.IsNullOrWhiteSpace(peerEndpoint) || !peerEndpoint.Contains(':'))
        {
            return new WireGuardConfigAuditResult(
                false, "INVALID_PEER_ENDPOINT: O Endpoint do [Peer] deve conter Host:Porta válido.",
                interfaceAddress, peerPublicKey, peerEndpoint, allowedIps,
                false, false, string.Empty, string.Empty
            );
        }

        if (allowedIps.Count == 0 || !allowedIps.All(IsValidCidrBlock))
        {
            return new WireGuardConfigAuditResult(
                false, "INVALID_ALLOWED_IPS: A lista AllowedIPs deve conter blocos CIDR válidos.",
                interfaceAddress, peerPublicKey, peerEndpoint, allowedIps,
                false, false, string.Empty, string.Empty
            );
        }

        bool altersIpv4Default = allowedIps.Any(IsIpv4DefaultRouteCidr);
        bool altersIpv6Default = allowedIps.Any(IsIpv6DefaultRouteCidr);
        bool altersDefaultRoute = altersIpv4Default || altersIpv6Default;

        string affectedProtocols = (altersIpv4Default, altersIpv6Default) switch
        {
            (true, true) => "IPv4 (0.0.0.0/0) e IPv6 (::/0)",
            (true, false) => "IPv4 (0.0.0.0/0)",
            (false, true) => "IPv6 (::/0)",
            _ => "Nenhum"
        };

        if (altersDefaultRoute && !allowDefaultRouteOverride)
        {
            return new WireGuardConfigAuditResult(
                IsValid: false,
                ErrorReason: $"DEFAULT_ROUTE_OVERRIDE_BLOCKED: A configuração contém rota padrão ({affectedProtocols}) que desvia todo o tráfego do Windows. Para o Game Network Accelerator, utilize apenas os blocos CIDR dos servidores do jogo em AllowedIPs ou confirme explicitamente o modo de túnel global.",
                InterfaceAddress: interfaceAddress,
                PeerPublicKey: peerPublicKey,
                PeerEndpoint: peerEndpoint,
                AllowedIps: allowedIps,
                AltersDefaultRoute: true,
                IsSelectiveGameTargetRouting: false,
                RouteImpactExplanationPt: $"ATENÇÃO: Esta configuração altera a rota padrão do Windows ({affectedProtocols}) e desviará TODO o tráfego do PC pelo túnel.",
                RouteImpactExplanationEn: $"WARNING: This configuration alters the Windows default route ({affectedProtocols}) and will route ALL PC traffic through the tunnel."
            )
            {
                AltersIpv4DefaultRoute = altersIpv4Default,
                AltersIpv6DefaultRoute = altersIpv6Default
            };
        }

        bool isSelective = !altersDefaultRoute;
        string joinedCidrs = string.Join(", ", allowedIps);

        string impactPt = isSelective
            ? $"Roteamento Seletivo por Destino: Apenas pacotes destinados a [{joinedCidrs}] passarão pelo túnel ({peerEndpoint}). A rota padrão do Windows NÃO será alterada."
            : $"Modo Túnel Global Autorizado ({affectedProtocols}): Todo o tráfego do Windows ([{joinedCidrs}]) será encaminhado para {peerEndpoint}.";

        string impactEn = isSelective
            ? $"Selective Destination Routing: Only packets destined for [{joinedCidrs}] will traverse the tunnel ({peerEndpoint}). The Windows default route will NOT be altered."
            : $"Authorized Full-Tunnel Mode ({affectedProtocols}): All Windows traffic ([{joinedCidrs}]) will be routed through {peerEndpoint}.";

        return new WireGuardConfigAuditResult(
            IsValid: true,
            ErrorReason: string.Empty,
            InterfaceAddress: interfaceAddress,
            PeerPublicKey: peerPublicKey,
            PeerEndpoint: peerEndpoint,
            AllowedIps: allowedIps,
            AltersDefaultRoute: altersDefaultRoute,
            IsSelectiveGameTargetRouting: isSelective,
            RouteImpactExplanationPt: impactPt,
            RouteImpactExplanationEn: impactEn
        )
        {
            AltersIpv4DefaultRoute = altersIpv4Default,
            AltersIpv6DefaultRoute = altersIpv6Default
        };
    }

    /// <summary>
    /// Generates a strictly validated, selective-routing WireGuard configuration targeting only the specified game server CIDRs.
    /// </summary>
    public static string BuildSelectiveGameTunnelConfig(
        string clientPrivateKey,
        string clientTunnelAddressCidr,
        RelayNodeCandidate node,
        IEnumerable<string>? overrideGameCidrs = null)
    {
        var cidrs = (overrideGameCidrs ?? node.GameTargetCidrs)
            .Where(IsValidCidrBlock)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string allowedIpsLine = cidrs.Count > 0 ? string.Join(", ", cidrs) : "155.133.248.0/24";

        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {clientPrivateKey.Trim()}");
        sb.AppendLine($"Address = {clientTunnelAddressCidr.Trim()}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {node.PublicKey.Trim()}");
        sb.AppendLine($"Endpoint = {node.EndpointHost.Trim()}:{node.EndpointPort}");
        sb.AppendLine($"AllowedIPs = {allowedIpsLine}");
        sb.AppendLine("PersistentKeepalive = 25");
        return sb.ToString();
    }

    /// <summary>
    /// Queries the real status of a WireGuard tunnel service and inspects `wg.exe show <tunnel>`
    /// to verify whether a cryptographic handshake and bidirectional traffic actually occurred.
    /// Never reports a tunnel as connected merely because the Windows service exists.
    /// </summary>
    public static async Task<WireGuardTunnelStatusReport> QueryTunnelStatusAsync(
        string tunnelName = "PCControlGameTunnel",
        CancellationToken cancellationToken = default)
    {
        var (installed, wgExePath, wgCliPath) = CheckWireGuardInstallation();
        if (!installed)
        {
            return new WireGuardTunnelStatusReport(
                WireGuardInstalled: false,
                WireGuardBinaryPath: null,
                TunnelName: tunnelName,
                ServiceInstalled: false,
                ServiceRunning: false,
                HandshakeVerified: false,
                LastHandshakeEpochSeconds: null,
                BytesReceived: 0,
                BytesSent: 0,
                ActiveAllowedIps: Array.Empty<string>(),
                DiagnosticMessage: "WireGuard for Windows não está instalado em C:\\Program Files\\WireGuard\\wireguard.exe."
            );
        }

        if (string.IsNullOrWhiteSpace(tunnelName) || !SafeTunnelNameRegex.IsMatch(tunnelName))
        {
            return new WireGuardTunnelStatusReport(
                installed, wgExePath, tunnelName ?? "INVALID", false, false, false, null, 0, 0,
                Array.Empty<string>(), "Nome de túnel inválido."
            );
        }

        string serviceName = $"WireGuardTunnel${tunnelName}";
        var (svcInstalled, svcRunning) = await CheckWindowsServiceStateAsync(serviceName, cancellationToken);

        if (!svcRunning || string.IsNullOrEmpty(wgCliPath))
        {
            return new WireGuardTunnelStatusReport(
                WireGuardInstalled: true,
                WireGuardBinaryPath: wgExePath,
                TunnelName: tunnelName,
                ServiceInstalled: svcInstalled,
                ServiceRunning: svcRunning,
                HandshakeVerified: false,
                LastHandshakeEpochSeconds: null,
                BytesReceived: 0,
                BytesSent: 0,
                ActiveAllowedIps: Array.Empty<string>(),
                DiagnosticMessage: svcInstalled
                    ? "Serviço do túnel instalado, porém parado (sem túnel ativo)."
                    : "Nenhum túnel WireGuard ativo no momento."
            );
        }

        // Inspect real cryptographic handshake and transfer counters via `wg.exe show <tunnel> dump`
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = wgCliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add(tunnelName);
            psi.ArgumentList.Add("dump");

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string dumpOutput = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
                await proc.WaitForExitAsync(cancellationToken);

                var lines = dumpOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                // Line 0 is interface, Line 1+ are peers: public-key, preshared-key, endpoint, allowed-ips, latest-handshake, transfer-rx, transfer-tx, persistent-keepalive
                if (lines.Length >= 2)
                {
                    var peerCols = lines[1].Split('\t');
                    if (peerCols.Length >= 7)
                    {
                        var allowedIps = peerCols[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        long.TryParse(peerCols[4], out long latestHandshakeEpoch);
                        long.TryParse(peerCols[5], out long rxBytes);
                        long.TryParse(peerCols[6], out long txBytes);

                        bool handshakeOk = latestHandshakeEpoch > 0 && rxBytes > 0;
                        string msg = handshakeOk
                            ? $"Túnel conectado com Handshake verificado (RX: {rxBytes} B / TX: {txBytes} B)."
                            : "ATENÇÃO: Serviço WireGuard iniciado, mas AINDA SEM HANDSHAKE / TRÁFEGO do servidor remoto.";

                        return new WireGuardTunnelStatusReport(
                            WireGuardInstalled: true,
                            WireGuardBinaryPath: wgExePath,
                            TunnelName: tunnelName,
                            ServiceInstalled: true,
                            ServiceRunning: true,
                            HandshakeVerified: handshakeOk,
                            LastHandshakeEpochSeconds: latestHandshakeEpoch > 0 ? latestHandshakeEpoch : null,
                            BytesReceived: rxBytes,
                            BytesSent: txBytes,
                            ActiveAllowedIps: allowedIps,
                            DiagnosticMessage: msg
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return new WireGuardTunnelStatusReport(
                true, wgExePath, tunnelName, svcInstalled, svcRunning, false, null, 0, 0,
                Array.Empty<string>(), $"Serviço ativo, mas leitura de handshake requer privilégio UAC: {ex.Message}"
            );
        }

        return new WireGuardTunnelStatusReport(
            true, wgExePath, tunnelName, svcInstalled, svcRunning, false, null, 0, 0,
            Array.Empty<string>(), "Serviço em execução, aguardando handshake inicial."
        );
    }

    public static async Task<string> CaptureCurrentIPv4RouteTableSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "route",
                Arguments = "print -4",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
                await proc.WaitForExitAsync(cancellationToken);
                _capturedRouteTableBeforeTunnel = output;
                return output;
            }
        }
        catch
        {
            // Ignore if route command unavailable
        }
        return string.Empty;
    }

    public static string? GetLastCapturedRouteTableSnapshot() => _capturedRouteTableBeforeTunnel;

    /// <summary>
    /// Installs a validated WireGuard tunnel service ONLY when the user has explicitly consented.
    /// Captures the pre-tunnel IPv4 route table, handles UAC cancellation (Win32 error 1223) cleanly,
    /// and verifies handshake state after service startup.
    /// </summary>
    public static async Task<WireGuardServiceOperationResult> InstallTunnelServiceWithConsentAsync(
        string tunnelName,
        string configContent,
        bool userConfirmedExecution,
        bool allowDefaultRouteOverride = false,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmedExecution)
        {
            return new WireGuardServiceOperationResult(
                false,
                "USER_CONSENT_REQUIRED",
                "Operação bloqueada: a instalação do serviço de túnel WireGuard exige consentimento explícito do usuário."
            );
        }

        if (string.IsNullOrWhiteSpace(tunnelName) || !SafeTunnelNameRegex.IsMatch(tunnelName.Trim()))
        {
            return new WireGuardServiceOperationResult(
                false,
                "INVALID_TUNNEL_NAME",
                "Nome de túnel inválido. Use apenas letras, números, hífen ou sublinhado (3 a 32 caracteres)."
            );
        }

        var audit = ValidateAndAuditConfig(configContent, allowDefaultRouteOverride);
        if (!audit.IsValid)
        {
            return new WireGuardServiceOperationResult(
                false,
                "CONFIG_VALIDATION_FAILED",
                audit.ErrorReason
            );
        }

        var (installed, wgExePath, _) = CheckWireGuardInstallation();
        if (!installed || string.IsNullOrWhiteSpace(wgExePath))
        {
            return new WireGuardServiceOperationResult(
                false,
                "WIREGUARD_NOT_INSTALLED",
                "WireGuard for Windows não está instalado em C:\\Program Files\\WireGuard\\wireguard.exe."
            );
        }

        await CaptureCurrentIPv4RouteTableSnapshotAsync(cancellationToken);

        string safeName = tunnelName.Trim();
        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCControlUltimate",
            "Tunnels"
        );
        Directory.CreateDirectory(configDir);
        if (OperatingSystem.IsWindows())
        {
            PCControlUltimate.Security.IntegrityVerifier.TryRestrictDirectoryAclToCurrentUser(configDir, out _);
        }

        string configFilePath = Path.Combine(configDir, $"{safeName}.conf");
        await File.WriteAllTextAsync(configFilePath, configContent, Encoding.UTF8, cancellationToken);
        if (OperatingSystem.IsWindows())
        {
            if (!PCControlUltimate.Security.IntegrityVerifier.TryRestrictFileAclToCurrentUser(configFilePath, out string? aclErr))
            {
                SecureDeleteConfigFile(configFilePath);
                return new WireGuardServiceOperationResult(
                    false,
                    "CONFIG_ACL_PROTECTION_FAILED",
                    $"Operação bloqueada: não foi possível restringir a ACL NTFS do arquivo temporário de configuração ({aclErr})."
                );
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = wgExePath,
                Arguments = $"/installtunnelservice \"{configFilePath}\"",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return new WireGuardServiceOperationResult(
                    false,
                    "PROCESS_START_FAILED",
                    "Não foi possível iniciar o processo de instalação do túnel WireGuard."
                );
            }

            await proc.WaitForExitAsync(cancellationToken);
            if (proc.ExitCode != 0)
            {
                return new WireGuardServiceOperationResult(
                    false,
                    "TUNNEL_INSTALL_EXIT_ERROR",
                    $"O instalador do serviço WireGuard retornou código {proc.ExitCode}."
                );
            }

            return new WireGuardServiceOperationResult(
                true,
                string.Empty,
                $"Serviço do túnel '{safeName}' instalado sob autorização UAC e arquivo temporário de chave privada removido com segurança. Verifique o status de handshake antes de iniciar a partida."
            );
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new WireGuardServiceOperationResult(
                false,
                "UAC_CANCELLED_BY_USER",
                "Solicitação de elevação UAC cancelada pelo usuário. Nenhum túnel ou serviço foi instalado.",
                UacCancelled: true
            );
        }
        catch (Exception ex)
        {
            return new WireGuardServiceOperationResult(
                false,
                "TUNNEL_INSTALL_EXCEPTION",
                $"Erro ao instalar serviço WireGuard: {ex.Message}"
            );
        }
        finally
        {
            // Always purge the temporary plain-text .conf file containing the PrivateKey after service installation attempt
            SecureDeleteConfigFile(configFilePath);
        }
    }

    private static void SecureDeleteConfigFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length > 0 && info.Length <= 65536)
            {
                byte[] zeros = new byte[info.Length];
                File.WriteAllBytes(filePath, zeros);
            }
            File.Delete(filePath);
        }
        catch
        {
            try { File.Delete(filePath); } catch { }
        }
    }

    /// <summary>
    /// Removes an active WireGuard tunnel service with explicit user consent, handles UAC cancellation cleanly,
    /// and verifies that the Windows service and tunnel routes are actually torn down before reporting RouteTableRestored.
    /// </summary>
    public static async Task<WireGuardServiceOperationResult> UninstallTunnelServiceWithConsentAsync(
        string tunnelName,
        bool userConfirmedExecution,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmedExecution)
        {
            return new WireGuardServiceOperationResult(
                false,
                "USER_CONSENT_REQUIRED",
                "Operação bloqueada: a remoção do serviço WireGuard exige confirmação explícita do usuário."
            );
        }

        if (string.IsNullOrWhiteSpace(tunnelName) || !SafeTunnelNameRegex.IsMatch(tunnelName.Trim()))
        {
            return new WireGuardServiceOperationResult(
                false,
                "INVALID_TUNNEL_NAME",
                "Nome de túnel inválido."
            );
        }

        var (installed, wgExePath, _) = CheckWireGuardInstallation();
        if (!installed || string.IsNullOrWhiteSpace(wgExePath))
        {
            return new WireGuardServiceOperationResult(
                false,
                "WIREGUARD_NOT_INSTALLED",
                "Cliente WireGuard não encontrado no sistema."
            );
        }

        string safeName = tunnelName.Trim();
        string configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCControlUltimate",
            "Tunnels",
            $"{safeName}.conf"
        );

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = wgExePath,
                Arguments = $"/uninstalltunnelservice {safeName}",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return new WireGuardServiceOperationResult(
                    false,
                    "PROCESS_START_FAILED",
                    "Não foi possível iniciar a desinstalação do serviço WireGuard."
                );
            }

            await proc.WaitForExitAsync(cancellationToken);
            SecureDeleteConfigFile(configFilePath);

            if (proc.ExitCode != 0)
            {
                return new WireGuardServiceOperationResult(
                    false,
                    "TUNNEL_UNINSTALL_EXIT_ERROR",
                    $"Desinstalação do túnel retornou código {proc.ExitCode}.",
                    UacCancelled: false,
                    RouteTableRestored: false
                );
            }

            // Explicitly verify that the tunnel service is stopped/removed and inspect current IPv4 route table
            string serviceName = $"WireGuardTunnel${safeName}";
            var (svcStillInstalled, svcStillRunning) = await CheckWindowsServiceStateAsync(serviceName, cancellationToken);
            string postUninstallRoutes = await CaptureCurrentIPv4RouteTableSnapshotAsync(cancellationToken);
            bool routeTableVerified = !svcStillRunning &&
                                      !svcStillInstalled &&
                                      !string.IsNullOrWhiteSpace(postUninstallRoutes) &&
                                      !postUninstallRoutes.Contains(safeName, StringComparison.OrdinalIgnoreCase);

            return new WireGuardServiceOperationResult(
                Success: !svcStillRunning,
                ErrorCode: !svcStillRunning ? string.Empty : "SERVICE_STILL_RUNNING_AFTER_UNINSTALL",
                Message: routeTableVerified
                    ? $"Serviço do túnel '{safeName}' removido e restauração da tabela de rotas verificada no Windows."
                    : $"Comando de remoção do túnel '{safeName}' concluído (serviço em execução: {svcStillRunning}; verificação da tabela de rotas: {(routeTableVerified ? "confirmada" : "pendente/parcial")}).",
                UacCancelled: false,
                RouteTableRestored: routeTableVerified
            );
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new WireGuardServiceOperationResult(
                false,
                "UAC_CANCELLED_BY_USER",
                "Solicitação UAC cancelada pelo usuário. O estado atual do serviço foi mantido.",
                UacCancelled: true
            );
        }
        catch (Exception ex)
        {
            return new WireGuardServiceOperationResult(
                false,
                "TUNNEL_UNINSTALL_EXCEPTION",
                $"Erro ao remover serviço WireGuard: {ex.Message}"
            );
        }
    }

    private static async Task<(bool installed, bool running)> CheckWindowsServiceStateAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, false);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("query");
            psi.ArgumentList.Add(serviceName);

            using var proc = Process.Start(psi);
            if (proc == null) return (false, false);

            string output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);

            if (proc.ExitCode != 0 || output.Contains("1060", StringComparison.OrdinalIgnoreCase))
            {
                return (false, false);
            }

            bool isRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            return (true, isRunning);
        }
        catch
        {
            return (false, false);
        }
    }
}
