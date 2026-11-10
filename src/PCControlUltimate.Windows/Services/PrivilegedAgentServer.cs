using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Gaming.PresentMon;
using PCControlUltimate.Security;

namespace PCControlUltimate.Windows.Services;

public class PrivilegedAgentServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly ConcurrentQueue<DateTime> _requestTimestamps = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public const int DefaultPort = 52194;
    public const int MaxRequestBodyBytes = 64 * 1024; // 64 KB max payload
    public const int MaxRequestsPerMinute = 120;

    public string SessionToken { get; }

    public static string ProtectedTokenFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "agent_session.dpapi"
    );

    public static string AuditLogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "Logs",
        "agent_audit.log"
    );

    private static readonly string[] AllowedOrigins = new[]
    {
        "http://localhost:3000",
        "http://127.0.0.1:3000",
        "http://localhost:52194",
        "http://127.0.0.1:52194"
    };

    private static readonly string[] AllowedHosts = new[]
    {
        $"127.0.0.1:{DefaultPort}",
        $"localhost:{DefaultPort}"
    };

    public PrivilegedAgentServer(IHardwareMonitorService hardwareMonitor)
    {
        _hardwareMonitor = hardwareMonitor;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{DefaultPort}/");

        byte[] tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        SessionToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        IntegrityVerifier.SaveProtectedSessionToken(ProtectedTokenFilePath, SessionToken);
        WriteAuditLog("SERVER_INIT", "PrivilegedAgentServer initialized with DPAPI-protected session token.");
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PrivilegedAgentServer));
        _cts = new CancellationTokenSource();
        _listener.Start();
        WriteAuditLog("SERVER_START", $"Listening exclusively on http://127.0.0.1:{DefaultPort}/");
        Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestWithTimeoutAsync(context, token), token);
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleRequestWithTimeoutAsync(HttpListenerContext context, CancellationToken serverToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await HandleRequestAsync(context, timeoutCts.Token);
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var req = context.Request;
        var res = context.Response;

        try
        {
            // 1. Strict Loopback Address Validation
            if (req.RemoteEndPoint == null || !IPAddress.IsLoopback(req.RemoteEndPoint.Address))
            {
                WriteAuditLog("SECURITY_REJECT", $"Non-loopback request blocked from {req.RemoteEndPoint?.Address}");
                await SendJsonAsync(res, new { error = "LOOPBACK_ONLY" }, 403);
                return;
            }

            // 2. Host Header Validation (Prevents DNS Rebinding attacks)
            string hostHeader = req.UserHostName ?? string.Empty;
            if (!AllowedHosts.Any(h => string.Equals(h, hostHeader, StringComparison.OrdinalIgnoreCase)))
            {
                WriteAuditLog("SECURITY_REJECT", $"Invalid Host header blocked: {hostHeader}");
                await SendJsonAsync(res, new { error = "INVALID_HOST_HEADER" }, 403);
                return;
            }

            // 3. Strict Origin Validation (No wildcard CORS)
            string? origin = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                bool isOriginAllowed = AllowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
                if (!isOriginAllowed)
                {
                    WriteAuditLog("SECURITY_REJECT", $"Unauthorized Origin blocked: {origin}");
                    await SendJsonAsync(res, new { error = "ORIGIN_FORBIDDEN" }, 403);
                    return;
                }
                res.AddHeader("Access-Control-Allow-Origin", origin);
                res.AddHeader("Vary", "Origin");
            }

            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-PCControl-Client");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            // 4. Rate Limiting
            if (!CheckRateLimit())
            {
                WriteAuditLog("RATE_LIMIT", "Too many requests per minute.");
                await SendJsonAsync(res, new { error = "RATE_LIMIT_EXCEEDED" }, 429);
                return;
            }

            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

            // Endpoint: GET /api/status (Reports real authentication state)
            if (path == "/api/status" && req.HttpMethod == "GET")
            {
                bool isAuth = IsAuthenticated(req);
                var status = new
                {
                    version = "2.4.0",
                    os = Environment.OSVersion.ToString(),
                    isElevated = UacElevationBridge.IsRunningAsAdministrator(),
                    isAuthenticated = isAuth,
                    timestamp = DateTime.UtcNow
                };
                await SendJsonAsync(res, status);
                return;
            }

            // Security Rule: /api/auth/token NEVER hands out the token without proof of local user identity
            if (path == "/api/auth/token")
            {
                WriteAuditLog("SECURITY_REJECT", "Blocked unauthenticated attempt to read /api/auth/token over HTTP.");
                await SendJsonAsync(res, new
                {
                    error = "DIRECT_TOKEN_EXPOSURE_DISABLED",
                    message = "Session token is protected via Windows DPAPI and never exposed to unauthenticated HTTP callers."
                }, 401);
                return;
            }

            // 5. All other endpoints (including private telemetry and system mutations) require Bearer authentication
            if (!IsAuthenticated(req))
            {
                WriteAuditLog("UNAUTHORIZED_ACCESS", $"Unauthenticated call blocked on {req.HttpMethod} {path}");
                await SendJsonAsync(res, new
                {
                    error = "UNAUTHORIZED_AGENT_ACCESS",
                    message = "Valid DPAPI-issued Bearer token is required."
                }, 401);
                return;
            }

            // 6. Anti-CSRF & Payload Size Validation for POST requests
            if (req.HttpMethod == "POST")
            {
                string contentType = req.ContentType ?? string.Empty;
                if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await SendJsonAsync(res, new { error = "INVALID_CONTENT_TYPE" }, 415);
                    return;
                }

                if (req.ContentLength64 > MaxRequestBodyBytes)
                {
                    await SendJsonAsync(res, new { error = "PAYLOAD_TOO_LARGE" }, 413);
                    return;
                }
            }

            // Read-Only Authenticated Routes
            if (path == "/api/telemetry" && req.HttpMethod == "GET")
            {
                var snapshot = await _hardwareMonitor.CaptureSnapshotAsync();
                await SendJsonAsync(res, snapshot);
                return;
            }

            if (path == "/api/display/info" && req.HttpMethod == "GET")
            {
                var disp = DisplaySettingsService.GetPrimaryDisplayInfo();
                await SendJsonAsync(res, disp);
                return;
            }

            if (path == "/api/drivers" && req.HttpMethod == "GET")
            {
                var drivers = DriverScannerService.QuerySignedDrivers();
                await SendJsonAsync(res, new { drivers });
                return;
            }

            if (path == "/api/network/diagnostics" && req.HttpMethod == "GET")
            {
                var netDiag = await NetworkDiagnosticsService.RunRealDiagnosticsAsync();
                await SendJsonAsync(res, netDiag);
                return;
            }

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

            if (path == "/api/snapshot/verify" && req.HttpMethod == "GET")
            {
                string? targetPath = req.QueryString["path"] ?? req.QueryString["id"];
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    await SendJsonAsync(res, new { success = false, isValid = false, error = "Path is required" }, 400);
                    return;
                }

                var (isValid, checksum, err) = RegistrySnapshotEngine.VerifyBackupIntegrity(targetPath);
                await SendJsonAsync(res, new { success = true, isValid, checksum, error = err });
                return;
            }

            // Mutating Routes Protected by Mutual Exclusion Lock
            if (req.HttpMethod == "POST")
            {
                await _mutationLock.WaitAsync(cancellationToken);
                try
                {
                    var body = await ReadBoundedBodyAsync(req, cancellationToken);
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

                    if (path == "/api/tweaks/apply")
                    {
                        string tweakId = doc.RootElement.TryGetProperty("tweakId", out var tidEl) ? (tidEl.GetString() ?? "") : "";
                        string? reqVal = doc.RootElement.TryGetProperty("targetValue", out var tvEl) ? tvEl.ToString() : null;

                        RegistryOperationResult opResult;
                        if (!string.IsNullOrWhiteSpace(tweakId) && RegistryOptimizer.Catalog.ContainsKey(tweakId))
                        {
                            opResult = RegistryOptimizer.ApplyCatalogTweak(tweakId, reqVal);
                        }
                        else
                        {
                            string subKey = doc.RootElement.TryGetProperty("keyPath", out var kpEl) ? (kpEl.GetString() ?? "") : "";
                            string valueName = doc.RootElement.TryGetProperty("valueName", out var vnEl) ? (vnEl.GetString() ?? "") : "";
                            string valueKindStr = doc.RootElement.TryGetProperty("valueKind", out var vkEl) ? (vkEl.GetString() ?? "DWord") : "DWord";
                            if (!Enum.TryParse<RegistryValueKind>(valueKindStr, true, out var parsedKind))
                                parsedKind = RegistryValueKind.DWord;

                            opResult = RegistryOptimizer.TryApplyTypedValue(subKey, valueName, parsedKind, reqVal ?? "0");
                        }

                        WriteAuditLog("TWEAK_APPLY", $"Tweak={tweakId} Success={opResult.Success} Verified={opResult.VerifiedValue} Error={opResult.ErrorMessage}");

                        await SendJsonAsync(res, new
                        {
                            success = opResult.Success,
                            previousValue = opResult.PreviousValue ?? "",
                            verifiedValue = opResult.VerifiedValue ?? "",
                            valueKind = opResult.ValueKind,
                            wasInitiallyAbsent = opResult.WasInitiallyAbsent,
                            keyCreatedByApp = opResult.KeyCreatedByApp,
                            error = opResult.ErrorMessage
                        });
                        return;
                    }

                    if (path == "/api/tweaks/restore")
                    {
                        string subKey = doc.RootElement.TryGetProperty("keyPath", out var kpEl) ? (kpEl.GetString() ?? "") : "";
                        string valueName = doc.RootElement.TryGetProperty("valueName", out var vnEl) ? (vnEl.GetString() ?? "") : "";
                        string? origValStr = doc.RootElement.TryGetProperty("originalValue", out var ovEl) && ovEl.ValueKind != JsonValueKind.Null
                            ? ovEl.ToString()
                            : null;
                        string valueKindStr = doc.RootElement.TryGetProperty("valueKind", out var vkEl) ? (vkEl.GetString() ?? "DWord") : "DWord";
                        bool wasAbsent = doc.RootElement.TryGetProperty("wasInitiallyAbsent", out var waEl) && waEl.GetBoolean();
                        bool keyCreated = doc.RootElement.TryGetProperty("keyCreatedByApp", out var kcEl) && kcEl.GetBoolean();

                        var restoreRes = RegistryOptimizer.TryRestoreValue(subKey, valueName, origValStr, valueKindStr, wasAbsent, keyCreated);
                        WriteAuditLog("TWEAK_RESTORE", $"Key={subKey}\\{valueName} Success={restoreRes.Success} WasAbsent={wasAbsent}");

                        await SendJsonAsync(res, new
                        {
                            success = restoreRes.Success,
                            verified = restoreRes.Success,
                            verifiedValue = restoreRes.VerifiedValue,
                            error = restoreRes.ErrorMessage
                        });
                        return;
                    }

                    if (path == "/api/snapshot/create")
                    {
                        var snapshotResult = RegistrySnapshotEngine.CreateBackup();
                        WriteAuditLog("SNAPSHOT_CREATE", "Registry backup snapshot created.");
                        await SendJsonAsync(res, snapshotResult);
                        return;
                    }

                    if (path == "/api/snapshot/restore")
                    {
                        string filePath = doc.RootElement.TryGetProperty("filePath", out var fpEl)
                            ? (fpEl.GetString() ?? "")
                            : (doc.RootElement.TryGetProperty("snapshotId", out var idEl) ? (idEl.GetString() ?? "") : "");

                        var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(filePath);
                        WriteAuditLog("SNAPSHOT_RESTORE", $"File={filePath} Success={report.Success} Restored={report.RestoredCount} Failed={report.FailedCount}");

                        await SendJsonAsync(res, new
                        {
                            success = report.Success,
                            partialSuccess = report.PartialSuccess,
                            restoredKeysCount = report.RestoredCount,
                            failedKeysCount = report.FailedCount,
                            error = report.ErrorMessage
                        });
                        return;
                    }

                    if (path == "/api/debloat/purge")
                    {
                        string categoryKey = doc.RootElement.TryGetProperty("categoryKey", out var ckEl) ? (ckEl.GetString() ?? "") : "";
                        bool confirmUpdate = !doc.RootElement.TryGetProperty("confirmUpdateCache", out var cuEl) || cuEl.GetBoolean();
                        var purgeReport = DiskCleanerEngine.PurgeCategoryWithReport(categoryKey, confirmUpdate);

                        WriteAuditLog("DEBLOAT_PURGE", $"Category={categoryKey} FreedBytes={purgeReport.FreedBytes} DeletedFiles={purgeReport.DeletedFilesCount}");

                        await SendJsonAsync(res, new
                        {
                            freedBytes = purgeReport.FreedBytes,
                            deletedFilesCount = purgeReport.DeletedFilesCount,
                            skippedLockedCount = purgeReport.SkippedLockedCount,
                            skippedSymlinksCount = purgeReport.SkippedSymlinksCount,
                            errors = purgeReport.Errors
                        });
                        return;
                    }

                    if (path == "/api/presentmon/capture")
                    {
                        string processName = doc.RootElement.TryGetProperty("processName", out var pnEl) ? (pnEl.GetString() ?? "") : "";
                        int durationSeconds = doc.RootElement.TryGetProperty("durationSeconds", out var dsEl) ? dsEl.GetInt32() : 10;

                        var captureRes = await PresentMonSessionParser.CaptureProcessSessionAsync(processName, durationSeconds, cancellationToken);
                        if (!captureRes.Success || captureRes.Summary == null)
                        {
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                error = captureRes.ErrorMessage ?? "Captura PresentMon indisponível."
                            }, 400);
                            return;
                        }

                        await SendJsonAsync(res, new
                        {
                            success = true,
                            avgFps = captureRes.Summary.AverageFps,
                            minFps = captureRes.Summary.MinFps,
                            onePercentLow = captureRes.Summary.OnePercentLowFps,
                            frametimeAvgMs = captureRes.Summary.AverageFrametimeMs,
                            frametimeStDevMs = captureRes.Summary.FrametimeStandardDeviationMs,
                            capturedFrames = captureRes.Summary.TotalFramesCount,
                            csvFilePath = captureRes.CsvFilePath
                        });
                        return;
                    }
                }
                finally
                {
                    _mutationLock.Release();
                }
            }

            await SendJsonAsync(res, new { error = "ROUTE_NOT_FOUND" }, 404);
        }
        catch (JsonException)
        {
            await SendJsonAsync(res, new { error = "MALFORMED_JSON_PAYLOAD" }, 400);
        }
        catch (Exception ex)
        {
            WriteAuditLog("SERVER_ERROR", ex.Message);
            await SendJsonAsync(res, new { error = ex.Message }, 500);
        }
    }

    private bool CheckRateLimit()
    {
        var now = DateTime.UtcNow;
        while (_requestTimestamps.TryPeek(out var oldest) && (now - oldest).TotalSeconds > 60)
        {
            _requestTimestamps.TryDequeue(out _);
        }

        if (_requestTimestamps.Count >= MaxRequestsPerMinute)
            return false;

        _requestTimestamps.Enqueue(now);
        return true;
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpListenerRequest req, CancellationToken token)
    {
        using var ms = new MemoryStream();
        byte[] buffer = new byte[4096];
        int totalRead = 0;

        while (true)
        {
            int read = await req.InputStream.ReadAsync(buffer, 0, buffer.Length, token);
            if (read == 0) break;

            totalRead += read;
            if (totalRead > MaxRequestBodyBytes)
                throw new InvalidOperationException("Request body exceeds maximum allowed size of 64 KB.");

            ms.Write(buffer, 0, read);
        }

        return req.ContentEncoding.GetString(ms.ToArray());
    }

    public bool ValidateBearerToken(string? candidateToken)
    {
        if (string.IsNullOrWhiteSpace(candidateToken)) return false;

        byte[] candidateBytes = Encoding.UTF8.GetBytes(candidateToken.Trim());
        byte[] expectedBytes = Encoding.UTF8.GetBytes(SessionToken);

        if (candidateBytes.Length != expectedBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }

    private bool IsAuthenticated(HttpListenerRequest req)
    {
        string? authHeader = req.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(authHeader))
            return false;

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            string token = authHeader.Substring(7).Trim();
            return ValidateBearerToken(token);
        }

        return false;
    }

    private static void WriteAuditLog(string action, string message)
    {
        try
        {
            string? dir = Path.GetDirectoryName(AuditLogFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string line = $"{DateTime.UtcNow:O} [{action}] {message}{Environment.NewLine}";
            File.AppendAllText(AuditLogFilePath, line, Encoding.UTF8);
        }
        catch { }
    }

    private static async Task SendJsonAsync(HttpListenerResponse res, object data, int statusCode = 200)
    {
        try
        {
            res.ContentType = "application/json";
            res.StatusCode = statusCode;
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.Close();
        }
        catch { }
    }

    public void Stop()
    {
        if (_disposed) return;
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
            WriteAuditLog("SERVER_STOP", "PrivilegedAgentServer stopped cleanly.");
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _listener.Close();
        _mutationLock.Dispose();
        _cts?.Dispose();
    }
}