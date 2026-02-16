using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SensorPal.Mobile.Infrastructure;

public sealed class ConnectivityService(
    IOptions<ServerConfig> config,
    ILogger<ConnectivityService> logger) : IDisposable
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly string _statusUrl = $"{config.Value.BaseUrl}/status";
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    CancellationTokenSource? _cts;
    bool _disposed;

    static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(60);

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    public bool IsServerReachable { get; private set; } = true;

    public event Action<bool>? ConnectivityChanged;

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
        try
        {
            using var response = await _http.GetAsync(_statusUrl);
            reachable = response.IsSuccessStatusCode;
        }
        catch
        {
            reachable = false;
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
            logger.LogInformation("Server connectivity restored");
        else
            logger.LogWarning("Server unreachable");

        ConnectivityChanged?.Invoke(reachable);
    }

    async Task RunLoopAsync(CancellationToken ct)
    {
        logger.LogInformation("Connectivity monitor started ({Interval} min interval)",
            (int)CheckInterval.TotalMinutes);
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
