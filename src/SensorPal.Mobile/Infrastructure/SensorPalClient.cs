using Microsoft.Extensions.Options;
using SensorPal.Shared.Models;
using System.Net.Http.Json;

namespace SensorPal.Mobile.Infrastructure;

public sealed class SensorPalClient(IOptions<ServerConfig> config, ConnectivityService connectivity)
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

    public string GetEventAudioUrl(long eventId)
        => $"{_base}/events/{eventId}/audio";

    public Task StartMonitoringAsync()
        => ExecuteAsync(() => _http.PostAsync($"{_base}/monitoring/start", null));

    public Task StopMonitoringAsync()
        => ExecuteAsync(() => _http.PostAsync($"{_base}/monitoring/stop", null));

    async Task<T> ExecuteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            var result = await call();
            connectivity.ReportResult(true);
            return result;
        }
        catch
        {
            connectivity.ReportResult(false);
            throw;
        }
    }
}
