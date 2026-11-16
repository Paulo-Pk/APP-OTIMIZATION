using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Linq;
using Microsoft.Win32;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlUltimate.Gaming.Network;

public record InstalledGameEntry(
    string Title,
    string Launcher,
    string InstallLocation,
    string AppId,
    string? ExecutablePath,
    bool IsInstalled,
    string? CoverImageUrl = null
);

public record DetectedGameConnection(
    int ProcessId,
    string ProcessName,
    string GameTitle,
    string Protocol,
    string LocalEndpoint,
    string RemoteAddress,
    int RemotePort,
    string ConnectionState
)
{
    public string RemoteEndpoint => RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : RemoteAddress;
}

public record TracerouteHopResult(
    int HopNumber,
    string HopAddress,
    long? RttMs,
    string Status,
    bool IsFinalDestination
);

public record MultiSampleProbeSummary(
    string TargetLabel,
    string HostOrIp,
    int Port,
    string ProbeMethod,
    int SamplesAttempted,
    int SamplesSucceeded,
    double? AverageRttMs,
    double? MinRttMs,
    double? MaxRttMs,
    double? JitterMs,
    double PacketLossPercent,
    double StabilityScore,
    bool IsReliable,
    string TechnicalNote
);

public record RelayNodeCandidate(
    string NodeId,
    string DisplayName,
    string RegionCode,
    string EndpointHost,
    int EndpointPort,
    string PublicKey,
    IReadOnlyList<string> GameTargetCidrs,
    bool IsCustomVps
) : IAccelerationNode;

public record RouteComparisonEvaluation(
    string GameIdentifier,
    string TargetServerHost,
    int TargetServerPort,
    MultiSampleProbeSummary DirectRoute,
    string? BestRelayNodeId,
    string? BestRelayNodeName,
    MultiSampleProbeSummary? RelayIngressProbe,
    double? EstimatedTotalRelayRttMs,
    string RecommendedMode,
    bool ShouldUseRelay,
    string HonestVerdictPt,
    string HonestVerdictEn,
    string MeasuredAtUtc
);

public record SelectiveRoutingCapabilityReport(
    bool DestinationIpSelectiveRoutingSupported,
    bool KernelWfpProcessSplitTunnelEnabled,
    string ActiveMechanism,
    string AntiCheatSafetyGuarantee,
    string WfpLimitationExplanationPt,
    string WfpLimitationExplanationEn
);

public enum AcceleratorOperatingMode
{
    Automatico,
    Gaming,
    BaixaLatencia,
    Estabilidade,
    Personalizado
}

public record AcceleratorNetworkEnvironmentAnalysis(
    string ActiveInterfaceName,
    string InterfaceType,
    bool IsWiFi,
    long NegotiatedLinkSpeedMbps,
    long BytesReceived,
    long BytesSent,
    double? DnsResolutionTimeMs,
    bool LocalCongestionDetected,
    AcceleratorOperatingMode RecommendedMode,
    string ModeJustification
);

public static class GameNetworkAcceleratorService
{
    private static readonly Regex SafeHostOrIpRegex = new(
        @"^[A-Za-z0-9\.\-:]{1,253}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Dictionary<string, string> KnownGameCatalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2"] = "Counter-Strike 2 (Valve Source 2)",
        ["valorant-win64-shipping"] = "VALORANT (Riot Games)",
        ["league of legends"] = "League of Legends (Riot Games)",
        ["dota2"] = "Dota 2 (Valve)",
        ["r5apex"] = "Apex Legends (EA / Respawn)",
        ["overwatch"] = "Overwatch 2 (Blizzard)",
        ["fortniteclient-win64-shipping"] = "Fortnite (Epic Games)",
        ["cod"] = "Call of Duty HQ (Activision)",
        ["rainbowsix"] = "Rainbow Six Siege (Ubisoft)",
        ["pathofexile"] = "Path of Exile (Grinding Gear Games)",
        ["pubg-win64-shipping"] = "PUBG: Battlegrounds",
        ["rocketleague"] = "Rocket League"
    };

    private static readonly ConcurrentDictionary<string, RelayNodeCandidate> RegisteredRelayNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<RouteComparisonEvaluation> EvaluationHistory = new();

    public static IReadOnlyList<RelayNodeCandidate> GetRegisteredRelayNodes() =>
        RegisteredRelayNodes.Values.OrderBy(n => n.RegionCode).ThenBy(n => n.DisplayName).ToList();

    public static IReadOnlyList<RouteComparisonEvaluation> GetEvaluationHistory() =>
        EvaluationHistory.ToArray();

    public static SelectiveRoutingCapabilityReport GetSelectiveRoutingCapabilityReport() =>
        new(
            DestinationIpSelectiveRoutingSupported: true,
            KernelWfpProcessSplitTunnelEnabled: false,
            ActiveMechanism: "Policy-Based Destination CIDR Routing (WireGuard AllowedIPs per Game Server Subnet)",
            AntiCheatSafetyGuarantee: "Zero DLL injection, zero user-mode socket hooks, zero memory manipulation, zero unsigned kernel drivers (reduces anti-cheat conflict surface without promising absolute immunity from third-party policies).",
            WfpLimitationExplanationPt: "Roteamento Seletivo por Sub-rede de Destino (AllowedIPs) está ATIVO: apenas o tráfego destinado aos IPs dos servidores do jogo é encaminhado pelo túnel, preservando a rota padrão do Windows para os demais aplicativos. O redirecionamento exclusivo por PID/executável via Driver Kernel WFP (Windows Filtering Platform Callout Driver) permanece DESATIVADO nesta versão para evitar hooks invasivos e reduzir o risco de conflito com sistemas anti-cheat (Vanguard, Ricochet, EAC, BattlEye), sem prometer imunidade absoluta frente a políticas externas de terceiros.",
            WfpLimitationExplanationEn: "Destination-Subnet Selective Routing (AllowedIPs) is ACTIVE: only traffic addressed to the game server IP ranges is routed through the tunnel, preserving the Windows default route for all other apps. Per-process PID split-tunneling via a custom WFP Kernel Callout Driver remains DISABLED in this version to avoid invasive hooks and reduce conflict risk with anti-cheat systems (Vanguard, Ricochet, EAC, BattlEye), without promising absolute immunity from third-party policies."
        );

    public static (bool success, string errorReason, RelayNodeCandidate? node) RegisterOrUpdateRelayNode(
        string nodeId,
        string displayName,
        string regionCode,
        string endpointHost,
        int endpointPort,
        string publicKey,
        IEnumerable<string> gameTargetCidrs,
        bool isCustomVps = true)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !Regex.IsMatch(nodeId.Trim(), @"^[A-Za-z0-9_\-]{2,64}$"))
        {
            return (false, "INVALID_NODE_ID: Use apenas letras, números, hífen ou sublinhado (2 a 64 caracteres).", null);
        }

        if (!IsHostOrIpValid(endpointHost))
        {
            return (false, "INVALID_ENDPOINT_HOST: Host ou IP do nó intermediário é inválido.", null);
        }

        if (endpointPort is < 1 or > 65535)
        {
            return (false, "INVALID_ENDPOINT_PORT: A porta UDP do nó deve estar entre 1 e 65535.", null);
        }

        var cidrs = (gameTargetCidrs ?? Array.Empty<string>())
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        foreach (var cidr in cidrs)
        {
            if (!WireGuardTunnelManager.IsValidCidrBlock(cidr))
            {
                return (false, $"INVALID_CIDR_BLOCK: O bloco CIDR '{cidr}' não é uma sub-rede IPv4/IPv6 válida.", null);
            }
        }

        var candidate = new RelayNodeCandidate(
            NodeId: nodeId.Trim(),
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? nodeId.Trim() : displayName.Trim(),
            RegionCode: string.IsNullOrWhiteSpace(regionCode) ? "CUSTOM" : regionCode.Trim().ToUpperInvariant(),
            EndpointHost: endpointHost.Trim(),
            EndpointPort: endpointPort,
            PublicKey: publicKey?.Trim() ?? string.Empty,
            GameTargetCidrs: cidrs,
            IsCustomVps: isCustomVps
        );

        RegisteredRelayNodes[candidate.NodeId] = candidate;
        return (true, string.Empty, candidate);
    }

    public static bool RemoveRelayNode(string nodeId) =>
        !string.IsNullOrWhiteSpace(nodeId) && RegisteredRelayNodes.TryRemove(nodeId.Trim(), out _);

    public static void ClearNodesForTesting() => RegisteredRelayNodes.Clear();

    public static bool IsHostOrIpValid(string? hostOrIp)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp)) return false;
        string trimmed = hostOrIp.Trim();
        if (!SafeHostOrIpRegex.IsMatch(trimmed)) return false;
        if (trimmed.Contains("..") || trimmed.StartsWith('-') || trimmed.EndsWith('-')) return false;
        return true;
    }

    private static bool IsMainGameTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Exclude separately catalogued add-ons while keeping normal editions of base games.
        return !Regex.IsMatch(
            title,
            @"\b(dlc|season\s*pass|expansion(?:\s*pass|\s*pack)?|add[\s-]?on|content\s*pack|soundtrack|original\s*soundtrack|ost|supporter\s*pack|upgrade\s*pack|cosmetic\s*pack|bonus\s*content|artbook|map\s*pack|character\s*pack|mission\s*pack|story\s*pack|starter\s*pack|demo|playtest)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Discovers installed games from supported Steam and Epic manifests plus GOG, Ubisoft Connect, and EA registry catalogs. No full-disk scan.
    /// </summary>
    public static IReadOnlyList<InstalledGameEntry> DiscoverInstalledGames()
    {
        var games = new Dictionary<string, InstalledGameEntry>(StringComparer.OrdinalIgnoreCase);
        void Add(InstalledGameEntry game)
        {
            if (string.IsNullOrWhiteSpace(game.Title) || string.IsNullOrWhiteSpace(game.InstallLocation))
                return;

            // Launcher catalogs may include DLC apps, expansion packs, soundtracks,
            // dedicated servers and tools alongside the base games. Keep the library
            // focused on standalone playable titles.
            if (IsDlcOrNonGameCatalogEntry(game.Title))
                return;

            // Steam exposes official library artwork by AppID. Other launchers do not
            // provide a common public artwork URL in their local install catalogs.
            if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(game.AppId, @"^\d+$"))
            {
                game = game with
                {
                    CoverImageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.AppId}/library_600x900.jpg"
                };
            }

            games[game.Launcher + ":" + game.AppId] = game;
        }

        try
        {
            var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string defaultSteam = Path.Combine(programFilesX86, "Steam");
            if (Directory.Exists(defaultSteam)) steamRoots.Add(defaultSteam);

            // Steam may be installed outside Program Files (x86). Read its registered
            // install locations instead of scanning the entire disk.
            AddSteamRegistryRoot(steamRoots, RegistryHive.CurrentUser, RegistryView.Registry32,
                @"Software\Valve\Steam", "SteamPath");
            AddSteamRegistryRoot(steamRoots, RegistryHive.CurrentUser, RegistryView.Registry64,
                @"Software\Valve\Steam", "SteamPath");
            AddSteamRegistryRoot(steamRoots, RegistryHive.LocalMachine, RegistryView.Registry32,
                @"Software\Valve\Steam", "InstallPath");
            AddSteamRegistryRoot(steamRoots, RegistryHive.LocalMachine, RegistryView.Registry64,
                @"Software\Valve\Steam", "InstallPath");
            AddSteamRegistryRoot(steamRoots, RegistryHive.LocalMachine, RegistryView.Registry64,
                @"Software\WOW6432Node\Valve\Steam", "InstallPath");

            // Expand the complete set of Steam libraries before reading app manifests.
            foreach (string root in steamRoots.ToArray())
            {
                string libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFile)) continue;
                string vdf = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(vdf, @"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase))
                {
                    string library = match.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(library)) steamRoots.Add(Path.GetFullPath(library));
                }
            }

            // Scan manifests in the primary Steam folder and every discovered library.
            foreach (string root in steamRoots)
            {
                string apps = Path.Combine(root, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string data = File.ReadAllText(manifest);
                        string title = Regex.Match(data, @"""name""\s*""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                        string installDir = Regex.Match(data, @"""installdir""\s*""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                        string appId = Regex.Match(Path.GetFileName(manifest), @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase).Groups[1].Value;
                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(installDir) || string.IsNullOrWhiteSpace(appId)) continue;
                        string location = Path.GetFullPath(Path.Combine(apps, "common", installDir));
                        if (Directory.Exists(location))
                            Add(new InstalledGameEntry(title, "Steam", location, appId, null, true));
                    }
                    catch { /* Skip unreadable or malformed manifests. */ }
                }
            }
        }
        catch { /* Steam is optional. */ }

        try
        {
            string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifests))
            {
                foreach (string manifest in Directory.EnumerateFiles(manifests, "*.item", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
                        JsonElement root = doc.RootElement;
                        string title = ReadManifestString(root, "DisplayName");
                        string location = ReadManifestString(root, "InstallLocation");
                        string appId = ReadManifestString(root, "AppName");
                        string launchExe = ReadManifestString(root, "LaunchExecutable");
                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(location)) continue;
                        location = Path.GetFullPath(location);
                        if (!Directory.Exists(location)) continue;
                        string? executable = null;
                        if (!string.IsNullOrWhiteSpace(launchExe))
                        {
                            string candidate = Path.GetFullPath(Path.Combine(location, launchExe));
                            string prefix = location.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                                executable = candidate;
                        }
                        Add(new InstalledGameEntry(title, "Epic Games", location,
                            string.IsNullOrWhiteSpace(appId) ? Path.GetFileNameWithoutExtension(manifest) : appId,
                            executable, true));
                    }
                    catch { /* Skip unreadable or malformed manifests. */ }
                }
            }
        }
        catch { /* Epic Games is optional. */ }

        // Additional launchers expose installed-game locations through their own registry catalogs.
        // These are bounded lookups; no disk-wide executable scan is performed.
        AddRegistryGameCatalog(games, RegistryHive.CurrentUser, RegistryView.Registry32,
            @"Software\GOG.com\Games", "GOG", new[] { "path", "gamePath", "installDir" },
            new[] { "gameName", "name", "DisplayName" }, new[] { "exe", "gameExe" });
        AddRegistryGameCatalog(games, RegistryHive.CurrentUser, RegistryView.Registry64,
            @"Software\GOG.com\Games", "GOG", new[] { "path", "gamePath", "installDir" },
            new[] { "gameName", "name", "DisplayName" }, new[] { "exe", "gameExe" });
        AddRegistryGameCatalog(games, RegistryHive.LocalMachine, RegistryView.Registry32,
            @"Software\GOG.com\Games", "GOG", new[] { "path", "gamePath", "installDir" },
            new[] { "gameName", "name", "DisplayName" }, new[] { "exe", "gameExe" });
        AddRegistryGameCatalog(games, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"Software\GOG.com\Games", "GOG", new[] { "path", "gamePath", "installDir" },
            new[] { "gameName", "name", "DisplayName" }, new[] { "exe", "gameExe" });

        AddRegistryGameCatalog(games, RegistryHive.LocalMachine, RegistryView.Registry32,
            @"Software\Ubisoft\Launcher\Installs", "Ubisoft Connect",
            new[] { "InstallDir", "InstallLocation", "InstallPath" },
            new[] { "DisplayName", "GameName", "name" }, new[] { "Exe", "Executable" });
        AddRegistryGameCatalog(games, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"Software\Ubisoft\Launcher\Installs", "Ubisoft Connect",
            new[] { "InstallDir", "InstallLocation", "InstallPath" },
            new[] { "DisplayName", "GameName", "name" }, new[] { "Exe", "Executable" });

        AddRegistryGameCatalog(games, RegistryHive.LocalMachine, RegistryView.Registry32,
            @"Software\EA Games", "EA app",
            new[] { "Install Dir", "InstallDir", "InstallLocation" },
            new[] { "DisplayName", "GameName", "ProductName" }, new[] { "Executable", "Exe" });
        AddRegistryGameCatalog(games, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"Software\EA Games", "EA app",
            new[] { "Install Dir", "InstallDir", "InstallLocation" },
            new[] { "DisplayName", "GameName", "ProductName" }, new[] { "Executable", "Exe" });

        // Xbox / Microsoft Store GDK games are registered as Windows app packages.
        // Enumerate only the current user's packages and accept packages carrying the
        // official MicrosoftGame.config game manifest (not every Store app).
        AddMicrosoftStoreGames(games);
        AddXboxGamesFolderGames(games);

        return games.Values
            .Where(game => !IsDlcOrNonGameCatalogEntry(game.Title))
            .Select(game =>
                string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(game.AppId, @"^\d+$") &&
                string.IsNullOrWhiteSpace(game.CoverImageUrl)
                    ? game with { CoverImageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.AppId}/library_600x900.jpg" }
                    : game)
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsDlcOrNonGameCatalogEntry(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        return Regex.IsMatch(
            title,
            @"\b(DLCs?|Expansions?(?:\s+Pack)?|Season\s+Pass|Add[- ]?ons?|Soundtracks?|Original\s+Soundtrack|Dedicated\s+Server|Server\s+Tools|Workshop\s+Tools|Test\s+Server|Public\s+Test|Test\s+Application|SDK|Editor|Mod\s+Tools|Redistributables?|Runtimes?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void AddXboxGamesFolderGames(Dictionary<string, InstalledGameEntry> games)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // GDK flat-file installs use [drive]:\\XboxGames by default. Keep this
            // lookup bounded to that known folder and its immediate game directories.
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    string xboxGamesRoot = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                    foreach (InstalledGameEntry game in DiscoverXboxGamesFolder(xboxGamesRoot))
                        games["Xbox / Microsoft Store:" + game.AppId] = game;
                }
                catch
                {
                    // An unavailable or protected drive must not interrupt other catalogs.
                }
            }
        }
        catch
        {
            // Xbox folder discovery is optional; other launcher catalogs remain available.
        }
    }

    internal static IReadOnlyList<InstalledGameEntry> DiscoverXboxGamesFolder(string xboxGamesRoot)
    {
        var discovered = new List<InstalledGameEntry>();
        try
        {
            if (string.IsNullOrWhiteSpace(xboxGamesRoot) || !Directory.Exists(xboxGamesRoot))
                return discovered;

            // No recursive or whole-disk scan: inspect only immediate game folders.
            foreach (string gameDirectory in Directory.EnumerateDirectories(
                xboxGamesRoot, "*", SearchOption.TopDirectoryOnly))
            {
                string packageName = Path.GetFileName(gameDirectory);
                if (TryReadMicrosoftGameConfig(gameDirectory, packageName, out InstalledGameEntry? game)
                    && game is not null)
                {
                    discovered.Add(game);
                }
            }
        }
        catch
        {
            // Skip inaccessible folders and malformed game installations individually.
        }

        return discovered;
    }

    private static void AddMicrosoftStoreGames(Dictionary<string, InstalledGameEntry> games)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            const string script =
                "Get-AppxPackage | Where-Object { -not [string]::IsNullOrWhiteSpace($_.InstallLocation) } " +
                "| Select-Object @{Name='Name';Expression={$_.Name}}, @{Name='InstallLocation';Expression={$_.InstallLocation}} " +
                "| ConvertTo-Json -Compress";
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);

            using Process? process = Process.Start(startInfo);
            if (process is null) return;

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { process.WaitForExit(5000); } catch { }
                // Observe both redirected-stream tasks after terminating a timed-out
                // PowerShell process so no background reads are left unobserved.
                try { Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult(); } catch { }
                return;
            }

            string output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return;

            using JsonDocument document = JsonDocument.Parse(output);
            IEnumerable<JsonElement> packages = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray()
                : document.RootElement.ValueKind == JsonValueKind.Object
                    ? new[] { document.RootElement }
                    : Array.Empty<JsonElement>();

            foreach (JsonElement package in packages)
            {
                string packageName = ReadManifestString(package, "Name");
                string installLocation = ReadManifestString(package, "InstallLocation");
                if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(installLocation))
                    continue;

                if (TryReadMicrosoftGameConfig(installLocation, packageName, out InstalledGameEntry? game)
                    && game is not null)
                {
                    games["Xbox / Microsoft Store:" + game.AppId] = game;
                }
            }
        }
        catch
        {
            // Package enumeration is optional; other launcher catalogs remain available.
        }
    }

    internal static bool TryReadMicrosoftGameConfig(
        string installLocation,
        string packageName,
        out InstalledGameEntry? game)
    {
        game = null;
        try
        {
            if (string.IsNullOrWhiteSpace(installLocation) || string.IsNullOrWhiteSpace(packageName))
                return false;

            string location = Path.GetFullPath(installLocation);
            if (!Directory.Exists(location)) return false;

            string configPath = Path.Combine(location, "MicrosoftGame.config");
            if (!File.Exists(configPath)) return false;

            XDocument config = XDocument.Load(configPath);
            XElement? identity = config.Descendants().FirstOrDefault(element => element.Name.LocalName == "Identity");
            XElement? shellVisuals = config.Descendants().FirstOrDefault(element => element.Name.LocalName == "ShellVisuals");
            string appId = identity?.Attribute("Name")?.Value?.Trim() ?? packageName;
            string title = shellVisuals?.Attribute("DefaultDisplayName")?.Value?.Trim() ?? packageName;
            if (string.IsNullOrWhiteSpace(appId)) appId = packageName;
            if (string.IsNullOrWhiteSpace(title)) title = packageName;

            string? executable = null;
            XElement? executableElement = config.Descendants().FirstOrDefault(element => element.Name.LocalName == "Executable");
            string? executableName = executableElement?.Attribute("Name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(executableName))
            {
                string candidate = Path.GetFullPath(Path.Combine(location, executableName));
                string prefix = location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                    executable = candidate;
            }

            game = new InstalledGameEntry(title, "Xbox / Microsoft Store", location, appId, executable, true);
            return true;
        }
        catch
        {
            // Invalid or inaccessible package manifests are skipped individually.
            return false;
        }
    }

    private static void AddRegistryGameCatalog(
        Dictionary<string, InstalledGameEntry> games,
        RegistryHive hive,
        RegistryView view,
        string catalogPath,
        string launcher,
        IReadOnlyList<string> locationValueNames,
        IReadOnlyList<string> titleValueNames,
        IReadOnlyList<string> executableValueNames)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? catalog = baseKey.OpenSubKey(catalogPath);
            if (catalog is null) return;

            foreach (string subKeyName in catalog.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? entry = catalog.OpenSubKey(subKeyName);
                    if (entry is null) continue;

                    string ReadFirst(IReadOnlyList<string> names)
                    {
                        foreach (string name in names)
                        {
                            string? value = entry.GetValue(name) as string;
                            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                        }
                        return string.Empty;
                    }

                    string rawLocation = ReadFirst(locationValueNames);
                    if (string.IsNullOrWhiteSpace(rawLocation)) continue;
                    string location = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawLocation));
                    if (!Directory.Exists(location)) continue;

                    string title = ReadFirst(titleValueNames);
                    if (string.IsNullOrWhiteSpace(title)) title = subKeyName;
                    string rawExecutable = ReadFirst(executableValueNames);
                    string? executable = null;
                    if (!string.IsNullOrWhiteSpace(rawExecutable))
                    {
                        string candidate = Path.IsPathRooted(rawExecutable)
                            ? Path.GetFullPath(rawExecutable)
                            : Path.GetFullPath(Path.Combine(location, rawExecutable));
                        string prefix = location.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                            executable = candidate;
                    }

                    string key = launcher + ":" + subKeyName;
                    games[key] = new InstalledGameEntry(title, launcher, location, subKeyName, executable, true);
                }
                catch
                {
                    // One malformed launcher entry must not stop discovery of other games.
                }
            }
        }
        catch
        {
            // Launcher registry catalogs are optional and may be absent or inaccessible.
        }
    }

    private static void AddSteamRegistryRoot(
        HashSet<string> roots,
        RegistryHive hive,
        RegistryView view,
        string subKeyPath,
        string valueName)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? steamKey = baseKey.OpenSubKey(subKeyPath);
            string? registeredPath = steamKey?.GetValue(valueName) as string;
            if (string.IsNullOrWhiteSpace(registeredPath)) return;

            string normalizedPath = Environment.ExpandEnvironmentVariables(registeredPath.Trim());
            if (Directory.Exists(normalizedPath))
                roots.Add(Path.GetFullPath(normalizedPath));
        }
        catch
        {
            // Registry access is optional; continue with other known Steam locations.
        }
    }

    private static string ReadManifestString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Detects currently running game processes and maps their active external TCP/UDP socket endpoints
    /// without injecting into game processes or reading process memory.
    /// </summary>
    public static IReadOnlyList<DetectedGameConnection> DetectRunningGamesAndConnections()
    {
        var results = new List<DetectedGameConnection>();
        var runningPidMap = new Dictionary<int, (string ProcessName, string Title)>();

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id <= 4 || string.IsNullOrWhiteSpace(proc.ProcessName))
                        continue;

                    if (KnownGameCatalog.TryGetValue(proc.ProcessName, out string? gameTitle))
                    {
                        runningPidMap[proc.Id] = (proc.ProcessName, gameTitle);
                    }
                }
                catch
                {
                    // Process exited or access restricted
                }
                finally
                {
                    proc.Dispose();
                }
            }

            if (runningPidMap.Count == 0)
            {
                return results;
            }

            // Parse active OS socket table via netstat -ano without touching the game process
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.SystemDirectory))
                return results;

            var psi = new ProcessStartInfo
            {
                // Use the Windows system binary explicitly; never resolve netstat through
                // the current directory or a potentially modified PATH.
                FileName = Path.Combine(Environment.SystemDirectory, "netstat.exe"),
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var netstatProc = Process.Start(psi);
            if (netstatProc != null)
            {
                // Drain stdout asynchronously while enforcing a hard process lifetime.
                // ReadToEnd() before a timeout can otherwise wait forever if netstat stalls.
                Task<string> outputTask = netstatProc.StandardOutput.ReadToEndAsync();
                if (!netstatProc.WaitForExit(2000))
                {
                    try
                    {
                        if (!netstatProc.HasExited)
                            netstatProc.Kill(entireProcessTree: true);
                    }
                    catch { }

                    // Do not wait on a redirected stream if the process could not be
                    // terminated; a failed kill must not turn this timeout into a hang.
                    try
                    {
                        if (!netstatProc.WaitForExit(1000))
                            return results;
                    }
                    catch
                    {
                        return results;
                    }
                }

                string output;
                try
                {
                    // The process has exited, so its redirected stdout should be closed.
                    // Keep a final bound in case an unexpected handle owner keeps the pipe open.
                    output = outputTask.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                }
                catch
                {
                    return results;
                }
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    string proto = parts[0].ToUpperInvariant();
                    if (proto != "TCP" && proto != "UDP") continue;

                    string pidStr = parts[^1];
                    if (!int.TryParse(pidStr, out int pid) || !runningPidMap.TryGetValue(pid, out var gameInfo))
                        continue;

                    string localEp = parts[1];
                    string remoteEp = parts[2];
                    string state = proto == "TCP" && parts.Length >= 5 ? parts[3] : "ACTIVE_SOCKET";

                    if (remoteEp.StartsWith("127.") || remoteEp.StartsWith("0.0.0.0") || remoteEp.StartsWith("[::]") || remoteEp == "*:*")
                        continue;

                    int lastColon = remoteEp.LastIndexOf(':');
                    if (lastColon <= 0) continue;

                    string remoteIp = remoteEp[..lastColon];
                    int.TryParse(remoteEp[(lastColon + 1)..], out int remotePort);

                    results.Add(new DetectedGameConnection(
                        ProcessId: pid,
                        ProcessName: gameInfo.ProcessName,
                        GameTitle: gameInfo.Title,
                        Protocol: proto,
                        LocalEndpoint: localEp,
                        RemoteAddress: remoteIp,
                        RemotePort: remotePort,
                        ConnectionState: state
                    ));
                }
            }

            // If a known game process is running but has not yet opened a match socket, include the process entry
            foreach (var kvp in runningPidMap)
            {
                if (results.All(r => r.ProcessId != kvp.Key))
                {
                    results.Add(new DetectedGameConnection(
                        ProcessId: kvp.Key,
                        ProcessName: kvp.Value.ProcessName,
                        GameTitle: kvp.Value.Title,
                        Protocol: "NONE",
                        LocalEndpoint: "Lobby / Menu",
                        RemoteAddress: "N/D (Sem partida ativa)",
                        RemotePort: 0,
                        ConnectionState: "PROCESS_RUNNING_IDLE"
                    ));
                }
            }
        }
        catch
        {
            // Safe fallback if process enumeration is restricted
        }

        return results;
    }

    /// <summary>
    /// Computes multi-sample statistical stability metrics from raw RTT samples and total probes attempted.
    /// Strictly requires multiple samples to evaluate jitter and penalizes packet loss and variance.
    /// </summary>
    public static MultiSampleProbeSummary ComputeStatisticalProbeSummary(
        string targetLabel,
        string hostOrIp,
        int port,
        string probeMethod,
        int samplesAttempted,
        IReadOnlyList<double> validRttSamplesMs)
    {
        int attempted = Math.Max(1, samplesAttempted);
        int succeeded = validRttSamplesMs?.Count ?? 0;
        int failed = Math.Max(0, attempted - succeeded);
        double lossPercent = Math.Round((double)failed / attempted * 100.0, 1);

        if (succeeded == 0 || validRttSamplesMs == null)
        {
            return new MultiSampleProbeSummary(
                TargetLabel: targetLabel,
                HostOrIp: hostOrIp,
                Port: port,
                ProbeMethod: probeMethod,
                SamplesAttempted: attempted,
                SamplesSucceeded: 0,
                AverageRttMs: null,
                MinRttMs: null,
                MaxRttMs: null,
                JitterMs: null,
                PacketLossPercent: 100.0,
                StabilityScore: 0.0,
                IsReliable: false,
                TechnicalNote: $"Sem resposta em {attempted} amostras ({probeMethod}). Não confunda bloqueio de firewall/ICMP com perda UDP dentro da partida."
            );
        }

        double avg = Math.Round(validRttSamplesMs.Average(), 2);
        double min = Math.Round(validRttSamplesMs.Min(), 2);
        double max = Math.Round(validRttSamplesMs.Max(), 2);

        double jitter = 0.0;
        if (succeeded > 1)
        {
            double mean = validRttSamplesMs.Average();
            double variance = validRttSamplesMs.Select(x => Math.Pow(x - mean, 2)).Average();
            jitter = Math.Round(Math.Sqrt(variance), 2);
        }

        // Lower effective cost = better route; map to 0..100 StabilityScore
        // Effective Penalty = AverageRTT + (Jitter * 2.5) + (LossPercent * 4.0)
        double effectivePenalty = avg + (jitter * 2.5) + (lossPercent * 4.0);
        double stabilityScore = Math.Round(Math.Clamp(100.0 - (effectivePenalty / 3.5), 0.0, 100.0), 1);
        bool isReliable = succeeded >= 3 && lossPercent <= 20.0 && jitter <= 25.0;

        string note = probeMethod.Contains("TCP", StringComparison.OrdinalIgnoreCase)
            ? $"Medição via Handshake TCP ({succeeded}/{attempted} amostras). Nota: Handshake TCP na borda não equivale a ping UDP de partida."
            : $"Medição via ICMP Echo ({succeeded}/{attempted} amostras). Média: {avg:F1} ms | Jitter (σ): {jitter:F2} ms | Perda ICMP: {lossPercent:F1}%.";

        return new MultiSampleProbeSummary(
            TargetLabel: targetLabel,
            HostOrIp: hostOrIp,
            Port: port,
            ProbeMethod: probeMethod,
            SamplesAttempted: attempted,
            SamplesSucceeded: succeeded,
            AverageRttMs: avg,
            MinRttMs: min,
            MaxRttMs: max,
            JitterMs: jitter,
            PacketLossPercent: lossPercent,
            StabilityScore: stabilityScore,
            IsReliable: isReliable,
            TechnicalNote: note
        );
    }

    /// <summary>
    /// Executes multi-sample ICMP Echo probes (with optional TCP Handshake fallback) against a target host.
    /// Never relies on a single ping measurement.
    /// </summary>
    public static async Task<MultiSampleProbeSummary> MeasureMultiSampleEndpointAsync(
        string targetLabel,
        string hostOrIp,
        int port = 443,
        int sampleCount = 5,
        bool preferIcmp = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsHostOrIpValid(hostOrIp))
        {
            return ComputeStatisticalProbeSummary(targetLabel, hostOrIp ?? "INVALID", port, "INVALID_HOST", sampleCount, Array.Empty<double>());
        }

        int count = Math.Clamp(sampleCount, 3, 15);
        var samples = new List<double>();
        string methodUsed = preferIcmp ? "ICMP Echo Multi-Amostra" : $"TCP Handshake Porta {port}";

        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (preferIcmp)
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(hostOrIp, 1200);
                    if (reply.Status == IPStatus.Success)
                    {
                        samples.Add(reply.RoundtripTime);
                    }
                }
                catch
                {
                    // ICMP blocked or unavailable
                }
            }
            else
            {
                try
                {
                    using var tcp = new TcpClient();
                    var sw = Stopwatch.StartNew();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(1500);
                    await tcp.ConnectAsync(hostOrIp, port, cts.Token);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    // TCP timeout or refused
                }
            }

            if (i < count - 1)
            {
                await Task.Delay(60, cancellationToken);
            }
        }

        // If ICMP was blocked by edge firewall (0 responses) and port > 0, perform explicit TCP Handshake measurement labeled as TCP
        if (preferIcmp && samples.Count == 0 && port > 0)
        {
            methodUsed = $"TCP Handshake Porta {port} (ICMP bloqueado na borda)";
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var tcp = new TcpClient();
                    var sw = Stopwatch.StartNew();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(1500);
                    await tcp.ConnectAsync(hostOrIp, port, cts.Token);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    // TCP failed
                }
            }
        }

        return ComputeStatisticalProbeSummary(targetLabel, hostOrIp, port, methodUsed, count, samples);
    }

    /// <summary>
    /// Executes a real hop-by-hop ICMP TTL Traceroute up to maxHops.
    /// </summary>
    public static async Task<IReadOnlyList<TracerouteHopResult>> RunTracerouteAsync(
        string hostOrIp,
        int maxHops = 15,
        int timeoutMs = 900,
        CancellationToken cancellationToken = default)
    {
        var hops = new List<TracerouteHopResult>();
        if (!IsHostOrIpValid(hostOrIp))
        {
            return hops;
        }

        int cappedHops = Math.Clamp(maxHops, 1, 30);
        byte[] buffer = new byte[32];

        for (int ttl = 1; ttl <= cappedHops; ttl++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var ping = new Ping();
                var options = new PingOptions(ttl, true);
                var sw = Stopwatch.StartNew();
                var reply = await ping.SendPingAsync(hostOrIp, timeoutMs, buffer, options);
                sw.Stop();

                if (reply.Status == IPStatus.Success)
                {
                    hops.Add(new TracerouteHopResult(
                        HopNumber: ttl,
                        HopAddress: reply.Address?.ToString() ?? hostOrIp,
                        RttMs: Math.Max(1, sw.ElapsedMilliseconds),
                        Status: "Destino Alcançado (ICMP EchoReply)",
                        IsFinalDestination: true
                    ));
                    break;
                }

                if (reply.Status == IPStatus.TtlExpired)
                {
                    hops.Add(new TracerouteHopResult(
                        HopNumber: ttl,
                        HopAddress: reply.Address?.ToString() ?? "*",
                        RttMs: Math.Max(1, sw.ElapsedMilliseconds),
                        Status: "Salto Intermediário (TTL Expirado)",
                        IsFinalDestination: false
                    ));
                }
                else
                {
                    hops.Add(new TracerouteHopResult(
                        HopNumber: ttl,
                        HopAddress: "*",
                        RttMs: null,
                        Status: $"Sem Resposta ICMP ({reply.Status})",
                        IsFinalDestination: false
                    ));
                }
            }
            catch (Exception ex)
            {
                hops.Add(new TracerouteHopResult(
                    HopNumber: ttl,
                    HopAddress: "*",
                    RttMs: null,
                    Status: $"Indisponível ({ex.GetType().Name})",
                    IsFinalDestination: false
                ));
            }
        }

        return hops;
    }

    /// <summary>
    /// Honest Route Comparator: Compares the measured Direct Route against a Relay Node candidate.
    /// Never promises artificial ping reduction; recommends keeping the Direct Route whenever the relay
    /// fails, adds latency, increases jitter, or does not achieve a statistically meaningful improvement.
    /// </summary>
    public static RouteComparisonEvaluation EvaluateHonestRouteComparison(
        string gameIdentifier,
        string targetServerHost,
        int targetServerPort,
        MultiSampleProbeSummary directRoute,
        RelayNodeCandidate? candidateNode,
        MultiSampleProbeSummary? relayIngressProbe,
        double? estimatedRelayToGameLegMs = null)
    {
        string nowIso = DateTime.UtcNow.ToString("O");

        if (candidateNode == null || relayIngressProbe == null || !relayIngressProbe.AverageRttMs.HasValue)
        {
            return new RouteComparisonEvaluation(
                GameIdentifier: gameIdentifier,
                TargetServerHost: targetServerHost,
                TargetServerPort: targetServerPort,
                DirectRoute: directRoute,
                BestRelayNodeId: candidateNode?.NodeId,
                BestRelayNodeName: candidateNode?.DisplayName,
                RelayIngressProbe: relayIngressProbe,
                EstimatedTotalRelayRttMs: null,
                RecommendedMode: "Direct",
                ShouldUseRelay: false,
                HonestVerdictPt: "Recomendação Honesta: MANTER ROTA DIRETA. Nenhum nó intermediário ativo/acessível superou a conexão direta.",
                HonestVerdictEn: "Honest Recommendation: KEEP DIRECT ROUTE. No active/reachable intermediate relay node outperformed your direct connection.",
                MeasuredAtUtc: nowIso
            );
        }

        if (!directRoute.IsReliable || !directRoute.AverageRttMs.HasValue)
        {
            var unreliableDirect = new RouteComparisonEvaluation(
                GameIdentifier: gameIdentifier,
                TargetServerHost: targetServerHost,
                TargetServerPort: targetServerPort,
                DirectRoute: directRoute,
                BestRelayNodeId: candidateNode.NodeId,
                BestRelayNodeName: candidateNode.DisplayName,
                RelayIngressProbe: relayIngressProbe,
                EstimatedTotalRelayRttMs: null,
                RecommendedMode: "Direct",
                ShouldUseRelay: false,
                HonestVerdictPt: "MANTER ROTA DIRETA / NOVA MEDIÇÃO NECESSÁRIA: a rota direta não possui amostras confiáveis para servir de referência. Não é seguro afirmar que o relay melhora a conexão.",
                HonestVerdictEn: "KEEP DIRECT / MEASUREMENT REQUIRED: the direct route has no reliable baseline samples. A relay improvement cannot be established safely.",
                MeasuredAtUtc: nowIso
            );
            EnqueueEvaluation(unreliableDirect);
            return unreliableDirect;
        }

        if (!estimatedRelayToGameLegMs.HasValue ||
            !double.IsFinite(estimatedRelayToGameLegMs.Value) ||
            estimatedRelayToGameLegMs.Value < 0)
        {
            var unmeasuredRelayLeg = new RouteComparisonEvaluation(
                GameIdentifier: gameIdentifier,
                TargetServerHost: targetServerHost,
                TargetServerPort: targetServerPort,
                DirectRoute: directRoute,
                BestRelayNodeId: candidateNode.NodeId,
                BestRelayNodeName: candidateNode.DisplayName,
                RelayIngressProbe: relayIngressProbe,
                EstimatedTotalRelayRttMs: null,
                RecommendedMode: "Direct",
                ShouldUseRelay: false,
                HonestVerdictPt: "MANTER ROTA DIRETA: foi medida apenas a latência entre o PC e o relay. A latência do relay até o servidor do jogo não foi medida; sem esse segundo trecho, não é possível calcular nem recomendar a rota completa.",
                HonestVerdictEn: "KEEP DIRECT: only the PC-to-relay leg was measured. The relay-to-game-server leg is unknown, so the complete route cannot be calculated or recommended.",
                MeasuredAtUtc: nowIso
            );
            EnqueueEvaluation(unmeasuredRelayLeg);
            return unmeasuredRelayLeg;
        }

        // Total RTT is only evaluated when the relay-to-game-server leg was supplied as a measured estimate.
        double relayTotalRtt = Math.Round(relayIngressProbe.AverageRttMs.Value + estimatedRelayToGameLegMs.Value, 2);
        double directRtt = directRoute.AverageRttMs ?? 9999.0;
        double directJitter = directRoute.JitterMs ?? 999.0;
        double relayJitter = relayIngressProbe.JitterMs ?? 999.0;

        bool relayHasHigherLoss = relayIngressProbe.PacketLossPercent > directRoute.PacketLossPercent;
        double rttImprovementMs = directRtt - relayTotalRtt;
        double jitterImprovementMs = directJitter - relayJitter;

        // Require genuine improvement: at least 3.0 ms lower RTT (with no worse packet loss) OR >= 5.0 ms lower jitter when direct route is unstable
        bool genuineBenefit = !relayHasHigherLoss &&
                              relayIngressProbe.IsReliable &&
                              ((rttImprovementMs >= 3.0 && relayTotalRtt < directRtt * 0.95) ||
                               (directJitter > 8.0 && jitterImprovementMs >= 4.0 && relayTotalRtt <= directRtt + 2.0));

        if (!genuineBenefit)
        {
            string reasonPt = relayTotalRtt >= directRtt
                ? $"Recomendação Honesta: MANTER ROTA DIRETA ({directRtt:F1} ms vs {relayTotalRtt:F1} ms no nó '{candidateNode.DisplayName}'). A rota alternativa não reduz a latência."
                : $"Recomendação Honesta: MANTER ROTA DIRETA. A diferença medida ({rttImprovementMs:F1} ms) está dentro da margem de variação estatística ou apresenta maior jitter/perda.";

            string reasonEn = relayTotalRtt >= directRtt
                ? $"Honest Recommendation: KEEP DIRECT ROUTE ({directRtt:F1} ms vs {relayTotalRtt:F1} ms via node '{candidateNode.DisplayName}'). The alternative route does not reduce latency."
                : $"Honest Recommendation: KEEP DIRECT ROUTE. The measured delta ({rttImprovementMs:F1} ms) is within normal statistical variance or introduces higher jitter/loss.";

            var directEval = new RouteComparisonEvaluation(
                GameIdentifier: gameIdentifier,
                TargetServerHost: targetServerHost,
                TargetServerPort: targetServerPort,
                DirectRoute: directRoute,
                BestRelayNodeId: candidateNode.NodeId,
                BestRelayNodeName: candidateNode.DisplayName,
                RelayIngressProbe: relayIngressProbe,
                EstimatedTotalRelayRttMs: relayTotalRtt,
                RecommendedMode: "Direct",
                ShouldUseRelay: false,
                HonestVerdictPt: reasonPt,
                HonestVerdictEn: reasonEn,
                MeasuredAtUtc: nowIso
            );
            EnqueueEvaluation(directEval);
            return directEval;
        }

        var relayEval = new RouteComparisonEvaluation(
            GameIdentifier: gameIdentifier,
            TargetServerHost: targetServerHost,
            TargetServerPort: targetServerPort,
            DirectRoute: directRoute,
            BestRelayNodeId: candidateNode.NodeId,
            BestRelayNodeName: candidateNode.DisplayName,
            RelayIngressProbe: relayIngressProbe,
            EstimatedTotalRelayRttMs: relayTotalRtt,
            RecommendedMode: $"Relay:{candidateNode.NodeId}",
            ShouldUseRelay: true,
            HonestVerdictPt: $"Rota Alternativa Recomendada ('{candidateNode.DisplayName}'): Redução medida de {rttImprovementMs:F1} ms (Direta: {directRtt:F1} ms / σ={directJitter:F1} ms -> Túnel: {relayTotalRtt:F1} ms / σ={relayJitter:F1} ms).",
            HonestVerdictEn: $"Alternative Route Recommended ('{candidateNode.DisplayName}'): Measured reduction of {rttImprovementMs:F1} ms (Direct: {directRtt:F1} ms / σ={directJitter:F1} ms -> Tunnel: {relayTotalRtt:F1} ms / σ={relayJitter:F1} ms).",
            MeasuredAtUtc: nowIso
        );
        EnqueueEvaluation(relayEval);
        return relayEval;
    }

    public static async Task<RouteComparisonEvaluation> AnalyzeGameRouteAsync(
        string gameIdentifier,
        string targetServerHost,
        int targetServerPort = 443,
        int sampleCount = 5,
        CancellationToken cancellationToken = default)
    {
        var directSummary = await MeasureMultiSampleEndpointAsync(
            $"Rota Direta ({gameIdentifier})",
            targetServerHost,
            targetServerPort,
            sampleCount,
            preferIcmp: true,
            cancellationToken
        );

        var nodes = GetRegisteredRelayNodes();
        if (nodes.Count == 0)
        {
            var evalNoNodes = EvaluateHonestRouteComparison(
                gameIdentifier,
                targetServerHost,
                targetServerPort,
                directSummary,
                null,
                null
            );
            return evalNoNodes;
        }

        RelayNodeCandidate? bestNode = null;
        MultiSampleProbeSummary? bestProbe = null;

        foreach (var node in nodes)
        {
            var probe = await MeasureMultiSampleEndpointAsync(
                $"Nó {node.DisplayName} ({node.RegionCode})",
                node.EndpointHost,
                node.EndpointPort,
                sampleCount,
                preferIcmp: true,
                cancellationToken
            );

            if (bestProbe == null || probe.StabilityScore > bestProbe.StabilityScore)
            {
                bestNode = node;
                bestProbe = probe;
            }
        }

        return EvaluateHonestRouteComparison(
            gameIdentifier,
            targetServerHost,
            targetServerPort,
            directSummary,
            bestNode,
            bestProbe
        );
    }

    public static async Task<AcceleratorNetworkEnvironmentAnalysis> AnalyzeLocalNetworkEnvironmentAsync(
        double? currentJitterMs = null,
        double? currentPacketLossPercent = null,
        CancellationToken cancellationToken = default)
    {
        string nicName = "Não detectado";
        string nicType = "Indisponível";
        bool isWifi = false;
        bool interfaceDetected = false;
        long speedMbps = 0;
        long rxBytes = 0;
        long txBytes = 0;

        try
        {
            var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

            if (activeNic != null)
            {
                interfaceDetected = true;
                nicName = activeNic.Description;
                nicType = activeNic.NetworkInterfaceType.ToString();
                isWifi = activeNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                         activeNic.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                         activeNic.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase);
                speedMbps = activeNic.Speed > 0 ? activeNic.Speed / 1_000_000 : 0;
                var stats = activeNic.GetIPStatistics();
                rxBytes = stats.BytesReceived;
                txBytes = stats.BytesSent;
            }
        }
        catch
        {
            interfaceDetected = false;
            nicName = "Não detectado";
            nicType = "Indisponível";
            speedMbps = 0;
            rxBytes = 0;
            txBytes = 0;
        }

        double? dnsMs = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await Dns.GetHostAddressesAsync("steampowered.com", cancellationToken);
            sw.Stop();
            dnsMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // DNS measurement is optional; absence is reported as unavailable.
        }

        bool hasJitterMeasurement = currentJitterMs.HasValue &&
                                    double.IsFinite(currentJitterMs.Value) &&
                                    currentJitterMs.Value >= 0;
        bool hasPacketLossMeasurement = currentPacketLossPercent.HasValue &&
                                        double.IsFinite(currentPacketLossPercent.Value) &&
                                        currentPacketLossPercent.Value >= 0;
        bool congestion = (hasJitterMeasurement && currentJitterMs!.Value >= 12.0) ||
                          (hasPacketLossMeasurement && currentPacketLossPercent!.Value >= 1.5);

        AcceleratorOperatingMode mode;
        string justification;
        if (!interfaceDetected)
        {
            mode = AcceleratorOperatingMode.Automatico;
            justification = "Nenhuma interface de rede ativa pôde ser confirmada. A recomendação de modo foi mantida neutra; verifique a conexão e tente novamente.";
        }
        else if (isWifi)
        {
            mode = AcceleratorOperatingMode.Estabilidade;
            justification = $"Conexão sem fio (Wi-Fi) detectada em '{nicName}'. Modo Estabilidade recomendado para reduzir oscilações típicas do enlace sem fio; isso não garante redução de ping.";
        }
        else if (congestion)
        {
            mode = AcceleratorOperatingMode.Estabilidade;
            string evidence = hasJitterMeasurement && currentJitterMs!.Value >= 12.0
                ? $"jitter medido de {currentJitterMs.Value:F1} ms"
                : $"perda de pacotes medida de {currentPacketLossPercent!.Value:F1}%";
            justification = $"Sinal de instabilidade detectado ({evidence}). Modo Estabilidade recomendado para priorizar consistência.";
        }
        else if (speedMbps >= 500 && hasJitterMeasurement && currentJitterMs!.Value < 4.0)
        {
            mode = AcceleratorOperatingMode.BaixaLatencia;
            justification = $"Link de alta velocidade ({speedMbps} Mbps) e jitter medido baixo ({currentJitterMs.Value:F1} ms). Modo Baixa Latência recomendado; o resultado depende também do servidor e da rota do jogo.";
        }
        else
        {
            mode = AcceleratorOperatingMode.Gaming;
            string link = speedMbps > 0
                ? $"Interface '{nicName}' negociada a {speedMbps} Mbps"
                : $"Interface '{nicName}' detectada, mas a velocidade negociada não foi informada";
            string jitter = hasJitterMeasurement
                ? $" Jitter medido: {currentJitterMs!.Value:F1} ms."
                : " Jitter não medido nesta análise.";
            justification = $"{link}.{jitter} Não há evidência suficiente para recomendar uma rota alternativa; mantenha a rota direta.";
        }

        return new AcceleratorNetworkEnvironmentAnalysis(
            ActiveInterfaceName: nicName,
            InterfaceType: nicType,
            IsWiFi: isWifi,
            NegotiatedLinkSpeedMbps: speedMbps,
            BytesReceived: rxBytes,
            BytesSent: txBytes,
            DnsResolutionTimeMs: dnsMs,
            LocalCongestionDetected: congestion,
            RecommendedMode: mode,
            ModeJustification: justification
        );
    }

    private static void EnqueueEvaluation(RouteComparisonEvaluation eval)
    {
        EvaluationHistory.Enqueue(eval);
        while (EvaluationHistory.Count > 50)
        {
            EvaluationHistory.TryDequeue(out _);
        }
    }
}
