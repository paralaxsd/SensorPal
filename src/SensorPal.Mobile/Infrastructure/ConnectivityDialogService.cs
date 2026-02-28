using Microsoft.Extensions.Logging;
using Application = Microsoft.Maui.Controls.Application;

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
    ConnectivityService connectivity, ILogger<ConnectivityDialogService> logger)
{
    enum DialogAction { Retry, Quit, Settings }

    bool _dialogVisible;
    bool _started;
    bool _resumeGrace;

    /// <summary>Called when the user taps "Settings" in the offline dialog.</summary>
    public Func<Task>? OpenSettingsAsync { get; set; }

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
        logger.LogDebug("CheckOnResumeAsync: start (dialogVisible={Dialog})", _dialogVisible);

        // Suppress OnConnectivityChanged-triggered dialogs for the duration of this check.
        // Without this flag, CheckNowAsync firing UpdateState(false) → ConnectivityChanged →
        // OnConnectivityChanged would post ShowOfflineDialogAsync to the main-thread queue;
        // Task.Delay then yields the thread, letting the dialog show before the retry fires.
        _resumeGrace = true;
        try
        {
            await connectivity.CheckNowAsync();
            logger.LogDebug("CheckOnResumeAsync: first ping — reachable={Reachable}", connectivity.IsServerReachable);

            if (connectivity.IsServerReachable || _dialogVisible) return;

            // Android's network stack needs a moment to fully recover after Doze mode or
            // process resume. Wait briefly and retry before interrupting the user with a dialog.
            logger.LogDebug("CheckOnResumeAsync: waiting 1.5 s for network recovery");
            await Task.Delay(1500);

            await connectivity.CheckNowAsync();
            logger.LogDebug("CheckOnResumeAsync: second ping — reachable={Reachable}", connectivity.IsServerReachable);
        }
        finally
        {
            _resumeGrace = false;
        }

        if (!connectivity.IsServerReachable && !_dialogVisible)
        {
            logger.LogWarning("CheckOnResumeAsync: server still unreachable after grace period — showing dialog");
            MainThread.BeginInvokeOnMainThread(() => _ = ShowOfflineDialogAsync());
        }
    }

    void OnConnectivityChanged(bool isReachable)
    {
        logger.LogDebug("OnConnectivityChanged: reachable={Reachable} dialogVisible={Dialog} resumeGrace={Grace}",
            isReachable, _dialogVisible, _resumeGrace);
        if (isReachable || _dialogVisible || _resumeGrace) return;

        // Don't show the dialog while any modal page is on top (e.g. Settings).
        // The user is actively configuring the app and needs to finish first.
        var windows = Application.Current?.Windows;
        var modalStack = windows is { Count: > 0 } ? windows[0].Page?.Navigation?.ModalStack : null;
        if (modalStack is { Count: > 0 }) return;

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
            while (true)
            {
                DialogAction action;
                try
                {
                    action = await ShowConnectivityAlertAsync();
                    logger.LogDebug("Alert result: {Action}", action);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Alert threw an exception");
                    return;
                }

                if (action == DialogAction.Quit)
                {
                    Environment.Exit(0);
                    return;
                }

                if (action == DialogAction.Settings && OpenSettingsAsync is { } openSettings)
                    await openSettings();

                await connectivity.CheckNowAsync();
                if (connectivity.IsServerReachable) break;
            }
        }
        finally
        {
            _dialogVisible = false;
        }
    }

    // MAUI's DisplayAlertAsync has a known issue in .NET 10 AOT/Release builds where the
    // backing TaskCompletionSource is never resolved. On Android we bypass the MAUI layer
    // and use AlertDialog directly via Platform.CurrentActivity.
    async Task<DialogAction> ShowConnectivityAlertAsync()
    {
#if ANDROID
        var tcs = new TaskCompletionSource<DialogAction>();

        var activity = (Activity?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            logger.LogError("No current Android Activity — cannot show alert");
            return DialogAction.Retry;
        }

        activity.RunOnUiThread(() =>
        {
            _activeDialog = new AlertDialog.Builder(activity)
                .SetTitle("Server Unreachable")!
                .SetMessage(
                    "The SensorPal server could not be reached.\n\n" +
                    "Make sure the server is running and your device is on the same network.")!
                .SetPositiveButton("Retry", (_, _) => tcs.TrySetResult(DialogAction.Retry))!
                .SetNeutralButton("Settings", (_, _) => tcs.TrySetResult(DialogAction.Settings))!
                .SetNegativeButton("Quit App", (_, _) => tcs.TrySetResult(DialogAction.Quit))!
                .SetCancelable(false)!
                .Create()!;

            _activeDialog.Show();
            // Override button text color — default MAUI theme renders it in an unreadable purple.
            var buttonColor = Android.Graphics.Color.Rgb(25, 118, 210); // Material Blue 700
            _activeDialog.GetButton(-1)?.SetTextColor(buttonColor);  // BUTTON_POSITIVE
            _activeDialog.GetButton(-3)?.SetTextColor(buttonColor);  // BUTTON_NEUTRAL
            _activeDialog.GetButton(-2)?.SetTextColor(buttonColor);  // BUTTON_NEGATIVE
        });

        var result = await tcs.Task;
        _activeDialog = null;
        return result;
#elif WINDOWS
        // WinUI ContentDialog renders all three buttons side-by-side in the footer,
        // matching the Android AlertDialog layout.
        const string message =
            "The SensorPal server could not be reached.\n\n" +
            "Make sure the server is running and your device is on the same network.";

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Server Unreachable",
            Content = message,
            PrimaryButtonText = "Retry",
            SecondaryButtonText = "Settings",
            CloseButtonText = "Quit App",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
        };

        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            is Microsoft.UI.Xaml.Window winUIWindow)
        {
            dialog.XamlRoot = winUIWindow.Content.XamlRoot;
            var result = await dialog.ShowAsync();
            return result switch
            {
                Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary => DialogAction.Retry,
                Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary => DialogAction.Settings,
                _ => DialogAction.Quit
            };
        }

        return DialogAction.Retry;
#else
        // iOS / macCatalyst fallback: DisplayActionSheetAsync supports 3+ options.
        var page = await WaitForCurrentPageAsync();
        logger.LogDebug("Alert target: {PageType}", page?.GetType().Name ?? "null");

        if (page is null) return DialogAction.Retry;

        var action = await page.DisplayActionSheetAsync(
            "Server Unreachable — Make sure the server is running and on the same network.",
            "Quit App",
            null,
            "Retry",
            "Settings");

        return action switch
        {
            "Settings" => DialogAction.Settings,
            "Quit App" => DialogAction.Quit,
            _ => DialogAction.Retry
        };
#endif
    }

    // Shell.OnAppearing fires before CurrentPage is set. Awaiting here yields the main
    // thread so Shell's own navigation continuation can run and set CurrentPage.
    async Task<Page?> WaitForCurrentPageAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
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
