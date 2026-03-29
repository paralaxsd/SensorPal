using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Services;

// Explicit Java class name so the AndroidManifest.xml entry can reference
// a stable, predictable name without relying on generated MD5 identifiers.
[assembly: Android.App.UsesPermission(Android.Manifest.Permission.ForegroundService)]
[assembly: Android.App.UsesPermission(Android.Manifest.Permission.ForegroundServiceDataSync)]
[assembly: Android.App.UsesPermission("android.permission.POST_NOTIFICATIONS")]

namespace SensorPal.Mobile;

[Register("org.speckdrumm.sensorpal.mobile.SensorPalForegroundService")]
sealed class SensorPalForegroundService : Android.App.Service
{
    const string ChannelId = "sensorpal_monitoring";
    const int ServiceNotificationId = 1001;
    const string ActionStopTracking = "org.speckdrumm.sensorpal.mobile.ACTION_STOP_TRACKING";

    CancellationTokenSource? _cts;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent, StartCommandFlags flags, int startId)
    {
        // User tapped "Stop tracking" in the notification — stop the service
        // without touching the server or the IsEnabled preference so the
        // service resumes automatically on next app launch.
        if (intent?.Action == ActionStopTracking)
        {
            // Suppress further notifications from the UI poll loop, then clean
            // up any lingering noise-event notification so the drawer is empty.
            GetNotificationService()?.Pause();
            (GetSystemService(NotificationService) as NotificationManager)
                ?.Cancel(Services.NotificationService.EventNotificationId);

            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // If the user disabled notifications while Android was restarting
        // the sticky service, bail out immediately.
        var notificationService = GetNotificationService();
        if (notificationService is null || !notificationService.IsEnabled)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            // Notification channels and foreground service require API 26+.
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        EnsureNotificationChannel();

        var notification = BuildServiceNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(ServiceNotificationId, notification,
                Android.Content.PM.ForegroundService.TypeDataSync);
        else
            StartForeground(ServiceNotificationId, notification);

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = RunPollLoopAsync(_cts.Token);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        base.OnDestroy();
    }

    async Task RunPollLoopAsync(CancellationToken ct)
    {
        var client = IPlatformApplication.Current?.Services.GetRequiredService<SensorPalClient>();
        var notificationService = GetNotificationService();

        while (!ct.IsCancellationRequested)
        {
            // Poll less aggressively when the screen is off — the user won't
            // see the notification immediately anyway.
            var pm = GetSystemService(PowerService) as Android.OS.PowerManager;
            var interval = pm?.IsInteractive == true
                ? TimeSpan.FromSeconds(30)
                : TimeSpan.FromMinutes(5);

            try
            {
                await Task.Delay(interval, ct);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            try
            {
                if (client is null || notificationService is null) continue;
                var level = await client.GetLevelAsync();

                // level == null means the server is unreachable — keep running.
                // level != null but no active session means monitoring has ended — stop.
                if (level is not null && !level.ActiveSessionStartedAt.HasValue)
                {
                    StopSelf();
                    return;
                }

                await notificationService.NotifyIfNewEventAsync(level);
            }
            catch { /* server unreachable — ignore */ }
        }
    }

    static NotificationService? GetNotificationService() =>
        IPlatformApplication.Current?.Services.GetService<NotificationService>();


    [System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
    void EnsureNotificationChannel()
    {
        var mgr = GetSystemService("notification") as NotificationManager;
        if (mgr?.GetNotificationChannel(ChannelId) is not null) return;

        var channel = new NotificationChannel(
            ChannelId,
            "SensorPal Monitoring",
            NotificationImportance.Low)
        {
            Description = "Keeps SensorPal running in background for noise-event notifications"
        };
        mgr?.CreateNotificationChannel(channel);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
    Notification BuildServiceNotification()
    {
        var openIntent = new Intent(this, typeof(MainActivity));
        openIntent.SetFlags(ActivityFlags.SingleTop);
        var openPending = PendingIntent.GetActivity(
            this, 0, openIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

        var stopIntent = new Intent(this, typeof(SensorPalForegroundService));
        stopIntent.SetAction(ActionStopTracking);
        var stopPending = PendingIntent.GetService(
            this, 1, stopIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

        var stopAction = new Notification.Action.Builder(
                Android.Graphics.Drawables.Icon.CreateWithResource(
                    this, Android.Resource.Drawable.IcMenuCloseClearCancel),
                "Pause notifications",
                stopPending)
            .Build();

        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetContentTitle("SensorPal")
            .SetContentText("Monitoring active — tap to open")
            .SetContentIntent(openPending)
            .SetOngoing(true)
            .AddAction(stopAction)
            .Build()!;
    }
}
