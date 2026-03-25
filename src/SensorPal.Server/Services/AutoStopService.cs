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
    DateTime? _scheduledStop;

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
        if (!stateService.IsMonitoring)
        {
            _scheduledStop = null;
            return;
        }

        var s = await settings.GetAsync();

        if (s.AutoStopTime is not { } rawTime) return;
        if (!TimeOnly.TryParse(rawTime, out var stopTime)) return;

        var now = DateTime.Now;

        if (_scheduledStop is null)
        {
            // Compute the next future occurrence of stopTime from now.
            // This correctly handles overnight monitoring: starting at 23:48 with a
            // 06:30 stop time schedules the stop for tomorrow, not today.
            var candidate = now.Date.Add(stopTime.ToTimeSpan());
            if (candidate <= now) candidate = candidate.AddDays(1);

            _scheduledStop = candidate;

            logger.LogInformation(
                "AutoStopService: armed — will stop monitoring at {Stop}",
                _scheduledStop.Value.ToString("yyyy-MM-dd HH:mm"));
        }

        if (now < _scheduledStop) return;

        logger.LogInformation(
            "AutoStopService: local time {Now} >= stop time {Stop} — stopping monitoring",
            now.ToString("HH:mm"), rawTime);

        stateService.Stop();
        _scheduledStop = null;
    }
}
