using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;

#if ANDROID
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using Plugin.LocalNotification;
#endif

namespace SensorPal.Mobile.Services;

/// <summary>
/// Manages noise-event push notifications.
/// Preference-backed (per device, opt-in, default off).
///
/// Platform strategy:
///   Android  — permission request + foreground service (API 26+) handle polling.
///   Windows  — self-contained 5-second PeriodicTimer; WinRT toast (best-effort).
///   Other    — no-op with a warning logged.
/// </summary>
public sealed class NotificationService : IDisposable
{
    const string EnabledKey = "notifications_enabled";
    const int EventNotificationId = 42;

    readonly SensorPalClient _client;
    readonly ILogger<NotificationService> _logger;

    // -1 = not yet observed; prevents spurious notification on first poll.
    int _lastKnownEventCount = -1;
    CancellationTokenSource? _cts;

    public NotificationService(SensorPalClient client, ILogger<NotificationService> logger)
    {
        _client = client;
        _logger = logger;

        // On non-Android platforms: resume the poll loop if the preference
        // was already enabled in a previous session.
#if !ANDROID
        if (IsEnabled)
            StartPollLoop();
#endif
    }

    public bool IsEnabled
    {
        get => Preferences.Get(EnabledKey, false);
        set => Preferences.Set(EnabledKey, value);
    }

    /// <summary>
    /// Requests required permissions and starts background monitoring.
    /// Returns false when a required permission was denied (caller should revert the UI).
    /// </summary>
    public async Task<bool> TryEnableAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                _logger.LogWarning(
                    "Notifications disabled: POST_NOTIFICATIONS permission denied by user");
                return false;
            }
        }

        IsEnabled = true;
#else
        IsEnabled = true;
        ResetEventCount();
        StartPollLoop();
        await Task.CompletedTask;
#endif
        return true;
    }

    /// <summary>Disables notifications and stops all background monitoring.</summary>
    public void Disable()
    {
        IsEnabled = false;
        ResetEventCount();
#if ANDROID
        StopAndroidForegroundService();
#else
        StopPollLoop();
#endif
    }

    /// <summary>
    /// Called when a monitoring session starts.
    /// Starts the Android foreground service if notifications are enabled.
    /// </summary>
    public void OnMonitoringStarted()
    {
        ResetEventCount();
#if ANDROID
        if (IsEnabled)
            StartAndroidForegroundService();
#endif
    }

    /// <summary>
    /// Called when a monitoring session ends.
    /// Stops the Android foreground service immediately.
    /// </summary>
    public void OnMonitoringStopped()
    {
#if ANDROID
        StopAndroidForegroundService();
#endif
    }

    /// <summary>
    /// Compares the current event count against the last known value and fires
    /// a notification if it genuinely increased. Safe to call concurrently from
    /// the UI poll (150 ms) and the foreground-service poll (5 s) — the worst-
    /// case race is a harmless duplicate notification.
    /// </summary>
    public async Task NotifyIfNewEventAsync(LiveLevelDto? level)
    {
        if (!IsEnabled) return;
        if (level?.ActiveSessionEventCount is not { } count) return;

        var prev = _lastKnownEventCount;
        _lastKnownEventCount = count;

        if (prev >= 0 && count > prev)
            await SendNotificationAsync(
                "Noise Detected",
                count == 1 ? "First event in current session"
                           : $"{count} events detected this session",
                EventNotificationId);
    }

    /// <summary>Resets the tracked count (call when a new session begins).</summary>
    public void ResetEventCount() => _lastKnownEventCount = -1;

    public void Dispose() => StopPollLoop();

    // -- private helpers --

    void StartPollLoop()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = RunPollLoopAsync(_cts.Token);
    }

    void StopPollLoop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    async Task RunPollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var level = await _client.GetLevelAsync();
                await NotifyIfNewEventAsync(level);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notification poll failed");
            }
        }
    }

    async Task SendNotificationAsync(string title, string body, int id)
    {
#if ANDROID
        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = body,
        };
        await LocalNotificationCenter.Current.Show(request);
#elif WINDOWS
        SendWindowsToast(title, body);
        await Task.CompletedTask;
#else
        _logger.LogWarning(
            "SendNotificationAsync: platform not supported, notification suppressed " +
            "(title={Title})", title);
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    void StartAndroidForegroundService()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _logger.LogWarning(
                "Cannot start notification service: foreground services require " +
                "Android 8.0 (API 26) or later");
            return;
        }

        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            _logger.LogWarning(
                "Cannot start notification service: Platform.CurrentActivity is null");
            return;
        }

        var intent = new Intent(activity, typeof(SensorPalForegroundService));
        activity.StartForegroundService(intent);
    }

    void StopAndroidForegroundService()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null) return;
        var intent = new Intent(activity, typeof(SensorPalForegroundService));
        activity.StopService(intent);
    }
#endif

#if WINDOWS
    void SendWindowsToast(string title, string body)
    {
        try
        {
            var xml = Windows.UI.Notifications.ToastNotificationManager
                .GetTemplateContent(Windows.UI.Notifications.ToastTemplateType.ToastText02);
            var nodes = xml.GetElementsByTagName("text");
            nodes[0].AppendChild(xml.CreateTextNode(title));
            nodes[1].AppendChild(xml.CreateTextNode(body));
            var toast = new Windows.UI.Notifications.ToastNotification(xml);
            // For unpackaged MAUI apps the notifier ID must match the app's AUMID.
            // This may silently fail; proper support requires COM server registration.
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier("SensorPal").Show(toast);
        }
        catch (Exception)
        {
            // Swallow — Windows toast is best-effort for unpackaged apps.
        }
    }
#endif
}
