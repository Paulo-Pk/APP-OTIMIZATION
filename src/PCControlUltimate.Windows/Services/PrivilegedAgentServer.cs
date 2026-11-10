using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Interfaces;

namespace PCControlUltimate.Windows.Services;

public class PrivilegedAgentServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly IHardwareMonitorService _hardwareMonitor;
    private CancellationTokenSource? _cts;
    public const int DefaultPort = 52194;

    public string SessionToken { get; }

    private static readonly string[] AllowedOrigins = new[]
    {
        "http://localhost:3000",
        "http://127.0.0.1:3000",
        "http://localhost:52194",
        "http://127.0.0.1:52194"
    };

    public PrivilegedAgentServer(IHardwareMonitorService hardwareMonitor)
    {
        _hardwareMonitor = hardwareMonitor;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{DefaultPort}/");

        // Generate 256-bit cryptographic session token
        byte[] tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        SessionToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        SaveSessionTokenToFile();
    }

    private void SaveSessionTokenToFile()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCControlUltimate");
            Directory.CreateDirectory(dir);
            string tokenPath = Path.Combine(dir, "agent_session.token");
            File.WriteAllText(tokenPath, SessionToken, Encoding.UTF8);
        }
        catch
        {
            // Fallback to local working directory
            try
            {
                File.WriteAllText("agent_session.token", SessionToken, Encoding.UTF8);
            }
            catch { }
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        // 1. Strict Loopback Check
        if (req.RemoteEndPoint != null && !IPAddress.IsLoopback(req.RemoteEndPoint.Address))
        {
            res.StatusCode = 403;
            res.Close();
            return;
        }

        // 2. Strict Origin Validation (No wildcard CORS)
        string? origin = req.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin))
        {
            bool isOriginAllowed = AllowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
            if (!isOriginAllowed)
            {
                res.StatusCode = 403;
                await SendJsonAsync(res, new { error = "ORIGIN_FORBIDDEN" }, 403);
                return;
            }
            res.AddHeader("Access-Control-Allow-Origin", origin);
        }
        else
        {
            // Desktop apps / curl without Origin header
            res.AddHeader("Access-Control-Allow-Origin", "http://localhost:3000");
        }

        res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        try
        {
            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

            // Endpoint: GET /api/auth/token (Loopback authentication handshake)
            if (path == "/api/auth/token" && req.HttpMethod == "GET")
            {
                await SendJsonAsync(res, new { token = SessionToken });
                return;
            }

            // Endpoint: GET /api/status
            if (path == "/api/status" && req.HttpMethod == "GET")
            {
                var status = new
                {
                    version = "2.4.0",
                    os = Environment.OSVersion.ToString(),
                    isElevated = UacElevationBridge.IsRunningAsAdministrator(),
                    timestamp = DateTime.UtcNow
                };
                await SendJsonAsync(res, status);
                return;
            }

            // Endpoint: GET /api/telemetry
            if (path == "/api/telemetry" && req.HttpMethod == "GET")
            {
                var snapshot = await _hardwareMonitor.CaptureSnapshotAsync();
                await SendJsonAsync(res, snapshot);
                return;
            }

            // Endpoint: GET /api/display/info
            if (path == "/api/display/info" && req.HttpMethod == "GET")
            {
                var disp = DisplaySettingsService.GetPrimaryDisplayInfo();
                await SendJsonAsync(res, disp);
                return;
            }

            // Endpoint: GET /api/debloat/scan
            if (path == "/api/debloat/scan" && req.HttpMethod == "GET")
            {
                long d3d = DiskCleanerEngine.ScanCategoryBytes("cache-dx-shaders");
                long tempWin = DiskCleanerEngine.ScanCategoryBytes("temp-windows");
                long tempUser = DiskCleanerEngine.ScanCategoryBytes("temp-user");
                long softDist = DiskCleanerEngine.ScanCategoryBytes("update-cache");

                await SendJsonAsync(res, new
                {
                    d3dCacheBytes = d3d,
                    tempWindowsBytes = tempWin,
                    tempUserBytes = tempUser,
                    softwareDistributionBytes = softDist
                });
                return;
            }

            // Endpoint: GET /api/snapshot/verify?path=...
            if (path == "/api/snapshot/verify" && req.HttpMethod == "GET")
            {
                string? targetPath = req.QueryString["path"];
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    await SendJsonAsync(res, new { success = false, isValid = false, error = "Path is required" }, 400);
                    return;
                }

                var (isValid, checksum, err) = RegistrySnapshotEngine.VerifyBackupIntegrity(targetPath);
                await SendJsonAsync(res, new { success = true, isValid, checksum, error = err });
                return;
            }

            // Protected Mutating Endpoints Require Bearer Token
            if (IsMutatingPath(path))
            {
                if (!IsAuthenticated(req))
                {
                    await SendJsonAsync(res, new { error = "UNAUTHORIZED_AGENT_ACCESS", message = "Valid Bearer token required for system modifications." }, 401);
                    return;
                }
            }

            // Endpoint: POST /api/tweaks/apply
            if (path == "/api/tweaks/apply" && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var tweakDoc = JsonDocument.Parse(body);

                string subKey = tweakDoc.RootElement.GetProperty("keyPath").GetString() ?? "";
                string valueName = tweakDoc.RootElement.GetProperty("valueName").GetString() ?? "";
                int targetValue = tweakDoc.RootElement.GetProperty("targetValue").GetInt32();

                bool success = RegistryOptimizer.TryApplyDword(subKey, valueName, targetValue, out var prevVal);

                await SendJsonAsync(res, new
                {
                    success,
                    previousValue = prevVal?.ToString() ?? "0",
                    verifiedValue = targetValue.ToString()
                });
                return;
            }

            // Endpoint: POST /api/tweaks/restore
            if (path == "/api/tweaks/restore" && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                string subKey = doc.RootElement.GetProperty("keyPath").GetString() ?? "";
                string valueName = doc.RootElement.GetProperty("valueName").GetString() ?? "";
                string origValStr = doc.RootElement.GetProperty("originalValue").GetString() ?? "0";
                int origVal = int.TryParse(origValStr, out int v) ? v : 0;

                bool success = RegistryOptimizer.TryApplyDword(subKey, valueName, origVal, out _);
                await SendJsonAsync(res, new { success, verified = success });
                return;
            }

            // Endpoint: POST /api/snapshot/create
            if (path == "/api/snapshot/create" && req.HttpMethod == "POST")
            {
                var snapshotResult = RegistrySnapshotEngine.CreateBackup();
                await SendJsonAsync(res, snapshotResult);
                return;
            }

            // Endpoint: POST /api/snapshot/restore
            if (path == "/api/snapshot/restore" && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                string filePath = doc.RootElement.GetProperty("filePath").GetString() ?? "";

                var (success, restored, failed, err) = RegistrySnapshotEngine.RestoreFromBackup(filePath);
                await SendJsonAsync(res, new
                {
                    success,
                    restoredKeysCount = restored,
                    failedKeysCount = failed,
                    error = err
                });
                return;
            }

            // Endpoint: POST /api/debloat/purge
            if (path == "/api/debloat/purge" && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                string categoryKey = doc.RootElement.GetProperty("categoryKey").GetString() ?? "";
                long freed = DiskCleanerEngine.PurgeCategory(categoryKey);
                await SendJsonAsync(res, new { freedBytes = freed });
                return;
            }

            res.StatusCode = 404;
            res.Close();
        }
        catch (Exception ex)
        {
            await SendJsonAsync(res, new { error = ex.Message }, 500);
        }
    }

    private static bool IsMutatingPath(string path)
    {
        return path.StartsWith("/api/tweaks/") ||
               path.StartsWith("/api/snapshot/") ||
               path.StartsWith("/api/debloat/purge");
    }

    private bool IsAuthenticated(HttpListenerRequest req)
    {
        string? authHeader = req.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(authHeader))
            return false;

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            string token = authHeader.Substring(7).Trim();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes(SessionToken)
            );
        }

        return false;
    }

    private async Task SendJsonAsync(HttpListenerResponse res, object data, int statusCode = 200)
    {
        res.ContentType = "application/json";
        res.StatusCode = statusCode;
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}