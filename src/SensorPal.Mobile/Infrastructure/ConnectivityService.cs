using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SensorPal.Mobile.Extensions;

namespace SensorPal.Mobile.Infrastructure;

public sealed class ConnectivityService(
    IOptions<ServerConfig> config, ILogger<ConnectivityService> logger) : IDisposable
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    readonly IOptions<ServerConfig> _config = config;
    readonly ILogger<ConnectivityService> _logger = logger;

    CancellationTokenSource? _cts;
    bool _disposed;

    static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(60);

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    public bool IsServerReachable { get; private set; } = true;

    public event Action<bool>? ConnectivityChanged;

    string BaseUrl
    {
        get
        {
            var saved = Preferences.Get(PreferencesKeys.ServerUrl, "");
            return saved.HasContent ? saved : _config.Value.BaseUrl;
        }
    }
    string StatusUrl => $"{BaseUrl}/status";

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public void Start()
    {
        if (_cts is { }) { return; }

        _cts = new();
        _ = RunLoopAsync(_cts.Token);
    }

    public async Task<bool> CheckNowAsync()
    {
        bool reachable;
        _logger.LogDebug("Pinging {Url}", StatusUrl);
        try
        {
            using var response = await _http.GetAsync(StatusUrl);
            reachable = response.IsSuccessStatusCode;
            _logger.LogDebug("Ping result: {StatusCode} → reachable={Reachable}",
                (int)response.StatusCode, reachable);
        }
        catch (Exception ex)
        {
            reachable = false;
            _logger.LogDebug("Ping failed: {Error}", ex.Message);
        }

        UpdateState(reachable);
        return reachable;
    }

    public void ReportResult(bool reachable) => UpdateState(reachable);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _http.Dispose();
    }

    void UpdateState(bool reachable)
    {
        if (reachable == IsServerReachable) return;
        IsServerReachable = reachable;

        if (reachable)
            _logger.LogInformation("Server connectivity restored");
        else
            _logger.LogWarning("Server unreachable");

        ConnectivityChanged?.Invoke(reachable);
    }

    async Task RunLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Connectivity monitor started — pinging {Url} every {Interval} min",
            StatusUrl, (int)CheckInterval.TotalMinutes);
        await CheckNowAsync();

        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await CheckNowAsync();
        }
        catch (OperationCanceledException) { }
    }
}
