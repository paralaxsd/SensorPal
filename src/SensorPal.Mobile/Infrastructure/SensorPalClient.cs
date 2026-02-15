using Microsoft.Extensions.Options;
using SensorPal.Shared.Models;
using System.Net.Http.Json;

namespace SensorPal.Mobile.Infrastructure;

public sealed class SensorPalClient(IOptions<ServerConfig> config)
{
    readonly HttpClient _http = new();
    readonly ServerConfig _config = config.Value;

    public async Task<string> GetStatusAsync()
        => await _http.GetStringAsync($"{_config.BaseUrl}/status");

    public async Task<IReadOnlyList<NoiseEventDto>> GetEventsAsync(DateOnly date)
        => await _http.GetFromJsonAsync<List<NoiseEventDto>>(
               $"{_config.BaseUrl}/events?date={date:yyyy-MM-dd}")
           ?? [];

    public async Task<IReadOnlyList<MonitoringSessionDto>> GetSessionsAsync()
        => await _http.GetFromJsonAsync<List<MonitoringSessionDto>>(
               $"{_config.BaseUrl}/monitoring/sessions")
           ?? [];

    public string GetEventAudioUrl(long eventId)
        => $"{_config.BaseUrl}/events/{eventId}/audio";

    public Task StartMonitoringAsync()
        => _http.PostAsync($"{_config.BaseUrl}/monitoring/start", null);

    public Task StopMonitoringAsync()
        => _http.PostAsync($"{_config.BaseUrl}/monitoring/stop", null);
}
