using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SensorPal.Shared.Extensions;
using SensorPal.Shared.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SensorPal.Mobile.Infrastructure;

public sealed class SensorPalClient
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly HttpClient _http;
    readonly string _configuredBaseUrl;
    readonly IOptions<ServerConfig> _config;
    readonly ConnectivityService _connectivity;
    readonly ILogger<SensorPalClient> _logger;

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    /// <summary>
    /// The default URL from appsettings — shown as placeholder in Settings.
    /// </summary>
    public string ConfiguredBaseUrl => _configuredBaseUrl;
    public bool IsAuthError { get; private set; }

    string BaseUrl
    {
        get
        {
            var saved = Preferences.Get(PreferencesKeys.ServerUrl, "");
            return saved.HasContent ? saved : _config.Value.BaseUrl;
        }
    }

    /******************************************************************************************
     * STRUCTORS
     * ***************************************************************************************/
    public SensorPalClient(
        IOptions<ServerConfig> config, ConnectivityService connectivity, 
            ILogger<SensorPalClient> logger)
    {
        _config = config;
        _connectivity = connectivity;
        _logger = logger;
        _configuredBaseUrl = config.Value.BaseUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var baseUrl = BaseUrl;
        _logger.LogInformation("SensorPalClient ready — base URL: {Url} (source: {Source})",
            baseUrl, baseUrl == _configuredBaseUrl ? "appsettings" : "saved preferences");

        var key = Preferences.Get(PreferencesKeys.ApiKey, "");
        if (key.HasContent)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public void SetBaseUrl(string? url)
    {
        var trimmed = url?.Trim() ?? "";

        if(Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Host is "0.0.0.0")
        {
            _logger.LogWarning("The host '0.0.0.0' cannot be used as a target name. " + 
                "Using 'localhost' as a reasonable fallback.");

            var builder = new UriBuilder(uri) { Host = "localhost" };
            trimmed = builder.Uri.ToString().TrimEnd('/');
        }

        Preferences.Set(PreferencesKeys.ServerUrl, trimmed);
    }

    public void SetApiKey(string? key)
    {
        IsAuthError = false;
        Preferences.Set(PreferencesKeys.ApiKey, key ?? "");
        _http.DefaultRequestHeaders.Authorization = key is { Length: > 0 }
            ? new AuthenticationHeaderValue("Bearer", key)
            : null;
    }

    public Task<string> GetStatusAsync()
        => ExecuteAsync(() => _http.GetStringAsync($"{BaseUrl}/status"));

    public async Task<IReadOnlyList<NoiseEventDto>> GetEventsAsync(DateOnly date)
    {
        var list = await ExecuteAsync(
            () => _http.GetFromJsonAsync<List<NoiseEventDto>>(
                $"{BaseUrl}/events?date={date:yyyy-MM-dd}"));
        return list ?? [];
    }

    public async Task<IReadOnlyList<MonitoringSessionDto>> GetSessionsAsync()
    {
        var list = await ExecuteAsync(
            () => _http.GetFromJsonAsync<List<MonitoringSessionDto>>(
                $"{BaseUrl}/monitoring/sessions"));
        return list ?? [];
    }

    public async Task<LiveLevelDto?> GetLevelAsync()
    {
        try { return await ExecuteAsync(() => _http.GetFromJsonAsync<LiveLevelDto>($"{BaseUrl}/monitoring/level")); }
        catch { return null; }
    }

    public string GetSessionAudioUrl(long sessionId)
    {
        var key = Preferences.Get(PreferencesKeys.ApiKey, "");
        var tokenSuffix = key.HasContent ? $"?token={Uri.EscapeDataString(key)}" : "";
        return $"{BaseUrl}/monitoring/{sessionId}/audio{tokenSuffix}";
    }

    public async Task<IReadOnlyList<EventMarkerDto>> GetSessionMarkersAsync(long sessionId)
    {
        var list = await ExecuteAsync(
            () => _http.GetFromJsonAsync<List<EventMarkerDto>>(
                $"{BaseUrl}/monitoring/{sessionId}/markers"));
        return list ?? [];
    }

    public async Task<Stream> GetEventAudioAsync(long eventId)
    {
        var bytes = await ExecuteAsync(
            () => _http.GetByteArrayAsync($"{BaseUrl}/events/{eventId}/audio"));
        return new MemoryStream(bytes);
    }

    public async Task StartMonitoringAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{BaseUrl}/monitoring/start", null));
        _logger.LogInformation("Monitoring started");
    }

    public async Task StopMonitoringAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{BaseUrl}/monitoring/stop", null));
        _logger.LogInformation("Monitoring stopped");
    }

    public async Task StartCalibrationAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{BaseUrl}/monitoring/calibrate/start", null));
        _logger.LogInformation("Calibration started");
    }

    public async Task StopCalibrationAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{BaseUrl}/monitoring/calibrate/stop", null));
        _logger.LogInformation("Calibration stopped");
    }

    public async Task<SettingsDto?> GetSettingsAsync()
    {
        return await ExecuteAsync(
            () => _http.GetFromJsonAsync<SettingsDto>($"{BaseUrl}/settings"));
    }

    public async Task SaveSettingsAsync(SettingsDto dto)
    {
        await ExecuteAsync(() => _http.PutAsJsonAsync($"{BaseUrl}/settings", dto));
    }

    public async Task DeleteEventsByDateAsync(DateOnly date)
    {
        await ExecuteAsync(() => _http.DeleteAsync($"{BaseUrl}/events?date={date:yyyy-MM-dd}"));
    }

    public async Task DeleteSessionAsync(long id)
    {
        await ExecuteAsync(() => _http.DeleteAsync($"{BaseUrl}/monitoring/{id}"));
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
