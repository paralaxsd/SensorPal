using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile;

public partial class AppShell
{
    readonly ConnectivityDialogService _dialog;
    bool _started;

    public AppShell(ConnectivityDialogService dialog)
    {
        _dialog = dialog;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        _dialog.Start();
    }

    public void CheckConnectivityOnResume() => _ = _dialog.CheckOnResumeAsync();
}
