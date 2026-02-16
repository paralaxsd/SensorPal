using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Infrastructure;

#if ANDROID
using Android.App;
#endif

namespace SensorPal.Mobile;

public partial class AppShell
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly ConnectivityService _connectivity;
    readonly ILogger<AppShell> _logger;
    bool _dialogVisible;
    bool _started;

#if ANDROID
    // Strong managed reference prevents GC of the dialog's Java callback peers in AOT builds.
    AlertDialog? _activeDialog;
#endif

    /******************************************************************************************
     * STRUCTORS
     * ***************************************************************************************/
    public AppShell(ConnectivityService connectivity, ILogger<AppShell> logger)
    {
        _connectivity = connectivity;
        _logger = logger;
        InitializeComponent();
    }

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_started) { return; }
        _started = true;

        // Subscribe here, not in the constructor. Page constructors fire HTTP requests that
        // can call ReportResult(false) before Shell.CurrentPage is set. If the event were
        // subscribed earlier, ShowOfflineDialogAsync would run with CurrentPage == null and
        // DisplayAlertAsync would hang indefinitely on the Shell container.
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        _connectivity.Start();

        // ReportResult(false) may already have been called (from a page constructor HTTP
        // request) before we subscribed, so the event was never fired. Check manually.
        if (!_connectivity.IsServerReachable && !_dialogVisible)
            _ = ShowOfflineDialogAsync();
    }

    public void CheckConnectivityOnResume()
    {
        if (!_connectivity.IsServerReachable && !_dialogVisible)
            MainThread.BeginInvokeOnMainThread(() => _ = ShowOfflineDialogAsync());
    }

    void OnConnectivityChanged(bool isReachable)
    {
        _logger.LogDebug("Connectivity changed to {Reachable}", isReachable);

        if (isReachable || _dialogVisible) return;
        MainThread.BeginInvokeOnMainThread(() => _ = ShowOfflineDialogAsync());
    }

    async Task ShowOfflineDialogAsync()
    {
        if (_dialogVisible)
        {
            _logger.LogWarning("Already showing connectivity dialog, skipping");
            return;
        }

        _dialogVisible = true;
        _logger.LogWarning("Showing connectivity dialog");

        try
        {
            do
            {
                bool retry;
                try
                {
                    retry = await ShowConnectivityAlertAsync();
                    _logger.LogDebug("Alert result: retry={Retry}", retry);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alert threw an exception");
                    return;
                }

                if (!retry)
                {
                    Environment.Exit(0);
                    return;
                }

                await _connectivity.CheckNowAsync();
            }
            while (!_connectivity.IsServerReachable);
        }
        finally
        {
            _dialogVisible = false;
        }
    }

    // MAUI's DisplayAlertAsync has a known issue in .NET 10 AOT/Release builds where the
    // backing TaskCompletionSource is never resolved (dialog created but never shown or
    // callbacks GC'd). On Android we bypass the MAUI layer and use AlertDialog directly.
    async Task<bool> ShowConnectivityAlertAsync()
    {
#if ANDROID
        var tcs = new TaskCompletionSource<bool>();

        var activity = (Activity?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            _logger.LogError("No current Android Activity — cannot show alert");
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
        _logger.LogDebug("Alert target: {PageType}", page?.GetType().Name ?? "null");

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
            _logger.LogDebug("Waiting for Shell.CurrentPage ({Attempt}/{Max})", i + 1, maxAttempts);
            await Task.Delay(100);
        }

        var current = shell.CurrentPage;
        if (current is null)
            _logger.LogWarning("Shell.CurrentPage still null after {Ms}ms, falling back to Shell",
                maxAttempts * 100);

        return current ?? shell;
    }
}
