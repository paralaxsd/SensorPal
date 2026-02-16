using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile;

public partial class AppShell : Shell
{
    readonly ConnectivityService _connectivity;
    bool _dialogVisible;

    public AppShell(ConnectivityService connectivity)
    {
        _connectivity = connectivity;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        InitializeComponent();
        _connectivity.Start();
    }

    void OnConnectivityChanged(bool isReachable)
    {
        if (isReachable || _dialogVisible) return;
        MainThread.BeginInvokeOnMainThread(() => _ = ShowOfflineDialogAsync());
    }

    async Task ShowOfflineDialogAsync()
    {
        if (_dialogVisible) return;
        _dialogVisible = true;

        try
        {
            while (!_connectivity.IsServerReachable)
            {
                bool retry = await DisplayAlertAsync(
                    "Server Unreachable",
                    "The SensorPal server could not be reached.\n\n" +
                    "Make sure the server is running and your device is on the same network.",
                    "Retry",
                    "Dismiss");

                if (!retry) break;

                await _connectivity.CheckNowAsync();
            }
        }
        finally
        {
            _dialogVisible = false;
        }
    }
}
