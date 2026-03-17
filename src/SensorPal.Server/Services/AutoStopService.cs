using SensorPal.Server.Storage;

namespace SensorPal.Server.Services;

/// <summary>
/// Background service that automatically stops monitoring at a configured local time.
/// Checks once per minute; stops monitoring when the current local time reaches
/// <c>AppSettings.AutoStopTime</c> and the state is <see cref="MonitoringState.Monitoring"/>.
/// </summary>
sealed class AutoStopService(
    MonitoringStateService stateService,
    SettingsRepository settings,
    ILogger<AutoStopService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckAndStopAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AutoStopService encountered an error during check");
            }
        }
    }

    async Task CheckAndStopAsync()
    {
        var s = await settings.GetAsync();

        if (s.AutoStopTime is not { } rawTime) return;
        if (!TimeOnly.TryParse(rawTime, out var stopTime)) return;
        if (!stateService.IsMonitoring) return;

        var now = TimeOnly.FromDateTime(DateTime.Now);

        if (now < stopTime) return;

        logger.LogInformation(
            "AutoStopService: local time {Now} >= stop time {Stop} — stopping monitoring",
            now.ToString("HH:mm"), rawTime);

        stateService.Stop();
    }
}
