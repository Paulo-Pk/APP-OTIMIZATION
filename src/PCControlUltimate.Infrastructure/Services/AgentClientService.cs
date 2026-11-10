using System;
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

    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool IsElevated { get; private set; }
    public string AgentVersion { get; private set; } = "Não detectado";
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
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Add("X-PCControl-Client", "WPF-Native-v2.4");

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

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                LoadTokenFromDpapi();
            }

            var res = await _httpClient.GetAsync("/api/status");
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);

                IsElevated = doc.RootElement.TryGetProperty("isElevated", out var el) && el.GetBoolean();
                IsAuthenticated = doc.RootElement.TryGetProperty("isAuthenticated", out var authEl) && authEl.GetBoolean();
                AgentVersion = doc.RootElement.TryGetProperty("version", out var verEl) ? (verEl.GetString() ?? "2.4.0") : "2.4.0";
                OperatingSystem = doc.RootElement.TryGetProperty("os", out var os) ? (os.GetString() ?? OperatingSystem) : OperatingSystem;

                // Only report IsConnected = true when the session is both reachable AND authenticated!
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
            var res = await _httpClient.GetAsync("/api/telemetry");
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync();
            }
        }
        catch { }
        return null;
    }

    public async Task<(bool success, string details)> ApplyCatalogTweakAsync(string tweakId, string? targetValue = null)
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            var payload = new { tweakId, targetValue };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await _httpClient.PostAsync("/api/tweaks/apply", content);
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

    public async Task<(bool success, string details)> ApplyTweakAsync(string keyPath, string valueName, int targetValue)
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            var payload = new { keyPath, valueName, targetValue };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await _httpClient.PostAsync("/api/tweaks/apply", content);
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

    public async Task<(bool success, string details)> CreateSnapshotAsync()
    {
        if (!IsConnected) return (false, "Agente não autenticado ou desconectado.");
        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var res = await _httpClient.PostAsync("/api/snapshot/create", content);
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
