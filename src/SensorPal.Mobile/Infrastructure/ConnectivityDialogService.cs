using Microsoft.Extensions.Logging;

#if ANDROID
using Android.App;
#endif

namespace SensorPal.Mobile.Infrastructure;

/// <summary>
/// Monitors server reachability and shows a blocking retry/quit dialog when the server
/// cannot be reached. Owns the full connectivity-dialog lifecycle so that AppShell stays
/// focused on navigation.
/// </summary>
public sealed class ConnectivityDialogService(
    ConnectivityService connectivity,
    ILogger<ConnectivityDialogService> logger)
{
    bool _dialogVisible;
    bool _started;

#if ANDROID
    // Strong managed reference prevents GC of the dialog's Java callback peers in AOT builds.
    AlertDialog? _activeDialog;
#endif

    public void Start()
    {
        if (_started) return;
        _started = true;

        // Subscribe before Start() so we don't miss an event fired during the first check.
        connectivity.ConnectivityChanged += OnConnectivityChanged;
        connectivity.Start();

        // ReportResult(false) may have already been called (e.g. from a page-constructor
        // HTTP request) before we subscribed, so the event was never fired. Check manually.
        if (!connectivity.IsServerReachable && !_dialogVisible)
            _ = ShowOfflineDialogAsync();
    }

    public async Task CheckOnResumeAsync()
    {
        // Do a fresh ping first — the cached IsServerReachable may be stale due to Android
        // Doze mode suppressing background network requests while the screen was off.
        await connectivity.CheckNowAsync();

        if (!connectivity.IsServerReachable && !_dialogVisible)
            MainThread.BeginInvokeOnMainThread(() => _ = ShowOfflineDialogAsync());
    }

    void OnConnectivityChanged(bool isReachable)
    {
        logger.LogDebug("Connectivity changed to {Reachable}", isReachable);
        if (isReachable || _dialogVisible) return;
        MainThread.BeginInvokeOnMainThread(() => _ = ShowOfflineDialogAsync());
    }

    async Task ShowOfflineDialogAsync()
    {
        if (_dialogVisible)
        {
            logger.LogWarning("Already showing connectivity dialog, skipping");
            return;
        }

        _dialogVisible = true;
        logger.LogWarning("Showing connectivity dialog");

        try
        {
            do
            {
                bool retry;
                try
                {
                    retry = await ShowConnectivityAlertAsync();
                    logger.LogDebug("Alert result: retry={Retry}", retry);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Alert threw an exception");
                    return;
                }

                if (!retry)
                {
                    Environment.Exit(0);
                    return;
                }

                await connectivity.CheckNowAsync();
            }
            while (!connectivity.IsServerReachable);
        }
        finally
        {
            _dialogVisible = false;
        }
    }

    // MAUI's DisplayAlertAsync has a known issue in .NET 10 AOT/Release builds where the
    // backing TaskCompletionSource is never resolved. On Android we bypass the MAUI layer
    // and use AlertDialog directly via Platform.CurrentActivity.
    async Task<bool> ShowConnectivityAlertAsync()
    {
#if ANDROID
        var tcs = new TaskCompletionSource<bool>();

        var activity = (Activity?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            logger.LogError("No current Android Activity — cannot show alert");
            return false;
        }

        activity.RunOnUiThread(() =>
        {
            _activeDialog = new AlertDialog.Builder(activity)
                .SetTitle("Server Unreachable")!
                .SetMessage(
                    "The SensorPal server could not be reached.\n\n" +
                    "Make sure the server is running and your device is on the same network.")!
                .SetPositiveButton("Retry", (_, _) => tcs.TrySetResult(true))!
                .SetNegativeButton("Quit App", (_, _) => tcs.TrySetResult(false))!
                .SetCancelable(false)!
                .Create()!;

            _activeDialog.Show();
        });

        var result = await tcs.Task;
        _activeDialog = null;
        return result;
#else
        // Shell.OnAppearing fires before CurrentPage is set — wait for initial navigation.
        var page = await WaitForCurrentPageAsync();
        logger.LogDebug("Alert target: {PageType}", page?.GetType().Name ?? "null");

        return page is not null && await page.DisplayAlertAsync(
            "Server Unreachable",
            "The SensorPal server could not be reached.\n\n" +
            "Make sure the server is running and your device is on the same network.",
            "Retry",
            "Quit App");
#endif
    }

    // Shell.OnAppearing fires before CurrentPage is set. Awaiting here yields the main
    // thread so Shell's own navigation continuation can run and set CurrentPage.
    async Task<Page?> WaitForCurrentPageAsync()
    {
        var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not Shell shell) return page;

        const int maxAttempts = 20;
        for (int i = 0; i < maxAttempts && shell.CurrentPage is null; i++)
        {
            logger.LogDebug("Waiting for Shell.CurrentPage ({Attempt}/{Max})", i + 1, maxAttempts);
            await Task.Delay(100);
        }

        var current = shell.CurrentPage;
        if (current is null)
            logger.LogWarning("Shell.CurrentPage still null after {Ms}ms, falling back to Shell",
                maxAttempts * 100);

        return current ?? shell;
    }
}
