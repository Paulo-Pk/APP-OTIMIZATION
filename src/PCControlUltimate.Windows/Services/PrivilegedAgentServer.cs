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
using PCControlUltimate.Security;

namespace PCControlUltimate.Windows.Services;

public class PrivilegedAgentServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly IHardwareMonitorService? _hardwareMonitor;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly ConcurrentQueue<DateTime> _requestTimestamps = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _recentAuditEvents = new();
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
        "http://127.0.0.1:52194"
    };

    public PrivilegedAgentServer(int port = DefaultPort) : this(null, port)
    {
    }

    public PrivilegedAgentServer(IHardwareMonitorService? hardwareMonitor, int port = DefaultPort)
    {
        _hardwareMonitor = hardwareMonitor;
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
        if (_disposed) throw new ObjectDisposedException(nameof(PrivilegedAgentServer));
        _cts = new CancellationTokenSource();
        _listener.Start();
        WriteAuditLog("SERVER_START", $"Listening exclusively on http://127.0.0.1:{Port}/");
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
        if (!string.Equals(clientHeader?.Trim(), ExpectedLocalClientHeader, StringComparison.Ordinal))
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

    public bool ValidateTimestampAndNonce(string? timestampHeader, string? nonceHeader, out string? errorCode)
    {
        errorCode = null;
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(nonceHeader))
        {
            errorCode = "MISSING_ANTI_REPLAY_HEADERS";
            return false;
        }

        string cleanNonce = nonceHeader.Trim();
        if (cleanNonce.Length < 12 || cleanNonce.Length > 128)
        {
            errorCode = "INVALID_NONCE_FORMAT";
            return false;
        }

        DateTimeOffset requestTime;
        if (long.TryParse(timestampHeader.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds))
        {
            requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        else if (!DateTimeOffset.TryParse(timestampHeader.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out requestTime))
        {
            errorCode = "INVALID_TIMESTAMP_FORMAT";
            return false;
        }

        double skewSeconds = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
        if (skewSeconds > 60.0)
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
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-PCControl-Client, X-PCControl-Timestamp, X-PCControl-Nonce");

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
                    drivers
                });
                return;
            }

            if (path == "/api/network/diagnostics" && req.HttpMethod == "GET")
            {
                var netDiag = await NetworkDiagnosticsService.RunRealDiagnosticsAsync();
                await SendJsonAsync(res, netDiag);
                return;
            }

            if (path == "/api/network/throughput" && req.HttpMethod == "GET")
            {
                var throughput = await NetworkDiagnosticsService.MeasureExternalThroughputAsync(cancellationToken);
                await SendJsonAsync(res, throughput);
                return;
            }

            if (path == "/api/debloat/scan" && req.HttpMethod == "GET")
            {
                var d3d = DiskCleanerEngine.SimulateCategoryDryRun("cache-dx-shaders");
                var tempWin = DiskCleanerEngine.SimulateCategoryDryRun("temp-windows");
                var tempUser = DiskCleanerEngine.SimulateCategoryDryRun("temp-user");
                var softDist = DiskCleanerEngine.SimulateCategoryDryRun("update-cache");

                await SendJsonAsync(res, new
                {
                    isEstimateOnly = true,
                    d3dCacheBytes = d3d.EstimatedBytes,
                    tempWindowsBytes = tempWin.EstimatedBytes,
                    tempUserBytes = tempUser.EstimatedBytes,
                    softwareDistributionBytes = softDist.EstimatedBytes,
                    categories = new[] { d3d, tempWin, tempUser, softDist }
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
                await SendJsonAsync(res, new { success = true, isValid, checksum, error = err });
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
                    registryAuditLogs = RegistryOptimizer.GetAuditLogs()
                });
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

                    // CRITICAL SECURITY FIX: /api/tweaks/apply ONLY accepts a cataloged tweakId!
                    // Any attempt to pass arbitrary keyPath/valueName/valueKind without a valid catalog tweakId is strictly rejected.
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
                        WriteAuditLog("TWEAK_APPLY", $"Tweak={tweakId} Success={opResult.Success} Verified={opResult.VerifiedValue} Error={opResult.ErrorMessage}");

                        await SendJsonAsync(res, new
                        {
                            success = opResult.Success,
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

                    // CRITICAL SECURITY FIX: /api/tweaks/restore requires an authorized catalog tweakId (or resolves strictly to Catalog)
                    if (path == "/api/tweaks/restore")
                    {
                        string tweakId = doc.RootElement.TryGetProperty("tweakId", out var tidEl) ? (tidEl.GetString() ?? "") : "";
                        string subKey = doc.RootElement.TryGetProperty("keyPath", out var kpEl) ? (kpEl.GetString() ?? "") : "";
                        string valueName = doc.RootElement.TryGetProperty("valueName", out var vnEl) ? (vnEl.GetString() ?? "") : "";

                        if (string.IsNullOrWhiteSpace(tweakId) && !string.IsNullOrWhiteSpace(subKey) && !string.IsNullOrWhiteSpace(valueName))
                        {
                            tweakId = RegistryOptimizer.FindAuthorizedCatalogEntry(subKey, valueName)?.TweakId ?? "";
                        }

                        if (string.IsNullOrWhiteSpace(tweakId) || !RegistryOptimizer.Catalog.ContainsKey(tweakId))
                        {
                            WriteAuditLog("SECURITY_BLOCK_RESTORE", $"Rejected tweak restore for uncataloged item tweakId='{tweakId}'");
                            await SendJsonAsync(res, new
                            {
                                success = false,
                                verified = false,
                                error = "Operação bloqueada: a restauração deve referenciar um ajuste válido do catálogo oficial."
                            }, 400);
                            return;
                        }

                        string? origValStr = doc.RootElement.TryGetProperty("originalValue", out var ovEl) && ovEl.ValueKind != JsonValueKind.Null
                            ? ovEl.ToString()
                            : null;
                        string? valueKindStr = doc.RootElement.TryGetProperty("valueKind", out var vkEl) ? vkEl.GetString() : null;
                        bool wasAbsent = doc.RootElement.TryGetProperty("wasInitiallyAbsent", out var waEl) && waEl.GetBoolean();
                        bool keyCreated = doc.RootElement.TryGetProperty("keyCreatedByApp", out var kcEl) && kcEl.GetBoolean();

                        var restoreRes = RegistryOptimizer.RestoreCatalogTweak(tweakId, origValStr, valueKindStr, wasAbsent, keyCreated);
                        WriteAuditLog("TWEAK_RESTORE", $"Tweak={tweakId} Success={restoreRes.Success} WasAbsent={wasAbsent}");

                        await SendJsonAsync(res, new
                        {
                            success = restoreRes.Success,
                            verified = restoreRes.Success,
                            tweakId = restoreRes.TweakId,
                            verifiedValue = restoreRes.VerifiedValue,
                            error = restoreRes.ErrorMessage
                        });
                        return;
                    }

                    if (path == "/api/snapshot/create")
                    {
                        string desc = doc.RootElement.TryGetProperty("description", out var dEl) && !string.IsNullOrWhiteSpace(dEl.GetString())
                            ? dEl.GetString()!
                            : "Backup dos Ajustes Suportados pelo Aplicativo";
                        var snapshotResult = RegistrySnapshotEngine.CreateBackup(desc);
                        WriteAuditLog("SNAPSHOT_CREATE", "Registry backup snapshot created and verified on disk.");
                        await SendJsonAsync(res, snapshotResult);
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
                        });
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
        catch (JsonException)
        {
            await SendJsonAsync(res, new { error = "MALFORMED_JSON_PAYLOAD" }, 400);
        }
        catch (Exception ex)
        {
            WriteAuditLog("SERVER_ERROR", ex.Message);
            await SendJsonAsync(res, new { error = "INTERNAL_AGENT_ERROR", message = ex.Message }, 500);
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