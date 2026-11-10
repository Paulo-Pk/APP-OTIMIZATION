using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCControlUltimate.Infrastructure.Services;

public class AgentClientService
{
    private readonly HttpClient _httpClient;
    private string? _sessionToken;
    public const string AgentBaseUrl = "http://127.0.0.1:52194";

    public bool IsConnected { get; private set; }
    public bool IsElevated { get; private set; }
    public string OperatingSystem { get; private set; } = "Windows 10/11";

    public AgentClientService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AgentBaseUrl),
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var res = await _httpClient.GetAsync("/api/status");
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(content);
                IsElevated = doc.RootElement.TryGetProperty("isElevated", out var el) && el.GetBoolean();
                OperatingSystem = doc.RootElement.TryGetProperty("os", out var os) ? (os.GetString() ?? OperatingSystem) : OperatingSystem;
                IsConnected = true;

                await FetchTokenAsync();
                return true;
            }
        }
        catch
        {
            // Agent not running or unreachable
        }

        IsConnected = false;
        return false;
    }

    private async Task FetchTokenAsync()
    {
        try
        {
            var res = await _httpClient.GetAsync("/api/auth/token");
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("token", out var tokenEl))
                {
                    _sessionToken = tokenEl.GetString();
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
                }
            }
        }
        catch { }
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

    public async Task<(bool success, string details)> ApplyTweakAsync(string keyPath, string valueName, int targetValue)
    {
        if (!IsConnected) return (false, "Agent offline");
        try
        {
            var payload = new { keyPath, valueName, targetValue };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await _httpClient.PostAsync("/api/tweaks/apply", content);
            string responseText = await res.Content.ReadAsStringAsync();
            return (res.IsSuccessStatusCode, responseText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool success, string details)> CreateSnapshotAsync()
    {
        if (!IsConnected) return (false, "Agent offline");
        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var res = await _httpClient.PostAsync("/api/snapshot/create", content);
            string responseText = await res.Content.ReadAsStringAsync();
            return (res.IsSuccessStatusCode, responseText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
