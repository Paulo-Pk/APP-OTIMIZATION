using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PCControlUltimate.Security;

namespace PCControlUltimate.Infrastructure.Services;

public class AgentClientService : IDisposable
{
    private readonly HttpClient _httpClient;
    private string? _sessionToken;
    public const string AgentBaseUrl = "http://127.0.0.1:52194";
    public const string LocalClientIdentifier = "WPF-Native-v2.4";

    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool IsElevated { get; private set; }
    public bool IsTokenDpapiProtected { get; private set; }
    public bool IsTokenAclRestricted { get; private set; }
    public string AgentVersion { get; private set; } = "N/D";
    public string OperatingSystem { get; private set; } = "Windows 10/11";

    private static string ProtectedTokenFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCControlUltimate",
        "agent_session.dpapi"
    );

    public AgentClientService(string? inMemorySessionToken = null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AgentBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (!string.IsNullOrWhiteSpace(inMemorySessionToken))
        {
            SetSessionToken(inMemorySessionToken);
        }
        else
        {
            LoadTokenFromDpapi();
        }
    }

    public void SetSessionToken(string token)
    {
        _sessionToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
    }

    private void LoadTokenFromDpapi()
    {
        string? token = IntegrityVerifier.ReadProtectedSessionToken(ProtectedTokenFilePath);
        if (!string.IsNullOrWhiteSpace(token))
        {
            SetSessionToken(token);
        }
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativePath, object? jsonPayload = null)
    {
        var req = new HttpRequestMessage(method, relativePath);
        if (!string.IsNullOrWhiteSpace(_sessionToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
        }

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string nonce = Guid.NewGuid().ToString("N");
        string? bodyString = jsonPayload != null ? JsonSerializer.Serialize(jsonPayload) : null;

        // Attach mandatory local client proof, anti-replay headers, and cryptographic HMAC-SHA256 signature
        req.Headers.TryAddWithoutValidation("X-PCControl-Client", LocalClientIdentifier);
        req.Headers.TryAddWithoutValidation("X-PCControl-Timestamp", timestamp);
        req.Headers.TryAddWithoutValidation("X-PCControl-Nonce", nonce);

        if (!string.IsNullOrWhiteSpace(_sessionToken))
        {
            string signature = IntegrityVerifier.ComputeRequestHmacSha256(
                _sessionToken,
                method.Method,
                relativePath,
                timestamp,
                nonce,
                bodyString
            );
            req.Headers.TryAddWithoutValidation("X-PCControl-Signature", signature);
        }

        if (bodyString != null)
        {
            req.Content = new StringContent(bodyString, Encoding.UTF8, "application/json");
        }

        return req;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                LoadTokenFromDpapi();
            }

            using var req = CreateAuthenticatedRequest(HttpMethod.Get, "/api/status");
            using var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);

                IsElevated = doc.RootElement.TryGetProperty("isElevated", out var el) && el.GetBoolean();
                IsAuthenticated = doc.RootElement.TryGetProperty("isAuthenticated", out var authEl) && authEl.GetBoolean();
                IsTokenDpapiProtected = doc.RootElement.TryGetProperty("isTokenDpapiProtected", out var dpEl) && dpEl.GetBoolean();
                IsTokenAclRestricted = doc.RootElement.TryGetProperty("isTokenAclRestricted", out var aclEl) && aclEl.GetBoolean();
                AgentVersion = doc.RootElement.TryGetProperty("version", out var verEl) ? (verEl.GetString() ?? "2.5.0") : "2.5.0";
                OperatingSystem = doc.RootElement.TryGetProperty("os", out var os) ? (os.GetString() ?? OperatingSystem) : OperatingSystem;

                IsConnected = IsAuthenticated;
                return IsConnected;
            }
        }
        catch
        {
            // Agent unreachable
        }

        IsConnected = false;
        IsAuthenticated = false;
        return false;
    }

    public async Task<string?> GetHardwareTelemetryAsync()
    {
        if (!IsConnected) return null;
        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Get, "/api/telemetry");
            using var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync();
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Applies a tweak strictly by its authorized catalog ID.
    /// Generic keyPath/valueName modification is prohibited.
    /// </summary>
    public async Task<(bool success, string details)> ApplyCatalogTweakAsync(string tweakId, string? targetValue = null)
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tweaks/apply", new { tweakId, targetValue });
            using var res = await _httpClient.SendAsync(req);
            string responseText = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (false, responseText);

            using var doc = JsonDocument.Parse(responseText);
            bool ok = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            return (ok, responseText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Restores a tweak strictly by its authorized catalog ID using the backend's trusted DPAPI/HMAC ledger or a verified snapshot.
    /// Never sends untrusted originalValue, valueKind, wasInitiallyAbsent, or keyCreatedByApp from the client.
    /// </summary>
    public async Task<(bool success, string details)> RestoreCatalogTweakAsync(
        string tweakId,
        string? operationId = null,
        string? snapshotId = null)
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            var payload = new
            {
                tweakId,
                operationId,
                snapshotId
            };
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tweaks/restore", payload);
            using var res = await _httpClient.SendAsync(req);
            string responseText = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (false, responseText);

            using var doc = JsonDocument.Parse(responseText);
            bool ok = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            return (ok, responseText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool success, string details)> CreateSnapshotAsync(string? description = null)
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/snapshot/create", new { description });
            using var res = await _httpClient.SendAsync(req);
            string responseText = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (false, responseText);

            using var doc = JsonDocument.Parse(responseText);
            bool ok = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            return (ok, responseText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool success, string details)> RestoreSnapshotAsync(
        string filePath,
        bool confirmIncompleteBackup = false,
        string? singleTweakId = null,
        bool rollbackOnPartialFailure = true)
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            var payload = new
            {
                filePath,
                confirmIncompleteBackup,
                tweakId = singleTweakId,
                rollbackOnPartialFailure
            };
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/snapshot/restore", payload);
            using var res = await _httpClient.SendAsync(req);
            string responseText = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (false, responseText);

            using var doc = JsonDocument.Parse(responseText);
            bool ok = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            return (ok, responseText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
