using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SensorPal.Shared.Models;
using System.Net.Http.Json;

namespace SensorPal.Mobile.Infrastructure;

public sealed class SensorPalClient
{
    readonly HttpClient _http;
    readonly string _configuredBaseUrl;
    readonly ConnectivityService _connectivity;
    readonly ILogger<SensorPalClient> _logger;
    string _base;

    public SensorPalClient(
        IOptions<ServerConfig> config,
        ConnectivityService connectivity,
        ILogger<SensorPalClient> logger)
    {
        _connectivity = connectivity;
        _logger = logger;
        _configuredBaseUrl = config.Value.BaseUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var savedUrl = Preferences.Get("ServerUrl", "");
        _base = string.IsNullOrWhiteSpace(savedUrl) ? _configuredBaseUrl : savedUrl;

        var key = Preferences.Get("ApiKey", "");
        if (!string.IsNullOrEmpty(key))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    /// <summary>The default URL from appsettings — shown as placeholder in Settings.</summary>
    public string ConfiguredBaseUrl => _configuredBaseUrl;

    public bool IsAuthError { get; private set; }

    public void SetBaseUrl(string? url)
    {
        var trimmed = url?.Trim() ?? "";
        Preferences.Set("ServerUrl", trimmed);
        _base = string.IsNullOrWhiteSpace(trimmed) ? _configuredBaseUrl : trimmed;
        _connectivity.UpdateStatusUrl(_base);
    }

    public void SetApiKey(string? key)
    {
        IsAuthError = false;
        Preferences.Set("ApiKey", key ?? "");
        _http.DefaultRequestHeaders.Authorization = key is { Length: > 0 }
            ? new AuthenticationHeaderValue("Bearer", key)
            : null;
    }

    public Task<string> GetStatusAsync()
        => ExecuteAsync(() => _http.GetStringAsync($"{_base}/status"));

    public async Task<IReadOnlyList<NoiseEventDto>> GetEventsAsync(DateOnly date)
    {
        var list = await ExecuteAsync(
            () => _http.GetFromJsonAsync<List<NoiseEventDto>>(
                $"{_base}/events?date={date:yyyy-MM-dd}"));
        return list ?? [];
    }

    public async Task<IReadOnlyList<MonitoringSessionDto>> GetSessionsAsync()
    {
        var list = await ExecuteAsync(
            () => _http.GetFromJsonAsync<List<MonitoringSessionDto>>(
                $"{_base}/monitoring/sessions"));
        return list ?? [];
    }

    public async Task<LiveLevelDto?> GetLevelAsync()
    {
        try { return await ExecuteAsync(() => _http.GetFromJsonAsync<LiveLevelDto>($"{_base}/monitoring/level")); }
        catch { return null; }
    }

    public string GetSessionAudioUrl(long sessionId) => $"{_base}/monitoring/{sessionId}/audio";

    public async Task<Stream> GetEventAudioAsync(long eventId)
    {
        var bytes = await ExecuteAsync(
            () => _http.GetByteArrayAsync($"{_base}/events/{eventId}/audio"));
        return new MemoryStream(bytes);
    }

    public async Task StartMonitoringAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{_base}/monitoring/start", null));
        _logger.LogInformation("Monitoring started");
    }

    public async Task StopMonitoringAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{_base}/monitoring/stop", null));
        _logger.LogInformation("Monitoring stopped");
    }

    public async Task<SettingsDto?> GetSettingsAsync()
    {
        return await ExecuteAsync(
            () => _http.GetFromJsonAsync<SettingsDto>($"{_base}/settings"));
    }

    public async Task SaveSettingsAsync(SettingsDto dto)
    {
        await ExecuteAsync(() => _http.PutAsJsonAsync($"{_base}/settings", dto));
    }

    public async Task DeleteEventsByDateAsync(DateOnly date)
    {
        await ExecuteAsync(() => _http.DeleteAsync($"{_base}/events?date={date:yyyy-MM-dd}"));
    }

    async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            var result = await call();
            _connectivity.ReportResult(true);
            IsAuthError = false;
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // 401 means the server is reachable but the API key is wrong or missing.
            // Do NOT report offline — the user needs to navigate to Settings to fix the key.
            IsAuthError = true;
            _logger.LogWarning("API request unauthorized — open Settings and enter the API key");
            throw;
        }
        catch (Exception ex)
        {
            _connectivity.ReportResult(false);
            _logger.LogError("HTTP request failed: {Message}", ex.Message);
            throw;
        }
    }
}
