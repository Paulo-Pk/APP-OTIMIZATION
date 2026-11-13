using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Gaming.PresentMon;
using PCControlUltimate.Gaming.Network;
using PCControlUltimate.Security;

namespace PCControlUltimate.Windows.Services;

public class PrivilegedAgentServer : IDisposable
{
    private sealed class RequestBodyTooLargeException : Exception
    {
        public RequestBodyTooLargeException() : base("Request body exceeds maximum allowed size of 64 KB.") { }
    }

    private readonly HttpListener _listener;
    private readonly IHardwareMonitorService? _hardwareMonitor;
    private readonly NetworkDiagnosticsService _networkDiagnostics;
    private readonly WingetSoftwareCenterService _softwareCenter = new();
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    // Bound concurrent handlers so loopback clients cannot create an unbounded
    // number of fire-and-forget request tasks.
    private readonly SemaphoreSlim _requestSlots = new(32, 32);
    private readonly ConcurrentQueue<DateTime> _requestTimestamps = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _recentAuditEvents = new();
    private readonly object _lifecycleLock = new();
    private readonly object _rateLimitLock = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public const int DefaultPort = 52194;
    public const int MaxRequestBodyBytes = 64 * 1024; // 64 KB max payload
    public const int MaxRequestsPerMinute = 120;
    public const string ExpectedLocalClientHeader = "WPF-Native-v2.4";

    public int Port { get; }
    public string SessionToken { get; }
    public TokenProtectionResult TokenProtectionStatus { get; }

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
        "http://127.0.0.1:52194",
        "https://app.pccontrol.local"
    };

    public PrivilegedAgentServer(int port = DefaultPort) : this(null, port, null)
    {
    }

    public PrivilegedAgentServer(
        IHardwareMonitorService? hardwareMonitor,
        int port = DefaultPort,
        NetworkDiagnosticsService? networkDiagnostics = null)
    {
        _hardwareMonitor = hardwareMonitor;
        _networkDiagnostics = networkDiagnostics ?? new NetworkDiagnosticsService();
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

        byte[] tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        SessionToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        TokenProtectionStatus = IntegrityVerifier.SaveProtectedSessionTokenWithReport(ProtectedTokenFilePath, SessionToken);
        if (TokenProtectionStatus.Success && TokenProtectionStatus.DpapiProtected && TokenProtectionStatus.AclRestricted)
        {
            WriteAuditLog("SERVER_INIT", "Session token protected with Windows DPAPI (CurrentUser) and restricted NTFS ACL.");
        }
        else
        {
            WriteAuditLog("SERVER_INIT_WARNING",
                $"Session token protection incomplete (DPAPI={TokenProtectionStatus.DpapiProtected}, ACL={TokenProtectionStatus.AclRestricted}): {TokenProtectionStatus.ErrorMessage}");
        }
    }

    public IReadOnlyList<string> GetAuditEvents() => _recentAuditEvents.ToArray();

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PrivilegedAgentServer));
            if (_listener.IsListening) return;

            var cts = new CancellationTokenSource();
            _listener.Start();
            _cts = cts;
            WriteAuditLog("SERVER_START", $"Listening exclusively on http://127.0.0.1:{Port}/");
            _ = Task.Run(() => ListenLoopAsync(cts.Token));
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();

                try
                {
                    // Stop accepting additional requests while all handler slots
                    // are occupied. This keeps queued work bounded by HttpListener
                    // instead of allocating an unbounded number of managed tasks.
                    await _requestSlots.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    try { context.Response.Abort(); } catch { }
                    break;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleRequestWithTimeoutAsync(context, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Observe unexpected handler failures and release the
                        // connection without leaking an unobserved task exception.
                        WriteAuditLog("REQUEST_HANDLER_FAILURE", ex.GetType().Name);
                        try { context.Response.Abort(); } catch { }
                    }
                    finally
                    {
                        _requestSlots.Release();
                    }
                }, CancellationToken.None);
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

        // PresentMon captures may run for up to 120 seconds, plus the parser's
        // 10-second process-exit grace period. Keep ordinary API requests short.
        TimeSpan requestTimeout = string.Equals(
            context.Request.Url?.AbsolutePath,
            "/api/presentmon/capture",
            StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(135)
            : TimeSpan.FromSeconds(15);

        timeoutCts.CancelAfter(requestTimeout);
        await HandleRequestAsync(context, timeoutCts.Token);
    }

    /// <summary>
    /// Validates Host header against loopback bindings on the active port.
    /// </summary>
    public bool ValidateHostHeader(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;
        string trimmed = hostHeader.Trim();
        return string.Equals(trimmed, $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, $"localhost:{Port}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] AllowedClientIdentifiers = new[]
    {
        ExpectedLocalClientHeader,
        "PCControl-WPF/2.1",
        "PCControl-WPF/2.5",
        "PCControl-WebBridge/2.1",
        "PCControl-WebBridge/2.5"
    };

    /// <summary>
    /// Enforces strict Origin validation for browser callers and anti-replay client proof for local WPF callers without Origin.
    /// </summary>
    public (bool IsValid, string? ErrorReason) ValidateOriginAndClientPolicy(
        string? originHeader,
        string? clientHeader,
        string? timestampHeader,
        string? nonceHeader)
    {
        bool ok = ValidateOriginAndClientPolicy(originHeader, clientHeader, timestampHeader, nonceHeader, out string? errorCode);
        return (ok, errorCode);
    }

    /// <summary>
    /// Enforces strict Origin validation for browser callers and anti-replay client proof for local WPF callers without Origin.
    /// Rejects requests with missing Origin unless valid X-PCControl-Client, X-PCControl-Timestamp, and X-PCControl-Nonce are supplied.
    /// </summary>
    public bool ValidateOriginAndClientPolicy(
        string? originHeader,
        string? clientHeader,
        string? timestampHeader,
        string? nonceHeader,
        out string? errorCode)
    {
        errorCode = null;
        PruneExpiredNonces();

        // Case A: Origin header is present (Browser / WebView caller)
        if (!string.IsNullOrWhiteSpace(originHeader))
        {
            bool isAllowedOrigin = AllowedOrigins.Any(o => string.Equals(o, originHeader.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!isAllowedOrigin)
            {
                errorCode = "ORIGIN_FORBIDDEN";
                return false;
            }

            // If nonce is also provided by the browser client, validate anti-replay
            if (!string.IsNullOrWhiteSpace(nonceHeader))
            {
                if (!ValidateTimestampAndNonce(timestampHeader, nonceHeader, out errorCode))
                    return false;
            }
            return true;
        }

        // Case B: Origin header is absent — only permitted for legitimate local WPF client with explicit header + anti-replay nonce
        string trimmedClient = clientHeader?.Trim() ?? string.Empty;
        bool isAllowedClient = AllowedClientIdentifiers.Any(c => string.Equals(c, trimmedClient, StringComparison.Ordinal));
        if (!isAllowedClient)
        {
            errorCode = "MISSING_ORIGIN_OR_CLIENT_PROOF";
            return false;
        }

        if (!ValidateTimestampAndNonce(timestampHeader, nonceHeader, out errorCode))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates client identifier and cryptographic HMAC-SHA256 signature over HTTP method, path, timestamp, nonce, and body hash.
    /// </summary>
    public (bool IsValid, string? ErrorReason) ValidateRequestHmacSignature(
        string? clientHeader,
        string? signatureHeader,
        string httpMethod,
        string pathAndQuery,
        string? timestampHeader,
        string? nonceHeader,
        string? bodyContent)
    {
        string trimmedClient = clientHeader?.Trim() ?? string.Empty;
        if (!AllowedClientIdentifiers.Any(c => string.Equals(c, trimmedClient, StringComparison.Ordinal)))
        {
            return (false, "Identificador de cliente local inválido ou não autorizado.");
        }

        return ValidateRequestHmacSignature(
            httpMethod,
            pathAndQuery,
            timestampHeader,
            nonceHeader,
            bodyContent,
            signatureHeader
        );
    }

    /// <summary>
    /// Validates the cryptographic HMAC-SHA256 signature over HTTP method, path, timestamp, nonce, and body hash.
    /// </summary>
    public (bool IsValid, string? ErrorReason) ValidateRequestHmacSignature(
        string httpMethod,
        string pathAndQuery,
        string? timestampHeader,
        string? nonceHeader,
        string? bodyContent,
        string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(nonceHeader))
        {
            return (false, "MISSING_TIMESTAMP_OR_NONCE_FOR_SIGNATURE: Cabeçalhos de timestamp ou nonce ausentes.");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return (false, "MISSING_HMAC_SIGNATURE: Assinatura HMAC-SHA256 ausente.");
        }

        bool valid = IntegrityVerifier.VerifyRequestHmacSha256(
            SessionToken,
            httpMethod,
            pathAndQuery,
            timestampHeader,
            nonceHeader,
            bodyContent ?? string.Empty,
            signatureHeader
        );

        return valid
            ? (true, null)
            : (false, "INVALID_HMAC_SIGNATURE_TAMPERED_PAYLOAD: Assinatura HMAC-SHA256 inválida ou payload adulterado.");
    }

    public bool ValidateTimestampAndNonce(string? timestampHeader, string? nonceHeader, out string? errorCode)
    {
        errorCode = null;
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(nonceHeader))
        {
            errorCode = "MISSING_ANTI_REPLAY_HEADERS";
            return false;
        }

        string cleanNonce = nonceHeader.Trim();
        if (cleanNonce.Length < 8 || cleanNonce.Length > 128)
        {
            errorCode = "INVALID_NONCE_FORMAT";
            return false;
        }

        DateTimeOffset requestTime;
        if (long.TryParse(timestampHeader.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds))
        {
            // FromUnixTimeSeconds throws for values outside DateTimeOffset's
            // supported range. Treat hostile/out-of-range headers as invalid
            // input instead of allowing an exception to escape validation.
            if (unixSeconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
                unixSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            {
                errorCode = "INVALID_TIMESTAMP_FORMAT";
                return false;
            }

            requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        else if (!DateTimeOffset.TryParse(timestampHeader.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out requestTime))
        {
            errorCode = "INVALID_TIMESTAMP_FORMAT";
            return false;
        }

        double skewSeconds = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
        if (skewSeconds > 120.0)
        {
            errorCode = "TIMESTAMP_OUT_OF_WINDOW";
            return false;
        }

        if (!_usedNonces.TryAdd(cleanNonce, DateTimeOffset.UtcNow))
        {
            errorCode = "REPLAY_DETECTED_NONCE_REUSED";
            return false;
        }

        return true;
    }

    private void PruneExpiredNonces()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-2);
        foreach (var kvp in _usedNonces)
        {
            if (kvp.Value < cutoff)
            {
                _usedNonces.TryRemove(kvp.Key, out _);
            }
        }
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
                await SendJsonAsync(res, new { error = "LOOPBACK_ONLY", message = "Access restricted to 127.0.0.1 loopback." }, 403);
                return;
            }

            // 2. Host Header Validation (Prevents DNS Rebinding attacks)
            string hostHeader = req.UserHostName ?? string.Empty;
            if (!ValidateHostHeader(hostHeader))
            {
                WriteAuditLog("SECURITY_REJECT", $"Invalid Host header blocked: {hostHeader}");
                await SendJsonAsync(res, new { error = "INVALID_HOST_HEADER", message = "Host header must match loopback interface." }, 403);
                return;
            }

            // 3. Strict Origin & Local Client Anti-Replay Policy (Never uses wildcard CORS)
            string? origin = req.Headers["Origin"];
            string? clientHeader = req.Headers["X-PCControl-Client"];
            string? timestampHeader = req.Headers["X-PCControl-Timestamp"];
            string? nonceHeader = req.Headers["X-PCControl-Nonce"];
            string? signatureHeader = req.Headers["X-PCControl-Signature"];

            if (!string.IsNullOrEmpty(origin))
            {
                bool isOriginAllowed = AllowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
                if (!isOriginAllowed)
                {
                    WriteAuditLog("SECURITY_REJECT", $"Unauthorized Origin blocked: {origin}");
                    await SendJsonAsync(res, new { error = "ORIGIN_FORBIDDEN", message = "Origin is not authorized." }, 403);
                    return;
                }
                res.AddHeader("Access-Control-Allow-Origin", origin);
                res.AddHeader("Vary", "Origin");
            }

            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-PCControl-Client, X-PCControl-Timestamp, X-PCControl-Nonce, X-PCControl-Signature");

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
                await SendJsonAsync(res, new { error = "RATE_LIMIT_EXCEEDED", message = "Request rate limit exceeded." }, 429);
                return;
            }

            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

            // Endpoint: GET /api/status (Reports real authentication and DPAPI/ACL state without exposing sensitive tokens)
            if (path == "/api/status" && req.HttpMethod == "GET")
            {
                bool isAuth = IsAuthenticated(req);
                var status = new
                {
                    version = "2.5.0",
                    os = Environment.OSVersion.ToString(),
                    isElevated = UacElevationBridge.IsRunningAsAdministrator(),
                    isAuthenticated = isAuth,
                    authenticated = isAuth,
                    isTokenDpapiProtected = TokenProtectionStatus.DpapiProtected,
                    isTokenAclRestricted = TokenProtectionStatus.AclRestricted,
                    hmacSigningSupported = true,
                    tokenProtectionNotice = TokenProtectionStatus.ErrorMessage,
                    timestamp = DateTime.UtcNow
                };
                await SendJsonAsync(res, status);
                return;
            }

            // Security Rule: /api/auth/token NEVER exposes the token over HTTP
            if (path == "/api/auth/token")
            {
                WriteAuditLog("SECURITY_REJECT", "Blocked attempt to read /api/auth/token over HTTP.");
                await SendJsonAsync(res, new
                {
                    error = "DIRECT_TOKEN_EXPOSURE_DISABLED",
                    message = "Session token is protected via Windows DPAPI and never exposed over HTTP."
                }, 401);
                return;
            }

            // 5. All other endpoints require valid Bearer token authentication
            if (!IsAuthenticated(req))
            {
                WriteAuditLog("UNAUTHORIZED_ACCESS", $"Unauthenticated call blocked on {req.HttpMethod} {path}");
                await SendJsonAsync(res, new
                {
                    error = "UNAUTHORIZED_AGENT_ACCESS",
                    message = "Valid DPAPI-protected Bearer session token is required."
                }, 401);
                return;
            }

            // 6. Validate Origin / Local Client Anti-Replay Policy for all authenticated operations
            if (!ValidateOriginAndClientPolicy(origin, clientHeader, timestampHeader, nonceHeader, out string? policyErr))
            {
                WriteAuditLog("SECURITY_REJECT_POLICY", $"Blocked {req.HttpMethod} {path}: {policyErr}");
                await SendJsonAsync(res, new
                {
                    error = policyErr ?? "ORIGIN_OR_ANTI_REPLAY_FAILED",
                    message = "Request rejected by Origin / Local Client Anti-Replay security policy."
                }, 403);
                return;
            }

            // 7. Anti-CSRF & Payload Size Validation for POST requests
            if (req.HttpMethod == "POST")
            {
                string contentType = req.ContentType ?? string.Empty;
                if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await SendJsonAsync(res, new { error = "INVALID_CONTENT_TYPE", message = "Content-Type must be application/json." }, 415);
                    return;
                }

                if (req.ContentLength64 > MaxRequestBodyBytes)
                {
                    await SendJsonAsync(res, new { error = "PAYLOAD_TOO_LARGE", message = "Payload exceeds 64 KB limit." }, 413);
                    return;
                }
            }

            // Read-Only Authenticated Routes
            if (path == "/api/telemetry" && req.HttpMethod == "GET")
            {
                if (_hardwareMonitor == null)
                {
                    await SendJsonAsync(res, new { available = false, error = "HardwareMonitorService not initialized." });
                    return;
                }
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
                await SendJsonAsync(res, new
                {
                    policyNotice = DriverScannerService.ModulePolicyNotice,
                    scanError = DriverScannerService.LastError,
                    drivers
                });
                return;
            }

            if (path == "/api/network/game-targets" && req.HttpMethod == "GET")
            {
                // Only closed-catalog game IDs are accepted; arbitrary hostnames are never supplied by the client.
                string rawGameIds = req.QueryString["games"] ?? string.Empty;
                string[] gameIds = rawGameIds.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                var gameMeasurements = await _networkDiagnostics.MeasureGameReferenceTargetsAsync(gameIds, cancellationToken);
                await SendJsonAsync(res, gameMeasurements);
                return;
            }

            if (path == "/api/network/diagnostics" && req.HttpMethod == "GET")
            {
                var netDiag = await _networkDiagnostics.RunRealDiagnosticsAsync(cancellationToken);
                await SendJsonAsync(res, netDiag);
                return;
            }

            if (path == "/api/network/throughput" && req.HttpMethod == "GET")
            {
                var throughput = await _networkDiagnostics.MeasureExternalThroughputAsync(cancellationToken);
                await SendJsonAsync(res, throughput);
                return;
            }

            if (path == "/api/debloat/scan" && req.HttpMethod == "GET")
            {
                var d3d = DiskCleanerEngine.SimulateCategoryDryRun("cache-dx-shaders");
                var tempWin = DiskCleanerEngine.SimulateCategoryDryRun("temp-windows");
                var tempUser = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
                var softDist = DiskCleanerEngine.SimulateCategoryDryRun("update-cache");
                var prefetch = DiskCleanerEngine.SimulateCategoryDryRun("temp-prefetch");
                var deliveryOptimization = DiskCleanerEngine.SimulateCategoryDryRun("cache-delivery-optimization");
                var werQueue = DiskCleanerEngine.SimulateCategoryDryRun("wer-report-queue");
                var werArchive = DiskCleanerEngine.SimulateCategoryDryRun("wer-report-archive");

                await SendJsonAsync(res, new
                {
                    isEstimateOnly = true,
                    d3dCacheBytes = d3d.EstimatedBytes,
                    tempWindowsBytes = tempWin.EstimatedBytes,
                    tempUserBytes = tempUser.EstimatedBytes,
                    softwareDistributionBytes = softDist.EstimatedBytes,
                    prefetchBytes = prefetch.EstimatedBytes,
                    deliveryOptimizationBytes = deliveryOptimization.EstimatedBytes,
                    werQueueBytes = werQueue.EstimatedBytes,
                    werArchiveBytes = werArchive.EstimatedBytes,
                    categories = new[] { d3d, tempWin, tempUser, softDist, prefetch, deliveryOptimization, werQueue, werArchive }
                });
                return;
            }

            if (path == "/api/snapshot/list" && req.HttpMethod == "GET")
            {
                var backups = RegistrySnapshotEngine.ListBackups();
                await SendJsonAsync(res, new { backups });
                return;
            }

            if (path == "/api/snapshot/inspect" && req.HttpMethod == "GET")
            {
                string? targetPath = req.QueryString["path"] ?? req.QueryString["id"];
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    await SendJsonAsync(res, new { success = false, error = "Snapshot path or ID is required." }, 400);
                    return;
                }

                var manifest = RegistrySnapshotEngine.InspectBackup(targetPath);
                if (manifest == null)
                {
                    await SendJsonAsync(res, new { success = false, error = "Backup file not found or unreadable." }, 404);
                    return;
                }

                var (isValid, checksum, verifyErr) = RegistrySnapshotEngine.VerifyBackupIntegrity(targetPath);
                await SendJsonAsync(res, new
                {
                    success = true,
                    isValid,
                    checksum,
                    verificationError = verifyErr,
                    manifest
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
                await SendJsonAsync(res, new { success = isValid, isValid, checksum, error = err }, isValid ? 200 : 422);
                return;
            }

            if (path == "/api/games/discover" && req.HttpMethod == "GET")
            {
                var games = GameNetworkAcceleratorService.DiscoverInstalledGames();
                await SendJsonAsync(res, new { games, scannedAtUtc = DateTime.UtcNow.ToString("O"), scope = "Supported launcher manifests and registered game catalogs; no full-disk scan." });
                return;
            }

            if (path == "/api/presentmon/processes" && req.HttpMethod == "GET")
            {
                var processes = PresentMonSessionParser.ListAvailableProcesses();
                var (installed, binPath, version) = PresentMonSessionParser.CheckPresentMonInstallation();
                await SendJsonAsync(res, new
                {
                    presentMonInstalled = installed,
                    presentMonPath = binPath,
                    presentMonVersion = version,
                    processes
                });
                return;
            }

            if (path == "/api/presentmon/history" && req.HttpMethod == "GET")
            {
                var history = PresentMonSessionParser.GetSessionHistory();
                await SendJsonAsync(res, new { sessions = history });
                return;
            }

            if (path == "/api/diagnostics/audit" && req.HttpMethod == "GET")
            {
                await SendJsonAsync(res, new
                {
                    agentAuditEvents = GetAuditEvents(),
                    registryAuditLogs = RegistryOptimizer.GetAuditLogs(),
                    stateLedgerEntries = RegistryStateLedger.GetAllEntries()
                });
                return;
            }

            // Read-only Registry inspection is authenticated but does not enter the mutation lock
            // or accept any client-supplied path/value. Only the closed backend catalog is inspected.
            if (path == "/api/tweaks/inspect" && req.HttpMethod == "GET")
            {
                var inspections = RegistryOptimizer.Catalog.Values
                    .OrderBy(def => def.TweakId, StringComparer.OrdinalIgnoreCase)
                    .Select(def =>
                    {
                        RegistryTweakInspectionResult inspection = RegistryOptimizer.InspectCatalogTweak(def.TweakId);
                        LedgerValidationResult restore = RegistryStateLedger.ResolveTrustedRestoreEntry(def.TweakId);
                        return new
                        {
                            tweakId = def.TweakId,
                            state = inspection.State,
                            isUndeterminable = inspection.IsUndeterminable,
                            currentValue = inspection.CurrentValue,
                            actualValueKind = inspection.ActualValueKind?.ToString(),
                            expectedValueKind = def.ValueKind.ToString(),
                            absenceKind = inspection.AbsenceKind,
                            diagnosticDetail = inspection.DiagnosticDetail,
                            recommendedValue = def.RecommendedValue,
                            canApply = string.Equals(inspection.State, "Inativo", StringComparison.OrdinalIgnoreCase)
                                && !inspection.IsUndeterminable,
                            canRestore = string.Equals(inspection.State, "Ativo", StringComparison.OrdinalIgnoreCase)
                                && restore.IsValid
                        };
                    })
                    .ToArray();

                await SendJsonAsync(res, new { success = true, count = inspections.Length, inspections });
                return;
            }

            // Mutating Routes Protected by Mutual Exclusion Lock and Cryptographic HMAC-SHA256 Verification
            if (req.HttpMethod == "POST")
            {
                await _mutationLock.WaitAsync(cancellationToken);
                try
                {
                    var body = await ReadBoundedBodyAsync(req, cancellationToken);

                    // Etapa 8: Verify cryptographic HMAC-SHA256 request signature for native WPF calls (and whenever X-PCControl-Signature is supplied)
                    if (string.IsNullOrWhiteSpace(origin) || !string.IsNullOrWhiteSpace(signatureHeader))
                    {
                        string pathAndQuery = req.Url?.PathAndQuery ?? path;
                        var (sigValid, sigErr) = ValidateRequestHmacSignature(
                            req.HttpMethod,
                            pathAndQuery,
                            timestampHeader,
                            nonceHeader,
                            body,
                            signatureHeader
                        );
                        if (!sigValid)
                        {
                            WriteAuditLog("SECURITY_REJECT_HMAC", $"Blocked {req.HttpMethod} {pathAndQuery}: {sigErr}");
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                error = sigErr ?? "INVALID_HMAC_SIGNATURE",
                                message = "Assinatura criptográfica HMAC-SHA256 da requisição ausente ou inválida."
                            }, 403);
                            return;
                        }
                    }

                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

                    if (path == "/api/software/install")
                    {
                        string packageId = doc.RootElement.TryGetProperty("packageId", out var packageIdEl)
                            ? (packageIdEl.GetString() ?? "").Trim()
                            : "";
                        bool userConfirmed = doc.RootElement.TryGetProperty("userConfirmed", out var confirmEl)
                            && confirmEl.ValueKind == JsonValueKind.True;

                        if (string.IsNullOrWhiteSpace(packageId) || !userConfirmed)
                        {
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                error = "EXPLICIT_INSTALL_CONFIRMATION_REQUIRED",
                                message = "Selecione um aplicativo do catálogo oficial e confirme a instalação antes de continuar."
                            }, 400);
                            return;
                        }

                        var installResult = await _softwareCenter.InstallPackageAsync(
                            packageId,
                            userConfirmed: true,
                            source: "winget",
                            cancellationToken: cancellationToken);

                        WriteAuditLog("SOFTWARE_INSTALL",
                            $"Package={installResult.PackageId} Success={installResult.Success} ExitCode={installResult.ExitCode} Status={installResult.Status}");

                        await SendJsonAsync(res, new
                        {
                            success = installResult.Success,
                            packageId = installResult.PackageId,
                            status = installResult.Status,
                            exitCode = installResult.ExitCode,
                            message = installResult.OutputLog,
                            signatureState = installResult.SignatureState.ToString()
                        }, installResult.Success ? 200 : 400);
                        return;
                    }

                    // CRITICAL SECURITY FIX: /api/tweaks/apply ONLY accepts a cataloged tweakId!
                    if (path == "/api/tweaks/apply")
                    {
                        string tweakId = doc.RootElement.TryGetProperty("tweakId", out var tidEl) ? (tidEl.GetString() ?? "") : "";
                        string? reqVal = doc.RootElement.TryGetProperty("targetValue", out var tvEl) && tvEl.ValueKind != JsonValueKind.Null
                            ? tvEl.ToString()
                            : null;

                        if (string.IsNullOrWhiteSpace(tweakId) || !RegistryOptimizer.Catalog.ContainsKey(tweakId))
                        {
                            WriteAuditLog("SECURITY_BLOCK_BYPASS", $"Rejected tweak apply with uncataloged tweakId='{tweakId}'");
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                error = "CATALOG_TWEAK_ID_REQUIRED",
                                message = "Alterações genéricas no Registro são proibidas. Informe exclusivamente um tweakId existente no catálogo fechado."
                            }, 400);
                            return;
                        }

                        var opResult = RegistryOptimizer.ApplyCatalogTweak(tweakId, reqVal);
                        WriteAuditLog("TWEAK_APPLY", $"Tweak={tweakId} OpId={opResult.OperationId} Success={opResult.Success} Verified={opResult.VerifiedValue} Error={opResult.ErrorMessage}");

                        await SendJsonAsync(res, new
                        {
                            success = opResult.Success,
                            operationId = opResult.OperationId,
                            tweakId = opResult.TweakId,
                            previousValue = opResult.PreviousValue ?? "",
                            verifiedValue = opResult.VerifiedValue ?? "",
                            valueKind = opResult.ValueKind,
                            wasInitiallyAbsent = opResult.WasInitiallyAbsent,
                            keyCreatedByApp = opResult.KeyCreatedByApp,
                            error = opResult.ErrorMessage
                        });
                        return;
                    }

                    // CRITICAL SECURITY FIX (Etapa 2): /api/tweaks/restore NEVER trusts client-supplied originalValue, valueKind, wasInitiallyAbsent, or keyCreatedByApp!
                    // Restoration uses EXCLUSIVELY the backend's DPAPI+HMAC RegistryStateLedger or a verified backend snapshot.
                    if (path == "/api/tweaks/restore")
                    {
                        string tweakId = doc.RootElement.TryGetProperty("tweakId", out var tidEl) ? (tidEl.GetString() ?? "") : "";
                        string? operationId = doc.RootElement.TryGetProperty("operationId", out var opEl) ? opEl.GetString() : null;
                        string? snapshotId = doc.RootElement.TryGetProperty("snapshotId", out var snEl)
                            ? snEl.GetString()
                            : (doc.RootElement.TryGetProperty("snapshotPath", out var spEl) ? spEl.GetString() : null);

                        if (string.IsNullOrWhiteSpace(tweakId) || !RegistryOptimizer.Catalog.ContainsKey(tweakId))
                        {
                            WriteAuditLog("SECURITY_BLOCK_RESTORE", $"Rejected tweak restore for uncataloged item tweakId='{tweakId}'");
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                verified = false,
                                error = "Operação bloqueada: a restauração deve referenciar um tweakId válido do catálogo fechado."
                            }, 400);
                            return;
                        }

                        // Reject any attempt by the client to inject arbitrary originalValue / valueKind / wasInitiallyAbsent / keyCreatedByApp
                        bool clientSentUntrustedFields =
                            (doc.RootElement.TryGetProperty("originalValue", out var ovEl) && ovEl.ValueKind != JsonValueKind.Null) ||
                            (doc.RootElement.TryGetProperty("valueKind", out var vkEl) && vkEl.ValueKind != JsonValueKind.Null) ||
                            (doc.RootElement.TryGetProperty("wasInitiallyAbsent", out var waEl) && waEl.ValueKind != JsonValueKind.Null) ||
                            (doc.RootElement.TryGetProperty("keyCreatedByApp", out var kcEl) && kcEl.ValueKind != JsonValueKind.Null);

                        if (clientSentUntrustedFields)
                        {
                            WriteAuditLog("SECURITY_BLOCK_CLIENT_RESTORE_PAYLOAD",
                                $"Blocked client attempt to supply untrusted originalValue/valueKind/wasInitiallyAbsent for tweakId='{tweakId}'");
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                verified = false,
                                error = "CLIENT_ORIGINAL_STATE_FORBIDDEN",
                                message = "Violação de segurança bloqueada: o backend não aceita originalValue, valueKind, wasInitiallyAbsent ou keyCreatedByApp enviados pelo cliente. A restauração utiliza exclusivamente o Ledger DPAPI/HMAC do backend ou um Snapshot verificado."
                            }, 400);
                            return;
                        }

                        var restoreRes = RegistryOptimizer.RestoreCatalogTweakFromTrustedSource(tweakId, operationId, snapshotId);
                        WriteAuditLog("TWEAK_RESTORE", $"Tweak={tweakId} OpId={restoreRes.OperationId ?? operationId} Success={restoreRes.Success} Verified={restoreRes.VerifiedValue}");

                        await SendJsonAsync(res, new
                        {
                            success = restoreRes.Success,
                            verified = restoreRes.Success,
                            operationId = restoreRes.OperationId ?? operationId,
                            tweakId = restoreRes.TweakId,
                            verifiedValue = restoreRes.VerifiedValue,
                            error = restoreRes.ErrorMessage
                        }, restoreRes.Success ? 200 : 400);
                        return;
                    }

                    if (path == "/api/snapshot/create")
                    {
                        string desc = doc.RootElement.TryGetProperty("description", out var dEl) && !string.IsNullOrWhiteSpace(dEl.GetString())
                            ? dEl.GetString()!
                            : "Backup dos Ajustes Suportados pelo Aplicativo";
                        var snapshotResult = RegistrySnapshotEngine.CreateBackup(desc);
                        bool snapshotSuccess = false;
                        try
                        {
                            using var snapshotJson = JsonDocument.Parse(JsonSerializer.Serialize(snapshotResult));
                            snapshotSuccess = snapshotJson.RootElement.TryGetProperty("success", out var successElement) &&
                                successElement.ValueKind == JsonValueKind.True;
                        }
                        catch (JsonException) { }

                        WriteAuditLog("SNAPSHOT_CREATE",
                            $"Registry backup creation completed. Success={snapshotSuccess}.");
                        await SendJsonAsync(res, snapshotResult, snapshotSuccess ? 200 : 500);
                        return;
                    }

                    if (path == "/api/snapshot/restore")
                    {
                        string filePath = doc.RootElement.TryGetProperty("filePath", out var fpEl)
                            ? (fpEl.GetString() ?? "")
                            : (doc.RootElement.TryGetProperty("snapshotId", out var idEl) ? (idEl.GetString() ?? "") : "");
                        bool confirmIncomplete = doc.RootElement.TryGetProperty("confirmIncompleteBackup", out var ciEl) && ciEl.GetBoolean();
                        string? singleTweakId = doc.RootElement.TryGetProperty("tweakId", out var stEl) ? stEl.GetString() : null;
                        bool rollbackOnFail = doc.RootElement.TryGetProperty("rollbackOnPartialFailure", out var rbEl) && rbEl.GetBoolean();

                        var report = RegistrySnapshotEngine.RestoreFromBackupWithReport(
                            filePath,
                            confirmIncomplete,
                            singleTweakId,
                            rollbackOnFail
                        );
                        WriteAuditLog("SNAPSHOT_RESTORE",
                            $"File={filePath} TweakFilter={singleTweakId ?? "ALL"} Success={report.Success} Restored={report.RestoredCount} Failed={report.FailedCount} RolledBack={report.RolledBack}");

                        await SendJsonAsync(res, new
                        {
                            success = report.Success,
                            partialSuccess = report.PartialSuccess,
                            rolledBack = report.RolledBack,
                            restoredKeysCount = report.RestoredCount,
                            failedKeysCount = report.FailedCount,
                            itemReports = report.ItemReports,
                            error = report.ErrorMessage
                        }, report.Success ? 200 : report.PartialSuccess ? 207 : 400);
                        return;
                    }

                    if (path == "/api/debloat/purge")
                    {
                        string categoryKey = doc.RootElement.TryGetProperty("categoryKey", out var ckEl) ? (ckEl.GetString() ?? "") : "";
                        bool confirmUpdate = doc.RootElement.TryGetProperty("confirmUpdateCache", out var cuEl) && cuEl.GetBoolean();
                        var purgeReport = DiskCleanerEngine.PurgeCategoryWithReport(categoryKey, confirmUpdate);

                        WriteAuditLog("DEBLOAT_PURGE", $"Category={categoryKey} FreedBytes={purgeReport.FreedBytes} DeletedFiles={purgeReport.DeletedFilesCount}");

                        await SendJsonAsync(res, new
                        {
                            success = purgeReport.Success,
                            partialSuccess = purgeReport.PartialSuccess,
                            freedBytes = purgeReport.FreedBytes,
                            deletedFilesCount = purgeReport.DeletedFilesCount,
                            skippedLockedCount = purgeReport.SkippedLockedCount,
                            skippedSymlinksCount = purgeReport.SkippedSymlinksCount,
                            affectedItemsLog = purgeReport.AffectedItemsLog,
                            errors = purgeReport.Errors,
                            status = purgeReport.Status,
                            directoryExisted = purgeReport.DirectoryExisted,
                            warnings = purgeReport.Warnings
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
                                exitCode = captureRes.ExitCode,
                                error = captureRes.ErrorMessage ?? "Captura PresentMon indisponível."
                            }, 400);
                            return;
                        }

                        await SendJsonAsync(res, new
                        {
                            success = true,
                            processName = captureRes.ProcessName,
                            presentMonVersion = captureRes.PresentMonVersion,
                            exitCode = captureRes.ExitCode,
                            avgFps = captureRes.Summary.AverageFps,
                            minFps = captureRes.Summary.MinFps,
                            onePercentLow = captureRes.Summary.OnePercentLowFps,
                            frametimeAvgMs = captureRes.Summary.AverageFrametimeMs,
                            frametimeStDevMs = captureRes.Summary.FrametimeStandardDeviationMs,
                            capturedFrames = captureRes.Summary.TotalFramesCount,
                            recentFrameTimesMs = captureRes.Summary.RecentFrameTimesMs,
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
        catch (RequestBodyTooLargeException)
        {
            await SendJsonAsync(res, new
            {
                error = "PAYLOAD_TOO_LARGE",
                message = "Payload exceeds 64 KB limit."
            }, 413);
        }
        catch (JsonException)
        {
            await SendJsonAsync(res, new { error = "MALFORMED_JSON_PAYLOAD" }, 400);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Request timeout or server shutdown is an expected cancellation path.
            // Do not misreport it as an internal server error or attempt to write
            // a response after the listener/request has already been aborted.
            try { res.Abort(); } catch { }
        }
        catch (Exception ex)
        {
            WriteAuditLog("SERVER_ERROR", ex.Message);
            await SendJsonAsync(res, new { error = "INTERNAL_AGENT_ERROR", message = ex.Message }, 500);
        }
    }

    private bool CheckRateLimit()
    {
        lock (_rateLimitLock)
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
                throw new RequestBodyTooLargeException();

            ms.Write(buffer, 0, read);
        }

        return req.ContentEncoding.GetString(ms.ToArray());
    }

    public bool ValidateBearerToken(string? authHeaderOrToken)
    {
        if (string.IsNullOrWhiteSpace(authHeaderOrToken)) return false;

        string candidate = authHeaderOrToken.Trim();
        if (candidate.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate.Substring(7).Trim();
        }

        if (string.IsNullOrWhiteSpace(candidate)) return false;

        byte[] candidateBytes = Encoding.UTF8.GetBytes(candidate);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(SessionToken);

        if (candidateBytes.Length != expectedBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }

    private bool IsAuthenticated(HttpListenerRequest req)
    {
        string? authHeader = req.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(authHeader))
            return false;

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        return ValidateBearerToken(authHeader);
    }

    private void WriteAuditLog(string action, string message)
    {
        string line = $"{DateTime.UtcNow:O} [{action}] {message}";
        _recentAuditEvents.Enqueue(line);
        while (_recentAuditEvents.Count > 200)
        {
            _recentAuditEvents.TryDequeue(out _);
        }

        try
        {
            string? dir = Path.GetDirectoryName(AuditLogFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(AuditLogFilePath, line + Environment.NewLine, Encoding.UTF8);
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
        lock (_lifecycleLock)
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
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            try
            {
                _cts?.Cancel();
                if (_listener.IsListening) _listener.Stop();
            }
            catch { }

            _disposed = true;
            _listener.Close();
            WriteAuditLog("SERVER_DISPOSE", "PrivilegedAgentServer disposed.");
            // Request handlers may still be unwinding and releasing/waiting on these
            // shared synchronization primitives. Do not dispose them while requests
            // are in flight; the server and its managed primitives can be collected
            // together after the outstanding handlers complete.
        }
    }
}