using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Pages;

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

    async void OnAboutClicked(object? sender, EventArgs e)
        => await Navigation.PushModalAsync(new AboutPage());
}
