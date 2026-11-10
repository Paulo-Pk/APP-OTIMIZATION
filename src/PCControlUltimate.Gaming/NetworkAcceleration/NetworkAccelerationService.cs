using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PCControlUltimate.Gaming.NetworkAcceleration;

public sealed record GameProcessInfo(int ProcessId, string ProcessName, string ExecutablePath);
public sealed record RouteProbeResult(string Label, string Host, int Port, int Samples, int Successful, double? AverageMs, double? JitterMs, double ConnectionFailurePercent, string Status, DateTimeOffset MeasuredAt);
public sealed record TraceHopResult(int Hop, string Address, double? RoundTripMs, string Status);
public sealed record TunnelOperationResult(bool Success, string Message, string? TunnelName = null);

public interface IRouteProbe
{
    Task<RouteProbeResult> MeasureTcpAsync(string host, int port, int samples = 5, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TraceHopResult>> TraceRouteAsync(string host, int maxHops = 16, CancellationToken cancellationToken = default);
}

public interface IAccelerationProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
}

public interface ITunnelProvider : IAccelerationProvider
{
    Task<TunnelOperationResult> InstallAsync(string configurationPath, CancellationToken cancellationToken = default);
    Task<TunnelOperationResult> RemoveAsync(string tunnelName, CancellationToken cancellationToken = default);
}

public sealed class GameProcessDetector
{
    private static readonly HashSet<string> KnownGameNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2", "csgo", "valorant", "league of legends", "leagueclient",
        "fortnite", "rocketleague", "r5apex", "apex", "overwatch",
        "overwatch 2", "gta5", "forza", "forzahorizon5", "forzahorizon6",
        "eldenring", "dota2", "rainbowsix", "rainbow six siege", "pubg",
        "cod", "modernwarfare", "destiny2"
    };

    public IReadOnlyList<GameProcessInfo> FindRunningGames()
    {
        var found = new List<GameProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName;
                if (!KnownGameNames.Contains(name) &&
                    !KnownGameNames.Contains(name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)))
                    continue;

                string path = string.Empty;
                try { path = process.MainModule?.FileName ?? string.Empty; } catch { /* protected process */ }
                found.Add(new GameProcessInfo(process.Id, name, path));
            }
            catch { /* process exited during enumeration */ }
            finally { process.Dispose(); }
        }
        return found.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class NetworkRouteProbe : IRouteProbe
{
    public async Task<RouteProbeResult> MeasureTcpAsync(string host, int port, int samples = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace))
            throw new ArgumentException("Informe um host ou IP válido.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        samples = Math.Clamp(samples, 3, 20);

        var timings = new List<double>();
        int failed = 0;
        for (int i = 0; i < samples; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var sw = Stopwatch.StartNew();
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                sw.Stop();
                timings.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { failed++; }
            catch (SocketException) { failed++; }
            catch (IOException) { failed++; }
        }

        double? average = timings.Count == 0 ? null : Math.Round(timings.Average(), 1);
        double? jitter = timings.Count < 2 ? null : Math.Round(timings.Zip(timings.Skip(1), (a, b) => Math.Abs(b - a)).Average(), 1);
        double loss = Math.Round(failed * 100d / samples, 1);
        string status = timings.Count == 0
            ? "Sem conexão TCP — não é possível medir a latência deste destino/porta"
            : $"{timings.Count}/{samples} conexões TCP concluídas";
        return new RouteProbeResult("Rota ativa", host, port, samples, timings.Count, average, jitter, loss, status, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<TraceHopResult>> TraceRouteAsync(string host, int maxHops = 16, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace))
            throw new ArgumentException("Informe um host válido.", nameof(host));

        var hops = new List<TraceHopResult>();
        using var ping = new Ping();
        byte[] payload = new byte[32];
        for (int ttl = 1; ttl <= Math.Clamp(maxHops, 1, 24); ttl++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var options = new PingOptions(ttl, true);
                var sw = Stopwatch.StartNew();
                var reply = await ping.SendPingAsync(host, 1200, payload, options).WaitAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();
                string address = reply.Address?.ToString() ?? "*";
                hops.Add(new TraceHopResult(ttl, address, reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired ? reply.RoundtripTime : null, reply.Status.ToString()));
                if (reply.Status == IPStatus.Success) break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { hops.Add(new TraceHopResult(ttl, "*", null, ex.GetType().Name)); }
        }
        return hops;
    }
}

/// <summary>
/// Optional adapter for the official WireGuard for Windows CLI. A tunnel changes Windows routing
/// according to AllowedIPs; it is NOT per-process routing. No tunnel is installed or started automatically.
/// </summary>
public sealed class WireGuardWindowsProvider : ITunnelProvider
{
    public string ProviderId => "wireguard-windows";
    public string DisplayName => "WireGuard for Windows (configuração própria)";
    public string? ExecutablePath => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe")
    }.FirstOrDefault(File.Exists);
    public bool IsAvailable => ExecutablePath != null;

    public async Task<TunnelOperationResult> InstallAsync(string configurationPath, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "WireGuard para Windows só pode ser controlado no Windows.");
        if (!IsAvailable) return new(false, "WireGuard for Windows não foi encontrado. Instale-o pela fonte oficial.");
        if (string.IsNullOrWhiteSpace(configurationPath) || !File.Exists(configurationPath))
            return new(false, "Selecione um arquivo de configuração WireGuard existente.");

        string config;
        try { config = await File.ReadAllTextAsync(configurationPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { return new(false, $"Não foi possível ler a configuração: {ex.Message}"); }

        if (!config.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) ||
            !config.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase) ||
            !config.Contains("[Peer]", StringComparison.OrdinalIgnoreCase) ||
            !config.Contains("PublicKey", StringComparison.OrdinalIgnoreCase) ||
            !config.Contains("Endpoint", StringComparison.OrdinalIgnoreCase))
            return new(false, "Configuração inválida: faltam seções/chaves obrigatórias de interface ou peer.");

        string[] forbidden = { "PreUp", "PostUp", "PreDown", "PostDown" };
        if (config.Split('\n').Any(line => forbidden.Any(key => line.TrimStart().StartsWith(key + " =", StringComparison.OrdinalIgnoreCase))))
            return new(false, "Configuração contém comandos de execução externos (hooks). Por segurança, importe um perfil sem scripts.");

        string tunnelName = Path.GetFileNameWithoutExtension(configurationPath);
        if (tunnelName.Length is < 1 or > 15 || tunnelName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            return new(false, "O nome do perfil deve ter até 15 caracteres alfanuméricos, hífen ou sublinhado.");

        bool fullTunnel = config.Contains("AllowedIPs = 0.0.0.0/0", StringComparison.OrdinalIgnoreCase) ||
                          config.Contains("AllowedIPs = ::/0", StringComparison.OrdinalIgnoreCase);
        var warning = fullTunnel
            ? " ATENÇÃO: AllowedIPs inclui rota padrão; a conexão poderá redirecionar o tráfego de TODO o Windows, não apenas do jogo."
            : " O escopo de tráfego depende dos AllowedIPs definidos no perfil.";
        var result = await RunWireGuardAsync(new[] { "/installtunnelservice", configurationPath }, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? new(true, "Serviço WireGuard instalado. Verifique a conectividade antes de abrir o jogo." + warning, tunnelName)
            : result;
    }

    public async Task<TunnelOperationResult> RemoveAsync(string tunnelName, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "Disponível somente no Windows.");
        if (!IsAvailable) return new(false, "WireGuard for Windows não foi encontrado.");
        if (string.IsNullOrWhiteSpace(tunnelName) || tunnelName.Length > 15 ||
            tunnelName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            return new(false, "Nome de túnel inválido.");
        return await RunWireGuardAsync(new[] { "/uninstalltunnelservice", tunnelName }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TunnelOperationResult> RunWireGuardAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo(ExecutablePath!)
            {
                // ShellExecute + runas displays the real Windows UAC consent prompt.
                // Output redirection is intentionally disabled for elevated ShellExecute.
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(arg => "\"" + arg.Replace("\"", "\\\\\"") + "\""))
            };
            using var process = Process.Start(start);
            if (process == null) return new(false, "O Windows não iniciou o processo WireGuard.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0
                ? new(true, "O comando WireGuard foi concluído com código de saída 0.")
                : new(false, $"WireGuard retornou código de saída {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(false, "A solicitação de elevação UAC foi cancelada. Nenhuma alteração foi confirmada.");
        }
        catch (OperationCanceledException) { return new(false, "Operação WireGuard cancelada ou excedeu o tempo limite."); }
        catch (Exception ex) { return new(false, $"Falha ao executar WireGuard: {ex.Message}"); }
    }

}
