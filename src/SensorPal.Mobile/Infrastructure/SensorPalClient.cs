using Microsoft.Extensions.Options;

namespace SensorPal.Mobile.Infrastructure;

public sealed class SensorPalClient(IOptions<ServerConfig> config)
{
    readonly HttpClient _http = new();
    readonly ServerConfig _config = config.Value;

    public async Task<string> GetStatusAsync()
        => await _http.GetStringAsync($"{_config.BaseUrl}/status");
}
