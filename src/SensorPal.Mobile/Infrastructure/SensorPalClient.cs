using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SensorPal.Shared.Models;
using System.Net.Http.Json;

namespace SensorPal.Mobile.Infrastructure;

public sealed class SensorPalClient(
    IOptions<ServerConfig> config,
    ConnectivityService connectivity,
    ILogger<SensorPalClient> logger)
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly string _base = config.Value.BaseUrl;

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
        try { return await _http.GetFromJsonAsync<LiveLevelDto>($"{_base}/monitoring/level"); }
        catch { return null; }
    }

    public async Task<Stream> GetEventAudioAsync(long eventId)
    {
        var bytes = await ExecuteAsync(
            () => _http.GetByteArrayAsync($"{_base}/events/{eventId}/audio"));
        return new MemoryStream(bytes);
    }

    public async Task StartMonitoringAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{_base}/monitoring/start", null));
        logger.LogInformation("Monitoring started");
    }

    public async Task StopMonitoringAsync()
    {
        await ExecuteAsync(() => _http.PostAsync($"{_base}/monitoring/stop", null));
        logger.LogInformation("Monitoring stopped");
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

    async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            var result = await call();
            connectivity.ReportResult(true);
            return result;
        }
        catch (Exception ex)
        {
            connectivity.ReportResult(false);
            logger.LogError("HTTP request failed: {Message}", ex.Message);
            throw;
        }
    }
}
